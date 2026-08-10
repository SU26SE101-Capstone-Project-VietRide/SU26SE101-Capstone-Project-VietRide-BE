using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;

namespace VietRide.Trip.IntegrationTests.Events;

public sealed class TripRouteChangedIntegrationEventTests
{
    [Fact]
    public async Task OutboxSameTransactionProcessedAtNullRoutingExchangeMessageIdRestartAndDedupe()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var trip = await db.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await db.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var routeChangeBase = new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero);
            trip.MarkBoarding(routeChangeBase);
            trip.Start(routeChangeBase);
            var alternative = AlternativeRoute.Create(
                route.Id,
                "Day 33 route change",
                route.DestinationStationId,
                950m,
                230);
            var firstStop = Stop.Create(seed.OperatorId, "First candidate", 10.1m, 106.1m);
            var secondStop = Stop.Create(seed.OperatorId, "Second candidate", 10.2m, 106.2m);
            db.AlternativeRoutes.Add(alternative);
            db.Stops.AddRange(firstStop, secondStop);
            db.AlternativeRouteStops.AddRange(
                AlternativeRouteStop.Create(alternative.Id, secondStop.Id, 2, 90, null),
                AlternativeRouteStop.Create(alternative.Id, firstStop.Id, 1, 45, null));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var affectedBookingIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var alternativeRoutes = CreateRepository<IAlternativeRouteRepository>(db, "AlternativeRouteRepository");
            var tripStops = CreateRepository<ITripStopRepository>(db, "TripStopRepository");
            var outbox = new IntegrationEventOutbox(
                new OutboxStore(db, new Day29CargoNearFullOutboxIntegrationTests.FixedClock()));
            var handler = new ChangeTripRouteCommandHandler(
                CreateRepository<ITripRepository>(db, "TripRepository"),
                alternativeRoutes,
                new BookingImpactStub(seed.TripId, affectedBookingIds),
                new TripRouteChangeService(alternativeRoutes, tripStops, outbox),
                new EfUnitOfWork(db),
                new Day29CargoNearFullOutboxIntegrationTests.FixedClock());

            await handler.Handle(
                new ChangeTripRouteCommand(seed.TripId, seed.OperatorId, Guid.NewGuid(), alternative.Id),
                CancellationToken.None);

            await using var assertionDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var persistedTrip = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(item => item.Id == seed.TripId);
            var persistedStops = await assertionDb.TripStops.AsNoTracking()
                .Where(item => item.TripId == seed.TripId)
                .OrderBy(item => item.OrderIndex)
                .ToArrayAsync();
            var row = await assertionDb.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == TripRouteChangedIntegrationEvent.EventTypeValue);

            persistedTrip.AlternativeRouteId.Should().Be(alternative.Id);
            persistedTrip.EstimatedArrivalTime.Should().Be(routeChangeBase.AddMinutes(230));
            persistedStops.Should().SatisfyRespectively(
                stop => stop.Should().Match<TripStop>(item =>
                    item.StopId == firstStop.Id
                    && item.OrderIndex == 1
                    && item.Status == TripStopStatus.PENDING
                    && item.EstimatedArrivalTime == routeChangeBase.AddMinutes(45)
                    && item.AllowPickup
                    && item.AllowDropoff),
                stop => stop.Should().Match<TripStop>(item =>
                    item.StopId == secondStop.Id
                    && item.OrderIndex == 2
                    && item.Status == TripStopStatus.PENDING
                    && item.EstimatedArrivalTime == routeChangeBase.AddMinutes(90)
                    && item.AllowPickup
                    && item.AllowDropoff));
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            row.RetryCount.Should().Be(0);
            row.EventType.Should().Be("trip.trip.route_changed");

            using var payload = JsonDocument.Parse(row.Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(
                    "eventId",
                    "occurredAt",
                    "tripId",
                    "operatorId",
                    "tripStatus",
                    "alternativeRouteId",
                    "affectedBookings");
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
            payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(seed.TripId);
            payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(seed.OperatorId);
            payload.RootElement.GetProperty("tripStatus").GetString().Should().Be("IN_PROGRESS");
            payload.RootElement.GetProperty("alternativeRouteId").GetGuid().Should().Be(alternative.Id);
            payload.RootElement.TryGetProperty("affectedBookingIds", out _).Should().BeFalse();
            var affectedBookings = payload.RootElement.GetProperty("affectedBookings")
                .EnumerateArray().ToArray();
            affectedBookings.Select(item => item.GetProperty("bookingId").GetGuid())
                .Should().Equal(affectedBookingIds.OrderBy(id => id));
            foreach (var affectedBooking in affectedBookings)
            {
                affectedBooking.EnumerateObject().Select(property => property.Name)
                    .Should().BeEquivalentTo("bookingId", "candidateStops");
                var candidates = affectedBooking.GetProperty("candidateStops")
                    .EnumerateArray().ToArray();
                candidates.Should().HaveCount(3);
                candidates.Select(item => item.GetProperty("sequence").GetInt32())
                    .Should().Equal(1, 2, 3);
                candidates.Select(item => item.EnumerateObject().Select(property => property.Name))
                    .Should().OnlyContain(fields => fields.ToHashSet().SetEquals(
                        new[] { "stopId", "stationId", "stationName", "sequence", "estimatedArrivalAt" }));
                candidates[0].GetProperty("stopId").GetGuid().Should().Be(firstStop.Id);
                candidates[0].GetProperty("stationId").ValueKind.Should().Be(JsonValueKind.Null);
                candidates[0].GetProperty("stationName").GetString().Should().Be(firstStop.Name);
                candidates[0].GetProperty("estimatedArrivalAt").GetDateTimeOffset()
                    .Should().Be(routeChangeBase.AddMinutes(45));
                candidates[1].GetProperty("stopId").GetGuid().Should().Be(secondStop.Id);
                candidates[1].GetProperty("stationId").ValueKind.Should().Be(JsonValueKind.Null);
                candidates[1].GetProperty("estimatedArrivalAt").GetDateTimeOffset()
                    .Should().Be(routeChangeBase.AddMinutes(90));
                candidates[2].GetProperty("stopId").ValueKind.Should().Be(JsonValueKind.Null);
                candidates[2].GetProperty("stationId").GetGuid()
                    .Should().Be(route.DestinationStationId);
                candidates[2].GetProperty("estimatedArrivalAt").GetDateTimeOffset()
                    .Should().Be(routeChangeBase.AddMinutes(230));
            }

            var broker = new RecordingPublisher();
            var firstRestartStore = new OutboxStore(
                assertionDb,
                new Day29CargoNearFullOutboxIntegrationTests.FixedClock());
            var firstDelivery = await firstRestartStore.FetchPendingAsync(10, 5, CancellationToken.None);
            firstDelivery.Should().ContainSingle();
            await broker.PublishRawAsync(
                firstDelivery[0].EventType,
                firstDelivery[0].Id,
                firstDelivery[0].Payload,
                CancellationToken.None);

            await using var replayDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var replayStore = new OutboxStore(
                replayDb,
                new Day29CargoNearFullOutboxIntegrationTests.FixedClock());
            var replayDelivery = await replayStore.FetchPendingAsync(10, 5, CancellationToken.None);
            replayDelivery.Should().ContainSingle();
            await broker.PublishRawAsync(
                replayDelivery[0].EventType,
                replayDelivery[0].Id,
                replayDelivery[0].Payload,
                CancellationToken.None);
            await replayStore.MarkPublishedAsync(
                replayDelivery[0].Id,
                DateTime.UtcNow,
                CancellationToken.None);

            broker.ExchangeName.Should().Be("vietride.events");
            broker.Deliveries.Should().HaveCount(2);
            broker.Deliveries.Should().OnlyContain(delivery =>
                delivery.RoutingKey == TripRouteChangedIntegrationEvent.EventTypeValue
                && delivery.MessageId == row.Id
                && delivery.Payload == row.Payload);
            broker.Deliveries.Select(delivery => delivery.MessageId).Distinct()
                .Should().ContainSingle("consumer dedupe is keyed by the stable broker MessageId");

            await using var completedRestartDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var completedRestartStore = new OutboxStore(
                completedRestartDb,
                new Day29CargoNearFullOutboxIntegrationTests.FixedClock());
            (await completedRestartStore.FetchPendingAsync(10, 5, CancellationToken.None))
                .Should().BeEmpty();
            var published = await completedRestartDb.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.Id == row.Id);
            published.Status.Should().Be(OutboxEventStatus.PUBLISHED);
            published.PublishedAt.Should().NotBeNull();

            await AssertTerminalStateRejectedByRealHandlerAsync();
            await AssertWrongParentAndForeignTenantRejectedByRealHandlerAsync();
            await AssertConcurrentPreflightChangesDetectedAsync();
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    [Fact]
    public void PayloadUsesExactRegistryFields()
    {
        var evt = new TripRouteChangedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            Guid.NewGuid(),
            []);
        typeof(TripRouteChangedIntegrationEvent).GetProperties()
            .Where(property => property.Name != nameof(evt.EventId)
                && property.Name != nameof(evt.OccurredAt)
                && property.Name != nameof(evt.EventType))
            .Select(property => property.Name)
            .Should().Equal(
                nameof(evt.TripId),
                nameof(evt.OperatorId),
                nameof(evt.TripStatus),
                nameof(evt.AlternativeRouteId),
                nameof(evt.AffectedBookings));
        typeof(TripRouteChangedIntegrationEvent).GetProperty("AffectedBookingIds")
            .Should().BeNull();
    }

    private static TRepository CreateRepository<TRepository>(TripDbContext db, string typeName)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            $"VietRide.Trip.Infrastructure.Persistence.Repositories.{typeName}",
            throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(type, db)!;
    }

    private static ChangeTripRouteCommandHandler CreateHandler(
        TripDbContext db,
        IBookingImpactClient bookingImpact)
    {
        var alternativeRoutes = CreateRepository<IAlternativeRouteRepository>(db, "AlternativeRouteRepository");
        var tripStops = CreateRepository<ITripStopRepository>(db, "TripStopRepository");
        var outbox = new IntegrationEventOutbox(
            new OutboxStore(db, new Day29CargoNearFullOutboxIntegrationTests.FixedClock()));
        return new ChangeTripRouteCommandHandler(
            CreateRepository<ITripRepository>(db, "TripRepository"),
            alternativeRoutes,
            bookingImpact,
            new TripRouteChangeService(alternativeRoutes, tripStops, outbox),
            new EfUnitOfWork(db),
            new Day29CargoNearFullOutboxIntegrationTests.FixedClock());
    }

    private static async Task AssertTerminalStateRejectedByRealHandlerAsync()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var trip = await db.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await db.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var alternative = AlternativeRoute.Create(
                route.Id,
                "Day 33 terminal route",
                route.DestinationStationId,
                null,
                null);
            trip.MarkBoarding(DateTimeOffset.UtcNow);
            trip.Start(DateTimeOffset.UtcNow);
            trip.CompleteManually(DateTimeOffset.UtcNow, Guid.NewGuid());
            db.AlternativeRoutes.Add(alternative);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var handler = CreateHandler(db, new BookingImpactStub(seed.TripId, []));
            var action = () => handler.Handle(
                new ChangeTripRouteCommand(
                    seed.TripId,
                    seed.OperatorId,
                    Guid.NewGuid(),
                    alternative.Id),
                CancellationToken.None);

            await action.Should().ThrowAsync<CodedConflictException>()
                .Where(exception => exception.ErrorCode == "TRIP_NOT_EDITABLE");
            (await db.OutboxEvents.CountAsync()).Should().Be(0);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static async Task AssertConcurrentPreflightChangesDetectedAsync()
    {
        await AssertConcurrentStateChangeDetectedAsync();
        await AssertConcurrentRouteChangeDetectedAsync();
    }

    private static async Task AssertWrongParentAndForeignTenantRejectedByRealHandlerAsync()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var trip = await db.Trips.SingleAsync(item => item.Id == seed.TripId);
            var originalRoute = await db.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var siblingRoute = Route.Create(
                seed.OperatorId,
                "Day 33 sibling route",
                originalRoute.OriginStationId,
                originalRoute.DestinationStationId,
                trip.BaseFare,
                null,
                240);
            var foreignRoute = Route.Create(
                Guid.NewGuid(),
                "Day 33 foreign route",
                originalRoute.OriginStationId,
                originalRoute.DestinationStationId,
                trip.BaseFare,
                null,
                240);
            var wrongParent = AlternativeRoute.Create(
                siblingRoute.Id,
                "Day 33 wrong parent",
                siblingRoute.DestinationStationId,
                null,
                null);
            var foreignTenant = AlternativeRoute.Create(
                foreignRoute.Id,
                "Day 33 foreign tenant",
                foreignRoute.DestinationStationId,
                null,
                null);
            db.AddRange(siblingRoute, foreignRoute, wrongParent, foreignTenant);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            foreach (var alternativeRouteId in new[] { wrongParent.Id, foreignTenant.Id })
            {
                var handler = CreateHandler(db, new BookingImpactStub(seed.TripId, []));
                var action = () => handler.Handle(
                    new ChangeTripRouteCommand(
                        seed.TripId,
                        seed.OperatorId,
                        Guid.NewGuid(),
                        alternativeRouteId),
                    CancellationToken.None);
                await action.Should().ThrowAsync<CodedNotFoundException>()
                    .Where(exception => exception.ErrorCode == "ROUTE_NOT_FOUND");
            }

            (await db.OutboxEvents.CountAsync()).Should().Be(0);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static async Task AssertConcurrentStateChangeDetectedAsync()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var handlerDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await handlerDb.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(handlerDb);
            var trip = await handlerDb.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await handlerDb.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var alternative = AlternativeRoute.Create(
                route.Id,
                "Day 33 concurrent state",
                route.DestinationStationId,
                null,
                null);
            handlerDb.AlternativeRoutes.Add(alternative);
            await handlerDb.SaveChangesAsync();
            handlerDb.ChangeTracker.Clear();

            var impact = new BlockingBookingImpact(seed.TripId);
            var handler = CreateHandler(handlerDb, impact);
            var handling = handler.Handle(
                new ChangeTripRouteCommand(
                    seed.TripId,
                    seed.OperatorId,
                    Guid.NewGuid(),
                    alternative.Id),
                CancellationToken.None);
            await impact.WaitUntilCalledAsync();

            await using (var contenderDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName))
            {
                var changedTrip = await contenderDb.Trips.SingleAsync(item => item.Id == seed.TripId);
                changedTrip.MarkBoarding(DateTimeOffset.UtcNow);
                changedTrip.Start(DateTimeOffset.UtcNow);
                changedTrip.CompleteManually(DateTimeOffset.UtcNow, Guid.NewGuid());
                await contenderDb.SaveChangesAsync();
            }

            impact.Release();
            Func<Task> observeHandling = async () => { await handling; };
            await observeHandling.Should().ThrowAsync<CodedConflictException>()
                .Where(exception => exception.ErrorCode == "TRIP_NOT_EDITABLE");

            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            (await assertionDb.OutboxEvents.CountAsync()).Should().Be(0);
            (await assertionDb.Trips.AsNoTracking().SingleAsync(item => item.Id == seed.TripId))
                .AlternativeRouteId.Should().BeNull();
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                handlerDb,
                databaseName);
        }
    }

    private static async Task AssertConcurrentRouteChangeDetectedAsync()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var handlerDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await handlerDb.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(handlerDb);
            var trip = await handlerDb.Trips.SingleAsync(item => item.Id == seed.TripId);
            var originalRoute = await handlerDb.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var alternative = AlternativeRoute.Create(
                originalRoute.Id,
                "Day 33 concurrent route",
                originalRoute.DestinationStationId,
                null,
                null);
            handlerDb.AlternativeRoutes.Add(alternative);
            await handlerDb.SaveChangesAsync();
            handlerDb.ChangeTracker.Clear();

            var impact = new BlockingBookingImpact(seed.TripId);
            var handler = CreateHandler(handlerDb, impact);
            var handling = handler.Handle(
                new ChangeTripRouteCommand(
                    seed.TripId,
                    seed.OperatorId,
                    Guid.NewGuid(),
                    alternative.Id),
                CancellationToken.None);
            await impact.WaitUntilCalledAsync();

            await using (var contenderDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName))
            {
                var changedTrip = await contenderDb.Trips.SingleAsync(item => item.Id == seed.TripId);
                var changedRoute = Route.Create(
                    seed.OperatorId,
                    "Day 33 replacement route",
                    originalRoute.OriginStationId,
                    originalRoute.DestinationStationId,
                    changedTrip.BaseFare,
                    null,
                    240);
                contenderDb.Routes.Add(changedRoute);
                changedTrip.ChangeRoute(changedRoute.Id, changedTrip.EstimatedArrivalTime);
                await contenderDb.SaveChangesAsync();
            }

            impact.Release();
            Func<Task> observeHandling = async () => { await handling; };
            await observeHandling.Should().ThrowAsync<CodedNotFoundException>()
                .Where(exception => exception.ErrorCode == "ROUTE_NOT_FOUND");

            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            (await assertionDb.OutboxEvents.CountAsync()).Should().Be(0);
            (await assertionDb.Trips.AsNoTracking().SingleAsync(item => item.Id == seed.TripId))
                .AlternativeRouteId.Should().BeNull();
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                handlerDb,
                databaseName);
        }
    }

    private sealed class BookingImpactStub(Guid tripId, IReadOnlyList<Guid> bookingIds) : IBookingImpactClient
    {
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid requestedTripId,
            Guid operatorId,
            CancellationToken cancellationToken)
        {
            requestedTripId.Should().Be(tripId);
            operatorId.Should().NotBeEmpty();
            return Task.FromResult(new TripBookingImpactProjection(
                tripId,
                bookingIds.Count,
                bookingIds.Select(id => new TripBookingImpactProjection.ActiveBooking(
                    id,
                    "CONFIRMED",
                    ["A1"])).ToArray()));
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public string ExchangeName { get; } = new RabbitMqOptions().ExchangeName;
        public List<Delivery> Deliveries { get; } = [];

        public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)
            where TEvent : IIntegrationEvent
            => PublishRawAsync(evt.EventType, evt.EventId, JsonSerializer.Serialize(evt), ct);

        public Task PublishRawAsync(
            string routingKey,
            Guid messageId,
            string payloadJson,
            CancellationToken ct)
        {
            Deliveries.Add(new Delivery(routingKey, messageId, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingBookingImpact(Guid tripId) : IBookingImpactClient
    {
        private readonly TaskCompletionSource called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilCalledAsync() => called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => released.SetResult();

        public async Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid requestedTripId,
            Guid operatorId,
            CancellationToken cancellationToken)
        {
            requestedTripId.Should().Be(tripId);
            called.SetResult();
            await released.Task.WaitAsync(cancellationToken);
            return new TripBookingImpactProjection(tripId, 0, []);
        }
    }

    private sealed record Delivery(string RoutingKey, Guid MessageId, string Payload);
}
