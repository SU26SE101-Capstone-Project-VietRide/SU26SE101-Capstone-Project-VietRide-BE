using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Jobs;
using VietRide.Trip.Infrastructure.Messaging;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class ShuttlePersistenceIntegrationTests
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);

    private const string PreviousMigration = "20260710000000_AddVehicleImageUrls";
    private const string PreviousAuditMigration = "20260821180959_AddShuttlePassengerBookingCode";

    [Fact]
    public async Task Migration_UpDownAndReapply_CreatesCanonicalShuttleTables()
    {
        var databaseName = $"vietride_trip_shuttle_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName, new SystemClock());

        try
        {
            await db.Database.MigrateAsync();
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeTrue();
            (await TableExistsAsync(db, "shuttle_passengers")).Should().BeTrue();
            (await TableExistsAsync(db, "shuttle_dispatch_alerts")).Should().BeTrue();

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await TableExistsAsync(db, "shuttle_trips")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConfirmedFanOut_ConcurrentReplay_CreatesOneManifestPerTicket()
    {
        var databaseName = $"vietride_trip_shuttle_fanout_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var setup = CreateDbContext(databaseName, clock);

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, now.AddHours(4));
            var bookingId = Guid.NewGuid();
            var passengerId = Guid.NewGuid();
            var tickets = Enumerable.Range(0, 3)
                .Select(_ => new BookingShuttleConfirmedIntegrationEvent.ConfirmedTicket(Guid.NewGuid(), passengerId))
                .ToArray();
            var integrationEvent = new BookingShuttleConfirmedIntegrationEvent
            {
                BookingId = bookingId,
                BookingCode = "VR-20260822-ABCDEFGH",
                TripId = seed.MainTripId,
                UserId = passengerId,
                Tickets = tickets,
                ShuttlePickup = new BookingShuttleConfirmedIntegrationEvent.ShuttlePickupPayload(
                    "12 Nguyen Hue, District 1",
                    10.7731m,
                    106.7032m),
            };

            await using var firstDb = CreateDbContext(databaseName, clock);
            await using var secondDb = CreateDbContext(databaseName, clock);
            var first = CreateConfirmedHandler(firstDb);
            var second = CreateConfirmedHandler(secondDb);

            await Task.WhenAll(
                first.HandleAsync(integrationEvent, CancellationToken.None),
                second.HandleAsync(integrationEvent, CancellationToken.None));

            await using var assertionDb = CreateDbContext(databaseName, clock);
            var manifests = await assertionDb.ShuttlePassengers.AsNoTracking()
                .Where(x => x.BookingId == bookingId)
                .ToArrayAsync();
            manifests.Should().HaveCount(3);
            manifests.Should().OnlyContain(x => x.BookingCode == "VR-20260822-ABCDEFGH");
            manifests.Select(x => x.TicketId).Should().OnlyHaveUniqueItems();
            manifests.Should().OnlyContain(x => x.Status == ShuttlePassenger.PendingAssignmentStatus);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConfirmedFanOut_RealInbox_CommitsMarkerAndManifests_ThenReplayIsDuplicate()
    {
        var databaseName = $"vietride_trip_shuttle_inbox_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var setup = CreateDbContext(databaseName, clock);

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, now.AddHours(4));
            var integrationEvent = CreateConfirmedEvent(seed.MainTripId, ticketCount: 3);
            var messageId = Guid.NewGuid();
            const string consumerName = "trip.booking-shuttle-confirmed";
            const string payloadHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

            await using (var deliveryDb = CreateDbContext(databaseName, clock))
            {
                var unitOfWork = new EfUnitOfWork(deliveryDb);
                var handler = CreateConfirmedHandler(deliveryDb, unitOfWork);
                var inbox = new EfIntegrationEventInbox<TripDbContext>(deliveryDb, unitOfWork, clock);

                var result = await inbox.ExecuteAsync(
                    consumerName,
                    messageId,
                    payloadHash,
                    cancellationToken => handler.HandleAsync(integrationEvent, cancellationToken),
                    CancellationToken.None);

                result.Should().Be(IntegrationEventInboxResult.Processed);
            }

            await using (var assertionDb = CreateDbContext(databaseName, clock))
            {
                (await assertionDb.ShuttlePassengers.AsNoTracking()
                    .CountAsync(x => x.BookingId == integrationEvent.BookingId)).Should().Be(3);
                (await assertionDb.Set<IntegrationInboxRecord>().AsNoTracking()
                    .CountAsync(x => x.ConsumerName == consumerName && x.MessageId == messageId))
                    .Should().Be(1);
            }

            await using (var replayDb = CreateDbContext(databaseName, clock))
            {
                var unitOfWork = new EfUnitOfWork(replayDb);
                var handler = CreateConfirmedHandler(replayDb, unitOfWork);
                var inbox = new EfIntegrationEventInbox<TripDbContext>(replayDb, unitOfWork, clock);
                var handlerCalled = false;

                var result = await inbox.ExecuteAsync(
                    consumerName,
                    messageId,
                    payloadHash,
                    async cancellationToken =>
                    {
                        handlerCalled = true;
                        await handler.HandleAsync(integrationEvent, cancellationToken);
                    },
                    CancellationToken.None);

                result.Should().Be(IntegrationEventInboxResult.Duplicate);
                handlerCalled.Should().BeFalse();
            }

            await using var replayAssertionDb = CreateDbContext(databaseName, clock);
            (await replayAssertionDb.ShuttlePassengers.AsNoTracking()
                .CountAsync(x => x.BookingId == integrationEvent.BookingId)).Should().Be(3);
            (await replayAssertionDb.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(x => x.ConsumerName == consumerName && x.MessageId == messageId))
                .Should().Be(1);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetPendingAsync_ProjectsRoutePassengersAndCompletePagination()
    {
        var databaseName = $"vietride_trip_shuttle_pending_projection_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var inboundBookingId = Guid.NewGuid();
            var outboundBookingId = Guid.NewGuid();
            var profiledPassengerId = Guid.NewGuid();
            var missingProfilePassengerId = Guid.NewGuid();
            var inboundTicketIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var outboundTicketId = Guid.NewGuid();

            db.ShuttlePassengers.AddRange(
                ShuttlePassenger.Request(
                    seed.MainTripId,
                    inboundBookingId,
                    inboundTicketIds[0],
                    profiledPassengerId,
                    "12 Nguyen Hue, District 1",
                    10.7731m,
                    106.7032m,
                    ShuttlePassenger.InboundDirection,
                    1_000,
                    "VR-20260822-ABCDEFGH"),
                ShuttlePassenger.Request(
                    seed.MainTripId,
                    inboundBookingId,
                    inboundTicketIds[1],
                    profiledPassengerId,
                    "12 Nguyen Hue, District 1",
                    10.7731m,
                    106.7032m,
                    ShuttlePassenger.InboundDirection,
                    1_000,
                    "VR-20260822-ABCDEFGH"),
                ShuttlePassenger.Request(
                    seed.MainTripId,
                    outboundBookingId,
                    outboundTicketId,
                    missingProfilePassengerId,
                    "45 Le Loi, District 1",
                    10.7722m,
                    106.6980m,
                    ShuttlePassenger.OutboundDirection,
                    900,
                    "VR-20260822-HGFEDCBA"));
            var originStationId = (await db.Routes.SingleAsync(route => route.Id == seed.RouteId)).OriginStationId;
            var assignedTrip = ShuttleTrip.Create(
                seed.OperatorId,
                seed.MainTripId,
                originStationId,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(2),
                now.AddHours(3),
                null);
            var assignedPassenger = ShuttlePassenger.Request(
                seed.MainTripId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                profiledPassengerId,
                "20 Pasteur, District 1",
                10.7750m,
                106.7000m,
                ShuttlePassenger.InboundDirection,
                1_200,
                "VR-20260822-ZYXWVUTS");
            assignedPassenger.Assign(assignedTrip.Id, 1);
            db.AddRange(assignedTrip, assignedPassenger);
            await db.SaveChangesAsync();

            var service = CreateDispatchService(
                db,
                clock,
                seed.OperatorId,
                new HashSet<Guid> { missingProfilePassengerId });

            var firstPage = await service.GetPendingAsync(
                seed.OperatorId,
                page: 1,
                pageSize: 1,
                CancellationToken.None);
            var secondPage = await service.GetPendingAsync(
                seed.OperatorId,
                page: 2,
                pageSize: 1,
                CancellationToken.None);

            firstPage.Items.Should().ContainSingle();
            firstPage.TotalItems.Should().Be(2);
            firstPage.TotalPages.Should().Be(2);
            firstPage.HasNextPage.Should().BeTrue();
            firstPage.HasPreviousPage.Should().BeFalse();
            firstPage.Summary.TotalPendingPassengerCount.Should().Be(3);
            firstPage.Summary.TotalPendingGroupCount.Should().Be(2);
            var inbound = firstPage.Items.Single();
            inbound.RouteName.Should().Be("Shuttle integration route");
            inbound.Direction.Should().Be(ShuttlePassenger.InboundDirection);
            inbound.BookingGroups.Should().ContainSingle();
            var inboundGroup = inbound.BookingGroups.Single();
            inboundGroup.BookingCode.Should().Be("VR-20260822-ABCDEFGH");
            inbound.AssignedPassengerCount.Should().Be(1);
            inbound.TotalShuttlePassengerCount.Should().Be(3);
            inbound.DispatchedShuttleTripCount.Should().Be(1);
            inboundGroup.Passengers.Should().NotBeNull().And.ContainSingle();
            var profiledPassenger = inboundGroup.Passengers.Single();
            profiledPassenger.PassengerUserId.Should().Be(profiledPassengerId);
            profiledPassenger.DisplayName.Should().Be("Shuttle Passenger");
            profiledPassenger.Phone.Should().Be("0900000000");
            profiledPassenger.TicketIds.Should().BeEquivalentTo(inboundTicketIds);

            secondPage.Items.Should().ContainSingle();
            secondPage.TotalItems.Should().Be(2);
            secondPage.TotalPages.Should().Be(2);
            secondPage.HasNextPage.Should().BeFalse();
            secondPage.HasPreviousPage.Should().BeTrue();
            var outbound = secondPage.Items.Single();
            outbound.RouteName.Should().Be("Shuttle integration route");
            outbound.Direction.Should().Be(ShuttlePassenger.OutboundDirection);
            var missingProfilePassenger = outbound.BookingGroups.Single().Passengers.Single();
            missingProfilePassenger.PassengerUserId.Should().Be(missingProfilePassengerId);
            missingProfilePassenger.DisplayName.Should().BeNull();
            missingProfilePassenger.Phone.Should().BeNull();
            missingProfilePassenger.TicketIds.Should().Equal(outboundTicketId);

            var filteredByTripDeparture = await service.GetPendingFilteredAsync(
                seed.OperatorId,
                page: 1,
                pageSize: 20,
                fromUtc: now.AddHours(3),
                toUtcExclusive: now.AddHours(5),
                mainTripId: null,
                search: null,
                passengerUserIds: [],
                CancellationToken.None);
            filteredByTripDeparture.Items.Should().HaveCount(2);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetPendingAsync_EmptyResultReturnsZeroPageMetadataAndEmptyItems()
    {
        var databaseName = $"vietride_trip_shuttle_pending_empty_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var service = CreateDispatchService(db, clock, seed.OperatorId);

            var result = await service.GetPendingAsync(
                Guid.NewGuid(),
                page: 1,
                pageSize: 20,
                CancellationToken.None);

            result.Items.Should().BeEmpty();
            result.TotalItems.Should().Be(0);
            result.TotalPages.Should().Be(0);
            result.HasNextPage.Should().BeFalse();
            result.HasPreviousPage.Should().BeFalse();
            result.Summary.TotalPendingPassengerCount.Should().Be(0);
            result.Summary.TotalPendingGroupCount.Should().Be(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ProjectsMainTripStationUsableCapacityAndPassengerProgress()
    {
        var databaseName = $"vietride_trip_shuttle_history_projection_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var shuttleVehicle = await db.Vehicles.SingleAsync(vehicle => vehicle.Id == seed.ShuttleVehicleId);
            var shuttleLayout = JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "SHUTTLE_TEST",
                totalSeats = 6,
                rows = 6,
                cols = 1,
                decks = 1,
                aisles = Array.Empty<object>(),
                seats = new object[]
                {
                    CreateSeat("A01", 1, "DRIVER_AREA", disabled: false),
                    CreateSeat("A02", 2, "STANDARD", disabled: true),
                    CreateSeat("A03", 3, "STANDARD", disabled: false),
                    CreateSeat("A04", 4, "STANDARD", disabled: false),
                    CreateSeat("A05", 5, "STANDARD", disabled: false),
                    CreateSeat("A06", 6, "STANDARD", disabled: false),
                },
            });
            shuttleVehicle.UpdateSeatLayout(shuttleLayout, totalSeats: 6);

            var originStation = await db.Stations.SingleAsync(station => station.Name == "Shuttle Origin");
            var shuttleTrip = ShuttleTrip.Create(
                seed.OperatorId,
                seed.MainTripId,
                originStation.Id,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(1),
                now.AddHours(2),
                null);
            var manifests = Enumerable.Range(0, 5)
                .Select(index => ShuttlePassenger.Request(
                    seed.MainTripId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"Pickup {index + 1}",
                    10.7731m,
                    106.7032m))
                .ToArray();
            manifests[0].Assign(shuttleTrip.Id, 1);
            manifests[1].Assign(shuttleTrip.Id, 2);
            manifests[1].MarkPickedUp(now);
            manifests[2].Assign(shuttleTrip.Id, 2);
            manifests[2].MarkPickedUp(now);
            manifests[2].MarkDelivered(now.AddMinutes(1));
            manifests[3].Assign(shuttleTrip.Id, 3);
            manifests[3].MarkNoShow("Passenger unavailable");
            manifests[4].Assign(shuttleTrip.Id, 4);
            manifests[4].Cancel("Booking cancelled");
            db.AddRange(shuttleTrip);
            db.AddRange(manifests);
            await db.SaveChangesAsync();

            var service = CreateDispatchService(db, clock, seed.OperatorId);
            var result = await service.GetHistoryAsync(
                seed.OperatorId,
                page: 1,
                pageSize: 20,
                from: null,
                to: null,
                statuses: null,
                CancellationToken.None);
            var trackingContext = await service.GetTrackingContextAsync(
                shuttleTrip.Id,
                Guid.NewGuid(),
                "OPERATOR_ADMIN",
                seed.OperatorId,
                CancellationToken.None);

            var item = result.Items.Should().ContainSingle().Subject;
            item.MainTrip.Should().Be(new OperatorShuttleMainTripDto(
                seed.MainTripId,
                "Shuttle integration route",
                now.AddHours(4),
                now.AddHours(7),
                now.AddHours(3.5)));
            item.Station.Should().Be(new OperatorShuttleStationDto(originStation.Id, "Shuttle Origin"));
            item.Vehicle.TypeDisplayName.Should().Be("Shuttle integration vehicle");
            item.Vehicle.UsablePassengerCapacity.Should().Be(4);
            item.PassengerCount.Should().Be(4);
            item.StopCount.Should().Be(3);
            item.PassengerProgress.Should().Be(new OperatorShuttlePassengerProgressDto(
                Pending: 1,
                PickedUp: 1,
                Delivered: 1,
                NoShow: 1,
                Cancelled: 1));
            trackingContext.Scope.Should().Be("OPERATOR");
            trackingContext.Stops.Should().Contain(stop =>
                !stop.IsStation
                && stop.Status == ShuttlePassenger.PickedUpStatus
                && stop.PassengerCount == 1
                && stop.PickedUpAt == now
                && stop.DeliveredAt == null
                && stop.StatusReason == null);
            trackingContext.Stops.Should().Contain(stop =>
                !stop.IsStation
                && stop.Status == ShuttlePassenger.DeliveredStatus
                && stop.PassengerCount == 1
                && stop.PickedUpAt == now
                && stop.DeliveredAt == now.AddMinutes(1)
                && stop.StatusReason == null);
            trackingContext.Stops.Should().Contain(stop =>
                !stop.IsStation
                && stop.Status == ShuttlePassenger.NoShowStatus
                && stop.PassengerCount == 1
                && stop.StatusReason == "Passenger unavailable");
            trackingContext.Stops.Should().Contain(stop =>
                !stop.IsStation
                && stop.Status == ShuttlePassenger.CancelledStatus
                && stop.PassengerCount == 1
                && stop.StatusReason == "Booking cancelled");
            trackingContext.Stops.Should().ContainSingle(stop => stop.IsStation)
                .Which.PassengerCount.Should().BeNull();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AuditMigration_UpDownAndReapply_ManagesDedicatedColumns()
    {
        var databaseName = $"vietride_trip_shuttle_audit_migration_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName, new SystemClock());

        try
        {
            await db.Database.MigrateAsync();
            (await ColumnExistsAsync(db, "shuttle_trips", "created_by_user_id")).Should().BeTrue();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancelled_at")).Should().BeTrue();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancel_reason")).Should().BeTrue();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancelled_by_user_id")).Should().BeTrue();

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousAuditMigration);
            (await ColumnExistsAsync(db, "shuttle_trips", "created_by_user_id")).Should().BeFalse();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancelled_at")).Should().BeFalse();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancel_reason")).Should().BeFalse();
            (await ColumnExistsAsync(db, "shuttle_trips", "cancelled_by_user_id")).Should().BeFalse();

            await migrator.MigrateAsync();
            (await ColumnExistsAsync(db, "shuttle_trips", "created_by_user_id")).Should().BeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CreateCancelAndHistory_PersistDedicatedShuttleAuditFieldsWithoutChangingNotes()
    {
        var databaseName = $"vietride_trip_shuttle_audit_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var bookingId = Guid.NewGuid();
            db.ShuttlePassengers.Add(ShuttlePassenger.Request(
                seed.MainTripId,
                bookingId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "12 Nguyen Hue, District 1",
                10.7731m,
                106.7032m,
                roadDistanceMeters: 1_000));
            db.ShuttlePassengers.Add(ShuttlePassenger.Request(
                seed.MainTripId,
                bookingId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "12 Nguyen Hue, District 1",
                10.7731m,
                106.7032m,
                roadDistanceMeters: 1_000));
            await db.SaveChangesAsync();

            var createdByUserId = Guid.NewGuid();
            var cancelledByUserId = Guid.NewGuid();
            var service = CreateDispatchService(db, clock, seed.OperatorId);
            var created = await service.CreateAsync(new CreateShuttleTripInput(
                seed.OperatorId,
                createdByUserId,
                seed.MainTripId,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(1),
                now.AddHours(2),
                [bookingId],
                "Call before pickup"), CancellationToken.None);

            await service.CancelShuttleTripAsync(
                seed.OperatorId,
                created.ShuttleTripId,
                cancelledByUserId,
                "Vehicle unavailable",
                CancellationToken.None);

            var persisted = await db.ShuttleTrips.AsNoTracking()
                .SingleAsync(shuttle => shuttle.Id == created.ShuttleTripId);
            persisted.CreatedByUserId.Should().Be(createdByUserId);
            persisted.CancelledAt.Should().Be(now);
            persisted.CancelReason.Should().Be("Vehicle unavailable");
            persisted.CancelledByUserId.Should().Be(cancelledByUserId);
            persisted.Notes.Should().Be("Call before pickup");
            var cancelledEvents = await db.OutboxEvents.AsNoTracking()
                .Where(item => item.EventType == "trip.shuttle.cancelled")
                .ToArrayAsync();
            cancelledEvents.Should().HaveCount(2);
            cancelledEvents.Count(item =>
            {
                using var payload = JsonDocument.Parse(item.Payload);
                return payload.RootElement.GetProperty("driverUserId").ValueKind == JsonValueKind.String
                    && payload.RootElement.GetProperty("driverUserId").GetGuid() == seed.ShuttleDriverId
                    && payload.RootElement.GetProperty("cancellationScope").GetString() == "SHUTTLE_TRIP";
            }).Should().Be(1);

            var history = await service.GetHistoryAsync(
                seed.OperatorId,
                page: 1,
                pageSize: 20,
                from: null,
                to: null,
                statuses: null,
                CancellationToken.None);
            var item = history.Items.Should().ContainSingle().Subject;
            item.Notes.Should().Be("Call before pickup");
            item.CreatedAt.Should().Be(now);
            item.CreatedBy.Should().Be(createdByUserId);
            item.CancelledAt.Should().Be(now);
            item.CancelReason.Should().Be("Vehicle unavailable");
            item.CancelledBy.Should().Be(cancelledByUserId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetPassengerContacts_GroupsManifestAndEnforcesTenantWithNullableMissingProfiles()
    {
        var databaseName = $"vietride_trip_shuttle_contacts_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var profiledPassengerId = Guid.NewGuid();
            var missingProfilePassengerId = Guid.NewGuid();
            var firstBookingId = Guid.NewGuid();
            var secondBookingId = Guid.NewGuid();
            var originStation = await db.Stations.SingleAsync(station => station.Name == "Shuttle Origin");
            var shuttleTrip = ShuttleTrip.Create(
                seed.OperatorId,
                seed.MainTripId,
                originStation.Id,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(1),
                now.AddHours(2),
                null);
            var firstTicketIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var manifests = new[]
            {
                ShuttlePassenger.Request(
                    seed.MainTripId, firstBookingId, firstTicketIds[0], profiledPassengerId,
                    "12 Nguyen Hue, District 1", 10.7731m, 106.7032m,
                    bookingCode: "VR-20260822-ABCDEFGH"),
                ShuttlePassenger.Request(
                    seed.MainTripId, firstBookingId, firstTicketIds[1], profiledPassengerId,
                    "12 Nguyen Hue, District 1", 10.7731m, 106.7032m,
                    bookingCode: "VR-20260822-ABCDEFGH"),
                ShuttlePassenger.Request(
                    seed.MainTripId, secondBookingId, Guid.NewGuid(), missingProfilePassengerId,
                    "45 Le Loi, District 1", 10.7722m, 106.6980m,
                    bookingCode: "VR-20260822-HGFEDCBA"),
            };
            manifests[0].Assign(shuttleTrip.Id, 2);
            manifests[1].Assign(shuttleTrip.Id, 2);
            manifests[2].Assign(shuttleTrip.Id, 1);
            db.Add(shuttleTrip);
            db.AddRange(manifests);
            await db.SaveChangesAsync();

            var service = CreateDispatchService(
                db,
                clock,
                seed.OperatorId,
                new HashSet<Guid> { missingProfilePassengerId });

            var result = await service.GetPassengerContactsAsync(
                seed.OperatorId,
                shuttleTrip.Id,
                CancellationToken.None);

            result.ShuttleTripId.Should().Be(shuttleTrip.Id);
            result.Groups.Select(group => group.PickupOrder).Should().Equal(1, 2);
            var firstStop = result.Groups[0];
            firstStop.BookingId.Should().Be(secondBookingId);
            firstStop.BookingCode.Should().Be("VR-20260822-HGFEDCBA");
            firstStop.PickupAddress.Should().Be("45 Le Loi, District 1");
            firstStop.PassengerCount.Should().Be(1);
            firstStop.Passengers.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new ShuttlePassengerContactDto(
                    missingProfilePassengerId,
                    null,
                    null,
                    [manifests[2].TicketId!.Value]));
            var secondStop = result.Groups[1];
            secondStop.BookingId.Should().Be(firstBookingId);
            secondStop.PassengerCount.Should().Be(2);
            secondStop.Passengers.Should().ContainSingle();
            secondStop.Passengers[0].DisplayName.Should().Be("Shuttle Passenger");
            secondStop.Passengers[0].Phone.Should().Be("0900000000");
            secondStop.Passengers[0].TicketIds.Should().BeEquivalentTo(firstTicketIds);

            var foreignTenant = async () => await service.GetPassengerContactsAsync(
                Guid.NewGuid(),
                shuttleTrip.Id,
                CancellationToken.None);
            await foreignTenant.Should().ThrowAsync<CodedNotFoundException>()
                .Where(error => error.ErrorCode == "SHUTTLE_TRIP_NOT_FOUND");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetPassengerContacts_IdentityTransportFailure_ReturnsUpstreamUnavailable()
    {
        var databaseName = $"vietride_trip_shuttle_contacts_identity_failure_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, _, created) = await SeedAssignedShuttleAsync(db, clock, now);
            var service = CreateDispatchService(
                db,
                clock,
                seed.OperatorId,
                throwProfileTransportError: true);

            var act = async () => await service.GetPassengerContactsAsync(
                seed.OperatorId,
                created.ShuttleTripId,
                CancellationToken.None);

            await act.Should().ThrowAsync<TripIdentityUnavailableException>()
                .Where(error => error.ErrorCode == "UPSTREAM_UNAVAILABLE" && error.StatusCode == 503);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReassignAsync_ScheduledTrip_AtomicallyReplacesResourcesAndPreservesManifest()
    {
        var databaseName = $"vietride_trip_shuttle_reassign_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, service, created) = await SeedAssignedShuttleAsync(db, clock, now);
            var vehicleTypeId = await db.Vehicles
                .Where(vehicle => vehicle.Id == seed.ShuttleVehicleId)
                .Select(vehicle => vehicle.VehicleTypeId)
                .SingleAsync();
            var replacementVehicle = Vehicle.Create(
                seed.OperatorId,
                vehicleTypeId,
                $"REPL-{Guid.NewGuid():N}"[..20],
                CreateSeatLayout("SHUTTLE_TEST", 3),
                3,
                100m,
                2m);
            db.Vehicles.Add(replacementVehicle);
            await db.SaveChangesAsync();
            var replacementDriverId = Guid.NewGuid();
            var manifestBefore = await db.ShuttlePassengers.AsNoTracking()
                .Where(manifest => manifest.ShuttleTripId == created.ShuttleTripId)
                .OrderBy(manifest => manifest.Id)
                .Select(manifest => new
                {
                    manifest.Id,
                    manifest.BookingId,
                    manifest.TicketId,
                    manifest.PickupOrder,
                    manifest.Status,
                })
                .ToArrayAsync();

            var result = await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    seed.OperatorId,
                    created.ShuttleTripId,
                    replacementDriverId,
                    replacementVehicle.Id,
                    "Original vehicle needs maintenance"),
                CancellationToken.None);
            await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    seed.OperatorId,
                    created.ShuttleTripId,
                    replacementDriverId,
                    replacementVehicle.Id,
                    "Repeat same assignment"),
                CancellationToken.None);

            db.ChangeTracker.Clear();
            var persisted = await db.ShuttleTrips.AsNoTracking()
                .SingleAsync(shuttle => shuttle.Id == created.ShuttleTripId);
            var reservations = await db.ResourceReservations.AsNoTracking()
                .Where(reservation => reservation.ShuttleTripId == created.ShuttleTripId
                    && reservation.Status == ResourceReservationStatus.RESERVED)
                .OrderBy(reservation => reservation.ResourceRole)
                .ToArrayAsync();
            var manifestAfter = await db.ShuttlePassengers.AsNoTracking()
                .Where(manifest => manifest.ShuttleTripId == created.ShuttleTripId)
                .OrderBy(manifest => manifest.Id)
                .Select(manifest => new
                {
                    manifest.Id,
                    manifest.BookingId,
                    manifest.TicketId,
                    manifest.PickupOrder,
                    manifest.Status,
                })
                .ToArrayAsync();
            var reassignedEvent = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == "trip.shuttle.reassigned");
            using var reassignedPayload = JsonDocument.Parse(reassignedEvent.Payload);

            result.Should().Be(new ReassignShuttleTripResult(
                created.ShuttleTripId,
                replacementDriverId,
                replacementVehicle.Id));
            persisted.Status.Should().Be(ShuttleTrip.ScheduledStatus);
            persisted.DriverUserId.Should().Be(replacementDriverId);
            persisted.VehicleId.Should().Be(replacementVehicle.Id);
            reservations.Should().HaveCount(2);
            reservations.Should().ContainSingle(reservation =>
                reservation.ResourceRole == ResourceReservationRole.DRIVER
                && reservation.ResourceId == replacementDriverId);
            reservations.Should().ContainSingle(reservation =>
                reservation.ResourceRole == ResourceReservationRole.VEHICLE
                && reservation.ResourceId == replacementVehicle.Id);
            manifestAfter.Should().BeEquivalentTo(manifestBefore, options => options.WithStrictOrdering());
            reassignedEvent.Status.Should().Be(OutboxEventStatus.PENDING);
            reassignedPayload.RootElement.GetProperty("eventId").GetGuid()
                .Should().Be(reassignedEvent.Id);
            reassignedPayload.RootElement.GetProperty("oldDriverUserId").GetGuid()
                .Should().Be(seed.ShuttleDriverId);
            reassignedPayload.RootElement.GetProperty("newDriver").GetProperty("userId").GetGuid()
                .Should().Be(replacementDriverId);
            reassignedPayload.RootElement.GetProperty("newVehicle").GetProperty("licensePlate")
                .GetString().Should().Be(replacementVehicle.LicensePlate);
            reassignedPayload.RootElement.GetProperty("reason").GetString()
                .Should().Be("Original vehicle needs maintenance");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReassignAsync_CapacityOrDriverConflict_PreservesOriginalAssignmentAndReservations()
    {
        var databaseName = $"vietride_trip_shuttle_reassign_failure_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, service, created) = await SeedAssignedShuttleAsync(db, clock, now);
            var target = await db.ShuttleTrips.SingleAsync(shuttle => shuttle.Id == created.ShuttleTripId);
            var vehicleTypeId = await db.Vehicles
                .Where(vehicle => vehicle.Id == seed.ShuttleVehicleId)
                .Select(vehicle => vehicle.VehicleTypeId)
                .SingleAsync();
            var undersizedVehicle = Vehicle.Create(
                seed.OperatorId,
                vehicleTypeId,
                $"SMALL-{Guid.NewGuid():N}"[..20],
                CreateSeatLayout("SHUTTLE_TEST", 1),
                1,
                100m,
                2m);
            var conflictingDriverId = Guid.NewGuid();
            var blocker = ShuttleTrip.Create(
                seed.OperatorId,
                seed.MainTripId,
                target.StationId,
                conflictingDriverId,
                seed.ShuttleVehicleId,
                target.ScheduledDepartureTime,
                target.ScheduledEndTime,
                null);
            var conflictingReservation = ResourceReservation.CreateForShuttleTrip(
                seed.OperatorId,
                ResourceReservationType.CREW,
                ResourceReservationRole.DRIVER,
                conflictingDriverId,
                blocker.Id,
                target.ScheduledDepartureTime,
                target.ScheduledEndTime,
                null,
                target.StationId,
                10.7731m,
                106.7032m,
                10.7769m,
                106.7009m);
            db.AddRange(undersizedVehicle, blocker, conflictingReservation);
            await db.SaveChangesAsync();

            var capacityFailure = async () => await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    seed.OperatorId,
                    created.ShuttleTripId,
                    null,
                    undersizedVehicle.Id,
                    "Use a smaller vehicle"),
                CancellationToken.None);
            await capacityFailure.Should().ThrowAsync<CodedConflictException>()
                .Where(error => error.ErrorCode == "SHUTTLE_CAPACITY_EXCEEDED");

            var conflictFailure = async () => await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    seed.OperatorId,
                    created.ShuttleTripId,
                    conflictingDriverId,
                    null,
                    "Use another driver"),
                CancellationToken.None);
            await conflictFailure.Should().ThrowAsync<CodedConflictException>()
                .Where(error => error.ErrorCode == "SHUTTLE_DRIVER_CONFLICT");

            var foreignTenant = async () => await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    Guid.NewGuid(),
                    created.ShuttleTripId,
                    Guid.NewGuid(),
                    null,
                    "Foreign tenant attempt"),
                CancellationToken.None);
            await foreignTenant.Should().ThrowAsync<CodedNotFoundException>()
                .Where(error => error.ErrorCode == "SHUTTLE_TRIP_NOT_FOUND");

            db.ChangeTracker.Clear();
            var persisted = await db.ShuttleTrips.AsNoTracking()
                .SingleAsync(shuttle => shuttle.Id == created.ShuttleTripId);
            var reservations = await db.ResourceReservations.AsNoTracking()
                .Where(reservation => reservation.ShuttleTripId == created.ShuttleTripId
                    && reservation.Status == ResourceReservationStatus.RESERVED)
                .ToArrayAsync();
            persisted.DriverUserId.Should().Be(seed.ShuttleDriverId);
            persisted.VehicleId.Should().Be(seed.ShuttleVehicleId);
            reservations.Should().ContainSingle(reservation =>
                reservation.ResourceRole == ResourceReservationRole.DRIVER
                && reservation.ResourceId == seed.ShuttleDriverId);
            reservations.Should().ContainSingle(reservation =>
                reservation.ResourceRole == ResourceReservationRole.VEHICLE
                && reservation.ResourceId == seed.ShuttleVehicleId);

            var mutable = await db.ShuttleTrips.SingleAsync(shuttle => shuttle.Id == created.ShuttleTripId);
            mutable.Start(now);
            await db.SaveChangesAsync();
            var invalidState = async () => await service.ReassignAsync(
                new ReassignShuttleTripInput(
                    seed.OperatorId,
                    created.ShuttleTripId,
                    Guid.NewGuid(),
                    null,
                    "Trip already started"),
                CancellationToken.None);
            await invalidState.Should().ThrowAsync<CodedConflictException>()
                .Where(error => error.ErrorCode == "SHUTTLE_TRIP_INVALID_STATE");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConfirmedFanOut_RealInbox_FailureBeforeMarker_RollsBackManifestsAndMarker()
    {
        var databaseName = $"vietride_trip_shuttle_inbox_failure_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        await using var setup = CreateDbContext(databaseName, clock);

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, now.AddHours(4));
            var integrationEvent = CreateConfirmedEvent(seed.MainTripId, ticketCount: 3);
            var messageId = Guid.NewGuid();
            const string consumerName = "trip.booking-shuttle-confirmed";
            const string payloadHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

            await using (var deliveryDb = CreateDbContext(databaseName, clock))
            {
                var unitOfWork = new EfUnitOfWork(deliveryDb);
                var handler = CreateConfirmedHandler(deliveryDb, unitOfWork);
                var inbox = new EfIntegrationEventInbox<TripDbContext>(deliveryDb, unitOfWork, clock);

                var act = () => inbox.ExecuteAsync(
                    consumerName,
                    messageId,
                    payloadHash,
                    async cancellationToken =>
                    {
                        await handler.HandleAsync(integrationEvent, cancellationToken);
                        throw new InvalidOperationException("crash before inbox marker");
                    },
                    CancellationToken.None);

                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("crash before inbox marker");
            }

            await using var assertionDb = CreateDbContext(databaseName, clock);
            (await assertionDb.ShuttlePassengers.AsNoTracking()
                .CountAsync(x => x.BookingId == integrationEvent.BookingId)).Should().Be(0);
            (await assertionDb.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(x => x.ConsumerName == consumerName && x.MessageId == messageId))
                .Should().Be(0);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DispatchAndCutoffRace_Repeatedly_PreservesBookingAtomicityAndOutboxConsistency()
    {
        var databaseName = $"vietride_trip_shuttle_race_{Guid.NewGuid():N}";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(5);
        await using var setup = CreateDbContext(databaseName, new FrozenClock(baseTime));

        try
        {
            await setup.Database.MigrateAsync();
            var seed = await SeedBaseAsync(setup, baseTime.AddHours(500));

            for (var iteration = 0; iteration < 12; iteration++)
            {
                var cutoffAt = baseTime.AddMinutes(iteration * 20);
                var trip = VietRide.Trip.Domain.Entities.Trip.Create(
                    seed.OperatorId,
                    seed.RouteId,
                    seed.MainVehicleId,
                    seed.MainDriverId,
                    null,
                    null,
                    cutoffAt.AddMinutes(30),
                    cutoffAt.AddHours(3),
                    TripSource.MANUAL,
                    Money.FromRaw(100_000),
                    500m,
                    maxCargoVolumeM3: null,
                    estimatedPassengerLuggageKg: 5m,
                    seatLayoutSnapshotJson: seed.MainSeatLayoutJson);
                setup.Trips.Add(trip);
                var bookingId = Guid.NewGuid();
                var passengerId = Guid.NewGuid();
                for (var ticketIndex = 0; ticketIndex < 3; ticketIndex++)
                {
                    setup.ShuttlePassengers.Add(ShuttlePassenger.Request(
                        trip.Id,
                        bookingId,
                        Guid.NewGuid(),
                        passengerId,
                        "12 Nguyen Hue, District 1",
                        10.7731m,
                        106.7032m));
                }

                await setup.SaveChangesAsync();

                var dispatchClock = new FrozenClock(cutoffAt.AddSeconds(-1));
                var cutoffClock = new FrozenClock(cutoffAt);
                await using var dispatchDb = CreateDbContext(databaseName, dispatchClock);
                await using var cutoffDb = CreateDbContext(databaseName, cutoffClock);
                var dispatch = CreateDispatchService(dispatchDb, dispatchClock, seed.OperatorId);
                var safetyJob = new ShuttleDispatchSafetyJob(
                    cutoffDb,
                    CreateOutbox(cutoffDb, cutoffClock),
                    cutoffClock);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var dispatchTask = Task.Run(async () =>
                {
                    await start.Task;
                    try
                    {
                        await dispatch.CreateAsync(new CreateShuttleTripInput(
                            seed.OperatorId,
                            Guid.NewGuid(),
                            trip.Id,
                            seed.ShuttleDriverId,
                            seed.ShuttleVehicleId,
                            cutoffAt.AddMinutes(-20),
                            cutoffAt.AddMinutes(-5),
                            [bookingId],
                            null), CancellationToken.None);
                    }
                    catch (Exception exception) when (exception.GetType().Name is
                        "ConflictException" or "CodedValidationException")
                    {
                        // The cutoff transaction is an allowed race winner.
                    }
                });
                var cutoffTask = Task.Run(async () =>
                {
                    await start.Task;
                    await safetyJob.ScanAsync(CancellationToken.None);
                });
                start.SetResult();
                await Task.WhenAll(dispatchTask, cutoffTask);

                await using var assertionDb = CreateDbContext(databaseName, cutoffClock);
                var manifests = await assertionDb.ShuttlePassengers.AsNoTracking()
                    .Where(x => x.BookingId == bookingId)
                    .ToArrayAsync();
                manifests.Should().HaveCount(3);
                manifests.Select(x => x.Status).Distinct().Should().ContainSingle();
                var assigned = manifests[0].Status == ShuttlePassenger.PendingStatus;
                var cancelled = manifests[0].Status == ShuttlePassenger.CancelledStatus;
                (assigned || cancelled).Should().BeTrue();

                var shuttleTrips = await assertionDb.ShuttleTrips.AsNoTracking()
                    .CountAsync(x => x.MainTripId == trip.Id);
                var outboxRows = await assertionDb.OutboxEvents.AsNoTracking()
                    .Select(x => new { x.EventType, x.Payload })
                    .ToArrayAsync();
                var outbox = outboxRows
                    .Where(x => x.Payload.Contains(bookingId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.EventType)
                    .ToArray();
                if (assigned)
                {
                    manifests.Select(x => x.ShuttleTripId).Distinct().Should().ContainSingle();
                    manifests.Should().OnlyContain(x => x.ShuttleTripId.HasValue);
                    shuttleTrips.Should().Be(1);
                    outbox.Should().ContainSingle(x => x == "trip.shuttle.assigned");
                    outbox.Should().NotContain("trip.shuttle.unfulfilled");
                }
                else
                {
                    manifests.Should().OnlyContain(x => !x.ShuttleTripId.HasValue);
                    shuttleTrips.Should().Be(0);
                    outbox.Should().ContainSingle(x => x == "trip.shuttle.unfulfilled");
                    outbox.Should().NotContain("trip.shuttle.assigned");
                }
            }
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PickupProgression_AssignedDriverMarksWholeOrderAndTrackingContextAdvances()
    {
        var databaseName = $"vietride_trip_shuttle_pickup_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedBaseAsync(db, now.AddHours(4));
            var bookingId = Guid.NewGuid();
            var passengerId = Guid.NewGuid();
            for (var index = 0; index < 2; index++)
            {
                db.ShuttlePassengers.Add(ShuttlePassenger.Request(
                    seed.MainTripId,
                    bookingId,
                    Guid.NewGuid(),
                    passengerId,
                    "12 Nguyen Hue, District 1",
                    10.7731m,
                    106.7032m,
                    roadDistanceMeters: 1_000));
            }

            await db.SaveChangesAsync();
            var service = CreateDispatchService(db, clock, seed.OperatorId);
            var created = await service.CreateAsync(new CreateShuttleTripInput(
                seed.OperatorId,
                Guid.NewGuid(),
                seed.MainTripId,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(1),
                now.AddHours(2),
                [bookingId],
                null), CancellationToken.None);

            var wrongDriver = async () => await service.MarkPickupAsync(
                created.ShuttleTripId,
                1,
                Guid.NewGuid(),
                CancellationToken.None);
            await wrongDriver.Should().ThrowAsync<ForbiddenException>();

            await service.StartAsync(
                created.ShuttleTripId,
                seed.ShuttleDriverId,
                CancellationToken.None);
            var startedEvent = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == "trip.shuttle.started");
            using (var payload = JsonDocument.Parse(startedEvent.Payload))
            {
                payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(startedEvent.Id);
                payload.RootElement.GetProperty("driverUserId").GetGuid()
                    .Should().Be(seed.ShuttleDriverId);
                payload.RootElement.GetProperty("passengers").GetArrayLength().Should().Be(1);
            }
            var first = await service.MarkPickupAsync(
                created.ShuttleTripId,
                1,
                seed.ShuttleDriverId,
                CancellationToken.None);
            var replay = await service.MarkPickupAsync(
                created.ShuttleTripId,
                1,
                seed.ShuttleDriverId,
                CancellationToken.None);
            var context = await service.GetTrackingContextAsync(
                created.ShuttleTripId,
                seed.ShuttleDriverId,
                "DRIVER",
                null,
                CancellationToken.None);
            var passengerContext = await service.GetTrackingContextAsync(
                created.ShuttleTripId,
                passengerId,
                "PASSENGER",
                null,
                CancellationToken.None);

            first.PickedUpPassengerCount.Should().Be(2);
            replay.PickedUpPassengerCount.Should().Be(0);
            context.Stops.Should().ContainSingle(stop =>
                stop.PickupOrder == 1 && stop.Status == ShuttlePassenger.PickedUpStatus);
            var persisted = await db.ShuttlePassengers.AsNoTracking()
                .Where(x => x.ShuttleTripId == created.ShuttleTripId)
                .ToArrayAsync();
            persisted.Should().OnlyContain(x =>
                x.Status == ShuttlePassenger.PickedUpStatus && x.PickedUpAt == now);
            passengerContext.Allowed.Should().BeTrue();
            passengerContext.Scope.Should().Be("PASSENGER");
            passengerContext.Stops.Should().ContainSingle(stop =>
                stop.PickupOrder == 1
                && stop.Status == ShuttlePassenger.PickedUpStatus
                && stop.IsOwnPickup);
            passengerContext.Station.Should().NotBeNull();
            passengerContext.Station!.StationId.Should().NotBeEmpty();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DriverAssignmentsAndManifest_FilterAuthorizeAndGroupPassengers()
    {
        var databaseName = $"vietride_trip_shuttle_driver_reads_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, service, created) = await SeedAssignedShuttleAsync(db, clock, now);

            var assignments = await service.GetDriverAssignmentsAsync(
                seed.ShuttleDriverId,
                null,
                null,
                CancellationToken.None);
            var foreignAssignments = await service.GetDriverAssignmentsAsync(
                Guid.NewGuid(),
                assignments.From,
                assignments.To,
                CancellationToken.None);
            var manifest = await service.GetDriverManifestAsync(
                created.ShuttleTripId,
                seed.ShuttleDriverId,
                CancellationToken.None);
            var forbidden = async () => await service.GetDriverManifestAsync(
                created.ShuttleTripId,
                Guid.NewGuid(),
                CancellationToken.None);
            var missing = async () => await service.GetDriverManifestAsync(
                Guid.NewGuid(),
                seed.ShuttleDriverId,
                CancellationToken.None);

            assignments.Items.Should().ContainSingle(item =>
                item.ShuttleTripId == created.ShuttleTripId
                && item.MainTripId == seed.MainTripId
                && item.PassengerCount == 2
                && item.StopCount == 1);
            foreignAssignments.Items.Should().BeEmpty();
            manifest.Stops.Should().ContainSingle(stop =>
                stop.PickupOrder == 1
                && stop.PassengerCount == 2
                && stop.TicketIds.Count == 2
                && stop.Status == ShuttlePassenger.PendingStatus);
            await forbidden.Should().ThrowAsync<ForbiddenException>();
            await missing.Should().ThrowAsync<CodedNotFoundException>()
                .Where(error => error.ErrorCode == "SHUTTLE_TRIP_NOT_FOUND");

            var shuttleTrip = await db.ShuttleTrips.SingleAsync(item => item.Id == created.ShuttleTripId);
            shuttleTrip.Cancel(now, Guid.NewGuid(), "test cancellation");
            await db.SaveChangesAsync();
            var afterCancellation = await service.GetDriverAssignmentsAsync(
                seed.ShuttleDriverId,
                assignments.From,
                assignments.To,
                CancellationToken.None);
            afterCancellation.Items.Should().BeEmpty();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DriverManifest_MixedPickupGroupStatuses_ReturnsConflictInsteadOfPending()
    {
        var databaseName = $"vietride_trip_shuttle_manifest_status_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, service, created) = await SeedAssignedShuttleAsync(db, clock, now);
            var passenger = await db.ShuttlePassengers
                .Where(item => item.ShuttleTripId == created.ShuttleTripId)
                .OrderBy(item => item.Id)
                .FirstAsync();
            passenger.MarkPickedUp(now);
            await db.SaveChangesAsync();

            var read = async () => await service.GetDriverManifestAsync(
                created.ShuttleTripId,
                seed.ShuttleDriverId,
                CancellationToken.None);

            await read.Should().ThrowAsync<CodedConflictException>()
                .Where(error => error.ErrorCode == "SHUTTLE_MANIFEST_INCONSISTENT_STATUS");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetTrackingProjection_ReturnsOnlyOwnedInProgressShuttles()
    {
        var databaseName = $"vietride_trip_shuttle_tracking_projection_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var clock = new FrozenClock(now);
        await using var db = CreateDbContext(databaseName, clock);

        try
        {
            await db.Database.MigrateAsync();
            var (seed, service, created) = await SeedAssignedShuttleAsync(db, clock, now);
            await service.StartAsync(created.ShuttleTripId, seed.ShuttleDriverId, CancellationToken.None);
            var active = await db.ShuttleTrips.SingleAsync(item => item.Id == created.ShuttleTripId);

            var scheduledOwned = ShuttleTrip.Create(
                seed.OperatorId,
                seed.MainTripId,
                active.StationId,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(3),
                now.AddHours(4),
                null);
            var activeOtherTenant = ShuttleTrip.Create(
                Guid.NewGuid(),
                seed.MainTripId,
                active.StationId,
                seed.ShuttleDriverId,
                seed.ShuttleVehicleId,
                now.AddHours(5),
                now.AddHours(6),
                null);
            activeOtherTenant.Start(now);
            db.ShuttleTrips.AddRange(scheduledOwned, activeOtherTenant);
            await db.SaveChangesAsync();

            var result = await service.GetTrackingProjectionAsync(
                seed.OperatorId,
                CancellationToken.None);

            result.Should().ContainSingle().Which.Should().Be(
                new OperatorTrackingShuttleTripDto(
                    created.ShuttleTripId,
                    seed.MainTripId,
                    ShuttleTrip.InProgressStatus));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(BaseSeed Seed, IShuttleDispatchService Service, CreateShuttleTripResult Created)>
        SeedAssignedShuttleAsync(TripDbContext db, IClock clock, DateTimeOffset now)
    {
        var seed = await SeedBaseAsync(db, now.AddHours(4));
        var bookingId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        for (var index = 0; index < 2; index++)
        {
            db.ShuttlePassengers.Add(ShuttlePassenger.Request(
                seed.MainTripId,
                bookingId,
                Guid.NewGuid(),
                passengerId,
                "12 Nguyen Hue, District 1",
                10.7731m,
                106.7032m,
                roadDistanceMeters: 1_000));
        }

        await db.SaveChangesAsync();
        var service = CreateDispatchService(db, clock, seed.OperatorId);
        var created = await service.CreateAsync(new CreateShuttleTripInput(
            seed.OperatorId,
            Guid.NewGuid(),
            seed.MainTripId,
            seed.ShuttleDriverId,
            seed.ShuttleVehicleId,
            now.AddHours(1),
            now.AddHours(2),
            [bookingId],
            null), CancellationToken.None);
        return (seed, service, created);
    }

    private static async Task<BaseSeed> SeedBaseAsync(TripDbContext db, DateTimeOffset departure)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Shuttle Origin",
            $"shuttle-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m,
            supportsShuttle: true);
        var destination = Station.Create(
            "Shuttle Destination",
            $"shuttle-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Shuttle integration route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            360);
        var vehicleType = VehicleType.Create("SHUTTLE_TEST", "Shuttle integration vehicle", 5, 20);
        var mainLayout = CreateSeatLayout("MAIN", 20);
        var shuttleLayout = CreateSeatLayout("SHUTTLE", 12);
        var mainVehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"MAIN-{Guid.NewGuid():N}"[..20],
            mainLayout,
            20,
            500m,
            10m);
        var shuttleVehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"SHUT-{Guid.NewGuid():N}"[..20],
            shuttleLayout,
            12,
            200m,
            5m);
        var mainDriverId = Guid.NewGuid();
        var shuttleDriverId = Guid.NewGuid();
        var mainTrip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            mainVehicle.Id,
            mainDriverId,
            null,
            null,
            departure,
            departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: mainLayout);

        db.AddRange(origin, destination, route, vehicleType, mainVehicle, shuttleVehicle, mainTrip);
        await db.SaveChangesAsync();
        return new BaseSeed(
            operatorId,
            route.Id,
            mainVehicle.Id,
            mainDriverId,
            shuttleVehicle.Id,
            shuttleDriverId,
            mainTrip.Id,
            mainVehicle.SeatLayoutJson);
    }

    private static JsonElement CreateSeatLayout(string vehicleTypeCode, int totalSeats)
        => JsonSerializer.SerializeToElement(new
        {
            version = 1,
            vehicleTypeCode,
            totalSeats,
            rows = totalSeats,
            cols = 1,
            decks = 1,
            aisles = Array.Empty<object>(),
            seats = Enumerable.Range(1, totalSeats).Select(index => new
            {
                seatNumber = $"A{index:00}",
                row = index,
                col = 1,
                deck = 1,
                type = "STANDARD",
                isWindow = true,
                isAisle = false,
                disabled = false,
            }),
        });

    private static object CreateSeat(string seatNumber, int row, string type, bool disabled)
        => new
        {
            seatNumber,
            row,
            col = 1,
            deck = 1,
            type,
            isWindow = true,
            isAisle = false,
            disabled,
        };

    private static BookingShuttleConfirmedIntegrationEvent CreateConfirmedEvent(
        Guid tripId,
        int ticketCount)
    {
        var passengerId = Guid.NewGuid();
        return new BookingShuttleConfirmedIntegrationEvent
        {
            BookingId = Guid.NewGuid(),
            BookingCode = "VR-20260822-ABCDEFGH",
            TripId = tripId,
            UserId = passengerId,
            Tickets = Enumerable.Range(0, ticketCount)
                .Select(_ => new BookingShuttleConfirmedIntegrationEvent.ConfirmedTicket(
                    Guid.NewGuid(),
                    passengerId))
                .ToArray(),
            ShuttlePickup = new BookingShuttleConfirmedIntegrationEvent.ShuttlePickupPayload(
                "12 Nguyen Hue, District 1",
                10.7731m,
                106.7032m),
        };
    }

    private static IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent> CreateConfirmedHandler(
        TripDbContext db,
        IUnitOfWork? unitOfWork = null)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Messaging.BookingShuttleConfirmedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent>)Activator.CreateInstance(
            type,
            db,
            unitOfWork ?? new EfUnitOfWork(db),
            new StubShuttleDistanceClient())!;
    }

    private static IShuttleDispatchService CreateDispatchService(
        TripDbContext db,
        IClock clock,
        Guid operatorId,
        IReadOnlySet<Guid>? missingProfileUserIds = null,
        bool throwProfileTransportError = false)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Services.ShuttleDispatchService",
            throwOnError: true)!;
        return (IShuttleDispatchService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [
                db,
                new StubIdentityClient(operatorId, missingProfileUserIds, throwProfileTransportError),
                CreateOutbox(db, clock),
                clock,
                CreateResourceAvailability(db, clock),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SHUTTLE_MAX_DISTANCE_KM"] = "10",
                    })
                    .Build(),
            ],
            culture: null)!;
    }

    private static IResourceAvailabilityService CreateResourceAvailability(TripDbContext db, IClock clock)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Services.ResourceAvailabilityService",
            throwOnError: true)!;
        return (IResourceAvailabilityService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db, new StubRepositionTravelTimeClient(), clock],
            culture: null)!;
    }

    private static IIntegrationEventOutbox CreateOutbox(TripDbContext db, IClock clock)
        => new IntegrationEventOutbox(new OutboxStore(db, clock));

    private static TripDbContext CreateDbContext(string databaseName, IClock clock)
    {
        var connectionString = CreateConnectionString(databaseName);
        var dataSource = DataSources.GetOrAdd(connectionString, static value =>
        {
            var builder = new NpgsqlDataSourceBuilder(value);
            builder.MapEnum<OutboxEventStatus>(
                $"{TripDbContext.SchemaName}.outbox_event_status",
                new NpgsqlNullNameTranslator());
            TripDbContext.ConfigurePostgresEnums(builder);
            return builder.Build();
        });
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, clock);
    }

    private static async Task<bool> TableExistsAsync(TripDbContext db, string tableName)
    {
        var wasClosed = db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed;
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT to_regclass('vietride_trip.{tableName}') IS NOT NULL";
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (wasClosed)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        TripDbContext db,
        string tableName,
        string columnName)
    {
        var wasClosed = db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed;
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'vietride_trip'
                      AND table_name = @table_name
                      AND column_name = @column_name)
                """;
            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "table_name";
            tableParameter.Value = tableName;
            command.Parameters.Add(tableParameter);
            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "column_name";
            columnParameter.Value = columnName;
            command.Parameters.Add(columnParameter);
            return (bool)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (wasClosed)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed record BaseSeed(
        Guid OperatorId,
        Guid RouteId,
        Guid MainVehicleId,
        Guid MainDriverId,
        Guid ShuttleVehicleId,
        Guid ShuttleDriverId,
        Guid MainTripId,
        JsonElement MainSeatLayoutJson);

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StubIdentityClient : IIdentityInternalClient
    {
        private readonly Guid _operatorId;
        private readonly IReadOnlySet<Guid> _missingProfileUserIds;
        private readonly bool _throwProfileTransportError;

        public StubIdentityClient(
            Guid operatorId,
            IReadOnlySet<Guid>? missingProfileUserIds = null,
            bool throwProfileTransportError = false)
        {
            _operatorId = operatorId;
            _missingProfileUserIds = missingProfileUserIds ?? new HashSet<Guid>();
            _throwProfileTransportError = throwProfileTransportError;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.Success(
                userId,
                "Shuttle Driver",
                null,
                "DRIVER",
                _operatorId,
                "ACTIVE") with
            {
                Phone = "0900000000",
            });

        public Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            if (_throwProfileTransportError)
            {
                throw new HttpRequestException("Identity transport failure");
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, IdentityUserProfile>>(userIds
                    .Where(userId => !_missingProfileUserIds.Contains(userId))
                    .ToDictionary(
                        userId => userId,
                        userId => new IdentityUserProfile(
                            userId,
                            "Shuttle Passenger",
                            null,
                            "PASSENGER",
                            _operatorId,
                            "ACTIVE",
                            "0900000000")));
        }
    }

    private sealed class StubShuttleDistanceClient : IShuttleDistanceClient
    {
        public Task<ShuttleDistanceOutcome> CalculateAsync(
            decimal originLatitude,
            decimal originLongitude,
            decimal destinationLatitude,
            decimal destinationLongitude,
            CancellationToken cancellationToken)
            => Task.FromResult<ShuttleDistanceOutcome>(new ShuttleDistanceOutcome.Success(10_000));
    }

    private sealed class StubRepositionTravelTimeClient : IRepositionTravelTimeClient
    {
        public Task<RepositionTravelTimeResult> CalculateAsync(
            decimal originLatitude,
            decimal originLongitude,
            decimal destinationLatitude,
            decimal destinationLongitude,
            CancellationToken cancellationToken = default)
            => Task.FromResult(RepositionTravelTimeResult.Success(0, 0));
    }
}
