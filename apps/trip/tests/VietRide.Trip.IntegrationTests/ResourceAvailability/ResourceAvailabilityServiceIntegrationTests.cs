using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Application.Features.TripGeneration;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Persistence.Repositories;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.ResourceAvailability;

public sealed class ResourceAvailabilityServiceIntegrationTests
{
    private const string ScratchDatabasePrefix = "vietride_resource_availability_";
    private const string BeforeResourceReservationMigration = "20260810113814_AddAdministrativeLocationHierarchy";
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);

    [Fact]
    public async Task Migration_CleanDatabase_CreatesReversibleTriggerAndExclusionConstraint()
    {
        await using var fixture = await Fixture.CreateAsync();

        (await ScalarAsync<bool>(fixture.Db,
                "SELECT to_regprocedure('vietride_trip.trg_set_resource_reservation_updated_at()') IS NOT NULL"))
            .Should().BeTrue();
        (await ScalarAsync<bool>(fixture.Db,
                "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_resource_reservations_no_overlap')"))
            .Should().BeTrue();

        var migrator = fixture.Db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeResourceReservationMigration);
        (await ScalarAsync<bool>(fixture.Db,
                "SELECT to_regprocedure('vietride_trip.trg_set_resource_reservation_updated_at()') IS NOT NULL"))
            .Should().BeFalse();

        await migrator.MigrateAsync();
        (await ScalarAsync<bool>(fixture.Db,
                "SELECT to_regprocedure('vietride_trip.trg_set_resource_reservation_updated_at()') IS NOT NULL"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task MainTrip_SameStationBoundary_ProtectsDriverAssistantAndVehicleAcrossRoles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            fixture.Assistant1Id,
            At(8),
            At(10));
        await fixture.ReserveTripAsync(first);

        var driverCandidate = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(10, 29),
            At(12, 29));
        var driverPreview = await fixture.CheckTripAsync(driverCandidate);
        driverPreview.Available.Should().BeFalse();
        driverPreview.Conflicts.Should().ContainSingle(conflict =>
            conflict.ResourceRole == "DRIVER"
            && conflict.Reason == "TURNAROUND_REQUIRED"
            && conflict.EarliestFeasibleStartAt == At(10, 30));

        var exactBoundary = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(10, 30),
            At(12, 30));
        (await fixture.CheckTripAsync(exactBoundary)).Available.Should().BeTrue();

        var assistantAsDriver = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle2Id,
            fixture.Assistant1Id,
            null,
            At(9),
            At(11));
        var assistantException = await fixture.ReserveTripExpectConflictAsync(assistantAsDriver);
        assistantException.ErrorCode.Should().Be("TRIP_DRIVER_CONFLICT");

        var vehicleCandidate = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle1Id,
            fixture.Driver2Id,
            null,
            At(9),
            At(11));
        var vehicleException = await fixture.ReserveTripExpectConflictAsync(vehicleCandidate);
        vehicleException.ErrorCode.Should().Be("TRIP_VEHICLE_CONFLICT");

        (await fixture.Db.ResourceReservations.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Reposition_UsesGoogleDuration_FailsClosedAndWritesNoPartialReservation()
    {
        await using var fixture = await Fixture.CreateAsync(travelMinutes: 60);
        var first = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        await fixture.ReserveTripAsync(first);

        var tooEarly = fixture.CreateTrip(
            fixture.RouteCdId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(11, 29),
            At(13, 29));
        var preview = await fixture.CheckTripAsync(tooEarly);
        preview.Available.Should().BeFalse();
        preview.Conflicts.Should().ContainSingle(conflict =>
            conflict.Reason == "REPOSITION_REQUIRED"
            && conflict.RequiredTravelMinutes == 60
            && conflict.EarliestFeasibleStartAt == At(11, 30));

        var boundary = fixture.CreateTrip(
            fixture.RouteCdId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(11, 30),
            At(13, 30));
        (await fixture.CheckTripAsync(boundary)).Available.Should().BeTrue();

        await using var unavailableFixture = await Fixture.CreateAsync();
        unavailableFixture.TravelTime.SetUnavailable("Google Routes timeout");
        var unavailableFirst = unavailableFixture.CreateTrip(
            unavailableFixture.RouteAbId,
            unavailableFixture.Vehicle1Id,
            unavailableFixture.Driver1Id,
            null,
            At(8),
            At(10));
        await unavailableFixture.ReserveTripAsync(unavailableFirst);
        var unavailable = unavailableFixture.CreateTrip(
            unavailableFixture.RouteCdId,
            unavailableFixture.Vehicle3Id,
            unavailableFixture.Driver1Id,
            null,
            At(12),
            At(14));
        var action = () => unavailableFixture.ReserveTripAsync(unavailable);
        var exception = await action.Should().ThrowAsync<ResourceTravelTimeUnavailableException>();
        exception.Which.ErrorCode.Should().Be("RESOURCE_TRAVEL_TIME_UNAVAILABLE");
        unavailableFixture.Db.ChangeTracker.Clear();
        (await unavailableFixture.Db.ResourceReservations.CountAsync(item => item.TripId == unavailable.Id)).Should().Be(0);
        (await unavailableFixture.Db.Trips.CountAsync(item => item.Id == unavailable.Id)).Should().Be(0);
    }

    [Fact]
    public async Task MissingCoordinates_FailsClosedBeforeScheduleOrReservationMutation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var action = () => fixture.Service.CheckDriverScheduleAsync(
            new DriverScheduleAvailabilityInput(
                fixture.OperatorId,
                fixture.RouteMissingCoordinatesId,
                fixture.Vehicle1Id,
                fixture.Driver1Id,
                null,
                [1],
                new TimeOnly(8, 0),
                new DateOnly(2026, 8, 10),
                null),
            acquireLocks: false);

        var exception = await action.Should().ThrowAsync<ResourceTravelTimeUnavailableException>();
        exception.Which.ErrorCode.Should().Be("RESOURCE_TRAVEL_TIME_UNAVAILABLE");
        (await fixture.Db.ResourceReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MainAndShuttle_ConflictInBothCreationOrders_AndShuttleManifestDefinesEndpoint()
    {
        await using var firstFixture = await Fixture.CreateAsync();
        var main = firstFixture.CreateTrip(
            firstFixture.RouteAbId,
            firstFixture.Vehicle1Id,
            firstFixture.Driver1Id,
            null,
            At(8),
            At(10));
        await firstFixture.ReserveTripAsync(main);
        var outbound = await firstFixture.CreateShuttleAsync(
            main,
            ShuttleTrip.OutboundDirection,
            firstFixture.Driver1Id,
            firstFixture.Vehicle2Id,
            At(10, 1),
            At(10, 31),
            pickupLat: 10.50m,
            pickupLng: 105.50m);
        var shuttleConflict = await firstFixture.ReserveShuttleExpectConflictAsync(outbound);
        shuttleConflict.ErrorCode.Should().Be("SHUTTLE_DRIVER_CONFLICT");
        shuttleConflict.Errors.Should().Contain(error =>
            error.Field == "conflictReason" && error.Message == "TURNAROUND_REQUIRED");

        await using var reverseFixture = await Fixture.CreateAsync();
        var reverseMain = reverseFixture.CreateTrip(
            reverseFixture.RouteAbId,
            reverseFixture.Vehicle1Id,
            reverseFixture.Driver1Id,
            null,
            At(8),
            At(10));
        await reverseFixture.PersistTripAsync(reverseMain);
        var inbound = await reverseFixture.CreateShuttleAsync(
            reverseMain,
            ShuttleTrip.InboundDirection,
            reverseFixture.Driver1Id,
            reverseFixture.Vehicle2Id,
            At(7),
            At(7, 45),
            pickupLat: 10.00m,
            pickupLng: 106.00m);
        await reverseFixture.ReserveShuttleAsync(inbound);
        var mainConflict = await reverseFixture.ReserveTripExpectConflictAsync(reverseMain, alreadyPersisted: true);
        mainConflict.ErrorCode.Should().Be("TRIP_DRIVER_CONFLICT");
        mainConflict.Errors.Should().Contain(error =>
            error.Field == "conflictReason" && error.Message == "TURNAROUND_REQUIRED");
    }

    [Fact]
    public async Task ShuttleToShuttle_ProtectsDriverAndVehicle_AndLifecycleReleasesResources()
    {
        await using var fixture = await Fixture.CreateAsync();
        var main = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(12),
            At(14));
        await fixture.PersistTripAsync(main);
        var first = await fixture.CreateShuttleAsync(
            main,
            ShuttleTrip.OutboundDirection,
            fixture.Driver2Id,
            fixture.Vehicle2Id,
            At(10),
            At(10, 30),
            10.50m,
            105.50m);
        await fixture.ReserveShuttleAsync(first);

        var overlappingDriver = await fixture.CreateShuttleAsync(
            main,
            ShuttleTrip.OutboundDirection,
            fixture.Driver2Id,
            fixture.Vehicle3Id,
            At(10, 15),
            At(10, 45),
            10.50m,
            105.50m);
        (await fixture.ReserveShuttleExpectConflictAsync(overlappingDriver)).ErrorCode
            .Should().Be("SHUTTLE_DRIVER_CONFLICT");

        var overlappingVehicle = await fixture.CreateShuttleAsync(
            main,
            ShuttleTrip.OutboundDirection,
            fixture.Driver3Id,
            fixture.Vehicle2Id,
            At(10, 15),
            At(10, 45),
            10.50m,
            105.50m);
        (await fixture.ReserveShuttleExpectConflictAsync(overlappingVehicle)).ErrorCode
            .Should().Be("SHUTTLE_VEHICLE_CONFLICT");

        await fixture.ExecuteAsync(async () =>
        {
            first.Shuttle.Start(At(10));
            await fixture.Service.ActivateShuttleTripAsync(first.Shuttle.Id, At(10));
        });
        (await fixture.Db.ResourceReservations.Where(item => item.ShuttleTripId == first.Shuttle.Id).ToArrayAsync())
            .Should().OnlyContain(item => item.Status == ResourceReservationStatus.ACTIVE);

        await fixture.ExecuteAsync(async () =>
        {
            first.Shuttle.Complete(At(10, 35));
            await fixture.Service.ReleaseShuttleTripAsync(first.Shuttle.Id, At(10, 35));
        });
        (await fixture.Db.ResourceReservations.Where(item => item.ShuttleTripId == first.Shuttle.Id).ToArrayAsync())
            .Should().OnlyContain(item => item.Status == ResourceReservationStatus.RELEASED);
    }

    [Fact]
    public async Task DriverSchedule_ChecksWeeklyValidityOvernightCrossRoleAndMultipleDrivers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existing = DriverSchedule.Create(
            fixture.OperatorId,
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            fixture.Assistant1Id,
            JsonSerializer.SerializeToElement(new[] { 7 }),
            new TimeOnly(23, 0),
            new DateOnly(2026, 1, 4),
            null,
            isActive: true);
        fixture.Db.DriverSchedules.Add(existing);
        await fixture.Db.SaveChangesAsync();

        var overnight = await fixture.Service.CheckDriverScheduleAsync(
            new DriverScheduleAvailabilityInput(
                fixture.OperatorId,
                fixture.RouteBaId,
                fixture.Vehicle2Id,
                fixture.Driver1Id,
                null,
                [1],
                new TimeOnly(0, 30),
                new DateOnly(2030, 1, 7),
                null),
            acquireLocks: false);
        overnight.Available.Should().BeFalse();
        overnight.Conflicts.Should().Contain(conflict => conflict.Reason == "TIME_OVERLAP");

        var assistantAsDriver = await fixture.Service.CheckDriverScheduleAsync(
            new DriverScheduleAvailabilityInput(
                fixture.OperatorId,
                fixture.RouteAbId,
                fixture.Vehicle2Id,
                fixture.Assistant1Id,
                null,
                [7],
                new TimeOnly(23, 30),
                new DateOnly(2026, 1, 4),
                null),
            acquireLocks: false);
        assistantAsDriver.Available.Should().BeFalse();

        var independentDriver = await fixture.Service.CheckDriverScheduleAsync(
            new DriverScheduleAvailabilityInput(
                fixture.OperatorId,
                fixture.RouteAbId,
                fixture.Vehicle2Id,
                fixture.Driver2Id,
                null,
                [7],
                new TimeOnly(23, 30),
                new DateOnly(2026, 1, 4),
                null),
            acquireLocks: false);
        independentDriver.Available.Should().BeTrue();
    }

    [Fact]
    public async Task DriverScheduleGeneration_CreatesThirtyDayTripsWithReservationsAndSkipsNearTimeConflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstSchedule = DriverSchedule.Create(
            fixture.OperatorId,
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            fixture.Assistant1Id,
            JsonSerializer.SerializeToElement(new[] { 1 }),
            new TimeOnly(8, 0),
            new DateOnly(2026, 8, 10),
            null,
            isActive: true);
        fixture.Db.DriverSchedules.Add(firstSchedule);
        await fixture.Db.SaveChangesAsync();

        GenerateTripsForScheduleResult? firstResult = null;
        await fixture.ExecuteAsync(async () =>
        {
            firstResult = await CreateGenerationService(fixture).GenerateAsync(
                firstSchedule.Id,
                CancellationToken.None);
        });
        firstResult.Should().NotBeNull();
        firstResult!.GeneratedCount.Should().Be(5);
        firstResult.SkippedCount.Should().Be(0);

        fixture.Db.ChangeTracker.Clear();
        var generated = await fixture.Db.Trips.AsNoTracking()
            .Where(item => item.DriverScheduleId == firstSchedule.Id)
            .OrderBy(item => item.DepartureDateTime)
            .ToArrayAsync();
        generated.Should().HaveCount(5).And.OnlyContain(item => item.Source == TripSource.AUTO_FROM_SCHEDULE);
        foreach (var trip in generated)
        {
            (await fixture.Db.ResourceReservations.AsNoTracking()
                    .Where(item => item.TripId == trip.Id)
                    .ToArrayAsync())
                .Should().HaveCount(3)
                .And.OnlyContain(item => item.Status == ResourceReservationStatus.RESERVED);
        }

        var conflictingSchedule = DriverSchedule.Create(
            fixture.OperatorId,
            fixture.RouteAbId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            JsonSerializer.SerializeToElement(new[] { 1 }),
            new TimeOnly(8, 1),
            new DateOnly(2026, 8, 10),
            null,
            isActive: true);
        fixture.Db.DriverSchedules.Add(conflictingSchedule);
        await fixture.Db.SaveChangesAsync();

        GenerateTripsForScheduleResult? conflictResult = null;
        await fixture.ExecuteAsync(async () =>
        {
            conflictResult = await CreateGenerationService(fixture).GenerateAsync(
                conflictingSchedule.Id,
                CancellationToken.None);
        });
        conflictResult.Should().NotBeNull();
        conflictResult!.GeneratedCount.Should().Be(0);
        conflictResult.SkippedCount.Should().Be(5);
        (await fixture.Db.Trips.CountAsync(item => item.DriverScheduleId == conflictingSchedule.Id)).Should().Be(0);
        (await fixture.Db.TripGenerationSkipLogs.CountAsync(item =>
            item.DriverScheduleId == conflictingSchedule.Id
            && item.Reason == TripGenerationSkipReason.DRIVER_CONFLICT)).Should().Be(5);
    }

    [Fact]
    public async Task MainTripLifecycle_ActiveBlocksNext_StartRollbackThenCompleteAllowsStartAndCancelFreesResource()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        var next = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(10, 30),
            At(12, 30));
        await fixture.ReserveTripAsync(first);
        await fixture.ReserveTripAsync(next);

        await fixture.ExecuteAsync(async () =>
        {
            first.MarkBoarding(At(7, 45));
            first.Start(At(8));
            await fixture.Service.ActivateTripAsync(first.Id, At(8));
        });

        var blockedStart = () => fixture.ExecuteAsync(async () =>
        {
            next.MarkBoarding(At(10, 25));
            next.Start(At(10, 30));
            await fixture.Service.ActivateTripAsync(next.Id, At(10, 30));
        });
        var activeException = await blockedStart.Should().ThrowAsync<CodedConflictException>();
        activeException.Which.Errors.Should().Contain(error =>
            error.Field == "conflictReason" && error.Message == "RESOURCE_ACTIVE");
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.Trips.SingleAsync(item => item.Id == next.Id)).Status.Should().Be(TripStatus.SCHEDULED);

        await fixture.ExecuteAsync(async () =>
        {
            var current = await fixture.Db.Trips.SingleAsync(item => item.Id == first.Id);
            current.CompleteManually(At(10, 31), fixture.Driver1Id);
            await fixture.Service.ReleaseTripAsync(first.Id, At(10, 31));
        });
        await fixture.ExecuteAsync(async () =>
        {
            var current = await fixture.Db.Trips.SingleAsync(item => item.Id == next.Id);
            current.MarkBoarding(At(10, 31));
            current.Start(At(10, 31));
            await fixture.Service.ActivateTripAsync(next.Id, At(10, 31));
        });

        var cancellable = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle3Id,
            fixture.Driver3Id,
            null,
            At(15),
            At(17));
        await fixture.ReserveTripAsync(cancellable);
        await fixture.ExecuteAsync(async () =>
        {
            cancellable.Cancel(At(14), fixture.Driver3Id, "operator cancelled");
            await fixture.Service.CancelTripAsync(cancellable.Id, At(14));
        });
        (await fixture.Db.ResourceReservations.Where(item => item.TripId == cancellable.Id).ToArrayAsync())
            .Should().OnlyContain(item => item.Status == ResourceReservationStatus.CANCELLED);
    }

    [Fact]
    public async Task CrewVehicleAndTimeMutation_ConflictRollsBackTripAndReservationTogether()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        var second = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle2Id,
            fixture.Driver2Id,
            null,
            At(8),
            At(10));
        await fixture.ReserveTripAsync(first);
        await fixture.ReserveTripAsync(second);

        var action = () => fixture.ExecuteAsync(async () =>
        {
            var locked = await fixture.Db.Trips.SingleAsync(item => item.Id == second.Id);
            locked.ChangeCrew(fixture.Driver1Id, null);
            locked.ChangeVehicle(fixture.Vehicle1Id);
            locked.Reschedule(At(8, 30), At(10, 30));
            await fixture.Service.RefreshTripAsync(locked);
        });
        await action.Should().ThrowAsync<CodedConflictException>();

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Trips.SingleAsync(item => item.Id == second.Id);
        persisted.DriverUserId.Should().Be(fixture.Driver2Id);
        persisted.VehicleId.Should().Be(fixture.Vehicle2Id);
        persisted.DepartureDateTime.Should().Be(At(8));
        var reservations = await fixture.Db.ResourceReservations
            .Where(item => item.TripId == second.Id)
            .ToArrayAsync();
        reservations.Should().Contain(item => item.ResourceId == fixture.Driver2Id);
        reservations.Should().Contain(item => item.ResourceId == fixture.Vehicle2Id);
    }

    [Fact]
    public async Task VehicleSubstitution_ReleasesTrackedOldReservationBeforeReservingReplacementInSameTransaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oldTrip = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        await fixture.ReserveTripAsync(oldTrip);
        await fixture.ExecuteAsync(async () =>
        {
            oldTrip.MarkBoarding(At(7, 45));
            oldTrip.Start(At(8));
            await fixture.Service.ActivateTripAsync(oldTrip.Id, At(8));
        });

        var replacement = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(9),
            At(10));
        await fixture.ExecuteAsync(async () =>
        {
            oldTrip.SubstituteVehicle(At(9), "breakdown");
            fixture.Db.Trips.Add(replacement);
            await fixture.Db.SaveChangesAsync();
            await fixture.Service.ReleaseTripAsync(oldTrip.Id, At(9));
            await fixture.Service.ReserveTripAsync(replacement);
        });

        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.ResourceReservations.Where(item => item.TripId == oldTrip.Id).ToArrayAsync())
            .Should().OnlyContain(item => item.Status == ResourceReservationStatus.RELEASED);
        (await fixture.Db.ResourceReservations.Where(item => item.TripId == replacement.Id).ToArrayAsync())
            .Should().HaveCount(2)
            .And.OnlyContain(item => item.Status == ResourceReservationStatus.RESERVED);
    }

    [Fact]
    public async Task ConcurrentMainTripReservation_ExactlyOneRequestWinsAndDatabaseHasNoOverlap()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        var second = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle2Id,
            fixture.Driver1Id,
            null,
            At(8, 1),
            At(10, 1));
        await fixture.PersistTripAsync(first);
        await fixture.PersistTripAsync(second);

        async Task<Exception?> AttemptAsync(Guid tripId)
        {
            await using var db = CreateDbContext(fixture.DatabaseName, fixture.Clock);
            var service = CreateService(db, new StubTravelTimeClient(0), fixture.Clock);
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var trip = await db.Trips.SingleAsync(item => item.Id == tripId);
                await service.ReserveTripAsync(trip);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return null;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync();
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(AttemptAsync(first.Id), AttemptAsync(second.Id));
        outcomes.Count(item => item is null).Should().Be(1);
        outcomes.Count(item => item is CodedConflictException conflict
            && conflict.ErrorCode == "TRIP_DRIVER_CONFLICT").Should().Be(1);
        fixture.Db.ChangeTracker.Clear();
        var driverReservations = await fixture.Db.ResourceReservations.AsNoTracking()
            .Where(item => item.ResourceType == ResourceReservationType.CREW
                && item.ResourceId == fixture.Driver1Id
                && item.Status == ResourceReservationStatus.RESERVED)
            .ToArrayAsync();
        driverReservations.Should().ContainSingle();
    }

    [Fact]
    public async Task VehicleProjection_ReturnsCurrentActiveAndNearestReservedAssignmentWithDriver()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = fixture.CreateTrip(
            fixture.RouteAbId,
            fixture.Vehicle1Id,
            fixture.Driver1Id,
            null,
            At(8),
            At(10));
        var next = fixture.CreateTrip(
            fixture.RouteBaId,
            fixture.Vehicle1Id,
            fixture.Driver2Id,
            null,
            At(10, 30),
            At(12, 30));
        await fixture.ReserveTripAsync(current);
        await fixture.ReserveTripAsync(next);
        await fixture.ExecuteAsync(() => fixture.Service.ActivateTripAsync(current.Id, At(8)));

        var projection = await fixture.Service.GetVehicleAssignmentsAsync(
            fixture.OperatorId,
            [fixture.Vehicle1Id],
            At(9));

        var currentProjection = projection[fixture.Vehicle1Id].Current;
        currentProjection.Should().NotBeNull();
        currentProjection!.DriverUserId.Should().Be(fixture.Driver1Id);
        currentProjection.Status.Should().Be("ACTIVE");
        var nextProjection = projection[fixture.Vehicle1Id].Next;
        nextProjection.Should().NotBeNull();
        nextProjection!.DriverUserId.Should().Be(fixture.Driver2Id);
        nextProjection.StartsAt.Should().Be(At(10, 30));
    }

    private static DateTimeOffset At(int hour, int minute = 0) => Now.AddHours(hour).AddMinutes(minute);

    private static IResourceAvailabilityService CreateService(
        TripDbContext db,
        IRepositionTravelTimeClient travelTime,
        IClock clock)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Services.ResourceAvailabilityService",
            throwOnError: true)!;
        return (IResourceAvailabilityService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db, travelTime, clock],
            culture: null)!;
    }

    private static TripGenerationService CreateGenerationService(Fixture fixture) =>
        new(
            fixture.Clock,
            new DriverScheduleRepository(fixture.Db),
            CreateInternal<IRouteRepository>(fixture.Db, "RouteRepository"),
            CreateInternal<IRouteStopRepository>(fixture.Db, "RouteStopRepository"),
            CreateInternal<IRouteStopFareTemplateRepository>(fixture.Db, "RouteStopFareTemplateRepository"),
            CreateInternal<IVehicleRepository>(fixture.Db, "VehicleRepository"),
            CreateInternal<ITripRepository>(fixture.Db, "TripRepository"),
            CreateInternal<ITripSeatRepository>(fixture.Db, "TripSeatRepository"),
            CreateInternal<ITripStopRepository>(fixture.Db, "TripStopRepository"),
            CreateInternal<ITripStopFareRepository>(fixture.Db, "TripStopFareRepository"),
            CreateInternal<ITripGenerationSkipLogRepository>(fixture.Db, "TripGenerationSkipLogRepository"),
            resourceAvailability: fixture.Service);

    private static T CreateInternal<T>(TripDbContext db, string typeName)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            $"VietRide.Trip.Infrastructure.Persistence.Repositories.{typeName}",
            throwOnError: true)!;
        return (T)Activator.CreateInstance(type, db)!;
    }

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

    private static async Task<T> ScalarAsync<T>(TripDbContext db, string sql)
    {
        var wasClosed = db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed;
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (wasClosed)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string databaseName,
            TripDbContext db,
            FrozenClock clock,
            StubTravelTimeClient travelTime,
            IResourceAvailabilityService service,
            Seed seed)
        {
            DatabaseName = databaseName;
            Db = db;
            Clock = clock;
            TravelTime = travelTime;
            Service = service;
            OperatorId = seed.OperatorId;
            RouteAbId = seed.RouteAbId;
            RouteBaId = seed.RouteBaId;
            RouteCdId = seed.RouteCdId;
            RouteMissingCoordinatesId = seed.RouteMissingCoordinatesId;
            Vehicle1Id = seed.Vehicle1Id;
            Vehicle2Id = seed.Vehicle2Id;
            Vehicle3Id = seed.Vehicle3Id;
            Driver1Id = seed.Driver1Id;
            Driver2Id = seed.Driver2Id;
            Driver3Id = seed.Driver3Id;
            Assistant1Id = seed.Assistant1Id;
            SeatLayout = seed.SeatLayout.Clone();
        }

        public string DatabaseName { get; }
        public TripDbContext Db { get; }
        public FrozenClock Clock { get; }
        public StubTravelTimeClient TravelTime { get; }
        public IResourceAvailabilityService Service { get; }
        public Guid OperatorId { get; }
        public Guid RouteAbId { get; }
        public Guid RouteBaId { get; }
        public Guid RouteCdId { get; }
        public Guid RouteMissingCoordinatesId { get; }
        public Guid Vehicle1Id { get; }
        public Guid Vehicle2Id { get; }
        public Guid Vehicle3Id { get; }
        public Guid Driver1Id { get; }
        public Guid Driver2Id { get; }
        public Guid Driver3Id { get; }
        public Guid Assistant1Id { get; }
        public JsonElement SeatLayout { get; }

        public static async Task<Fixture> CreateAsync(int travelMinutes = 0)
        {
            var databaseName = $"{ScratchDatabasePrefix}{Guid.NewGuid():N}";
            var clock = new FrozenClock(Now);
            var db = CreateDbContext(databaseName, clock);
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db);
            var travelTime = new StubTravelTimeClient(travelMinutes);
            return new Fixture(
                databaseName,
                db,
                clock,
                travelTime,
                CreateService(db, travelTime, clock),
                seed);
        }

        public TripEntity CreateTrip(
            Guid routeId,
            Guid vehicleId,
            Guid driverId,
            Guid? assistantId,
            DateTimeOffset start,
            DateTimeOffset end) =>
            TripEntity.Create(
                OperatorId,
                routeId,
                vehicleId,
                driverId,
                assistantId,
                null,
                start,
                end,
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                500m,
                maxCargoVolumeM3: 5m,
                estimatedPassengerLuggageKg: 0m,
                seatLayoutSnapshotJson: SeatLayout);

        public async Task PersistTripAsync(TripEntity trip)
        {
            Db.Trips.Add(trip);
            await Db.SaveChangesAsync();
        }

        public async Task ReserveTripAsync(TripEntity trip, bool alreadyPersisted = false)
        {
            await ExecuteAsync(async () =>
            {
                if (!alreadyPersisted)
                {
                    Db.Trips.Add(trip);
                    await Db.SaveChangesAsync();
                }

                await Service.ReserveTripAsync(trip);
            });
        }

        public async Task<CodedConflictException> ReserveTripExpectConflictAsync(
            TripEntity trip,
            bool alreadyPersisted = false)
        {
            var action = () => ReserveTripAsync(trip, alreadyPersisted);
            var assertion = await action.Should().ThrowAsync<CodedConflictException>();
            Db.ChangeTracker.Clear();
            return assertion.Which;
        }

        public Task<ResourceAvailabilityResult> CheckTripAsync(TripEntity trip) =>
            Service.CheckCandidateAsync(
                new ResourceAvailabilityCandidate(
                    trip.OperatorId,
                    AssignmentSourceType.TRIP,
                    trip.Id,
                    trip.Id,
                    null,
                    trip.DepartureDateTime,
                    trip.EstimatedArrivalTime,
                    ResolveStartLocation(trip.RouteId),
                    ResolveEndLocation(trip.RouteId),
                    BuildResources(trip)),
                acquireLocks: false);

        public async Task<(ShuttleTrip Shuttle, Guid BookingId)> CreateShuttleAsync(
            TripEntity mainTrip,
            string direction,
            Guid driverId,
            Guid vehicleId,
            DateTimeOffset start,
            DateTimeOffset end,
            decimal pickupLat,
            decimal pickupLng)
        {
            if (!await Db.Trips.AnyAsync(item => item.Id == mainTrip.Id))
            {
                Db.Trips.Add(mainTrip);
            }

            var bookingId = Guid.NewGuid();
            var passenger = ShuttlePassenger.Request(
                mainTrip.Id,
                bookingId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manifest endpoint",
                pickupLat,
                pickupLng,
                direction);
            var stationId = direction == ShuttleTrip.InboundDirection
                ? (await Db.Routes.AsNoTracking().SingleAsync(item => item.Id == mainTrip.RouteId)).OriginStationId
                : (await Db.Routes.AsNoTracking().SingleAsync(item => item.Id == mainTrip.RouteId)).DestinationStationId;
            var shuttle = ShuttleTrip.Create(
                OperatorId,
                mainTrip.Id,
                stationId,
                driverId,
                vehicleId,
                start,
                end,
                notes: null,
                direction);
            Db.AddRange(passenger, shuttle);
            await Db.SaveChangesAsync();
            return (shuttle, bookingId);
        }

        public Task ReserveShuttleAsync((ShuttleTrip Shuttle, Guid BookingId) value) =>
            ExecuteAsync(() => Service.ReserveShuttleTripAsync(value.Shuttle, [value.BookingId]));

        public async Task<CodedConflictException> ReserveShuttleExpectConflictAsync(
            (ShuttleTrip Shuttle, Guid BookingId) value)
        {
            var action = () => ReserveShuttleAsync(value);
            var assertion = await action.Should().ThrowAsync<CodedConflictException>();
            Db.ChangeTracker.Clear();
            return assertion.Which;
        }

        public async Task ExecuteAsync(Func<Task> operation)
        {
            await using var transaction = await Db.Database.BeginTransactionAsync();
            try
            {
                await operation();
                await Db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Db.Database.EnsureDeletedAsync();
            }
            finally
            {
                await Db.DisposeAsync();
            }
        }

        private ResourceLocationSnapshot ResolveStartLocation(Guid routeId)
        {
            var route = Db.Routes.Local.Single(item => item.Id == routeId);
            var station = Db.Stations.Local.Single(item => item.Id == route.OriginStationId);
            return new ResourceLocationSnapshot(station.Id, station.Latitude, station.Longitude);
        }

        private ResourceLocationSnapshot ResolveEndLocation(Guid routeId)
        {
            var route = Db.Routes.Local.Single(item => item.Id == routeId);
            var station = Db.Stations.Local.Single(item => item.Id == route.DestinationStationId);
            return new ResourceLocationSnapshot(station.Id, station.Latitude, station.Longitude);
        }

        private static IReadOnlyList<AvailabilityResource> BuildResources(TripEntity trip)
        {
            var result = new List<AvailabilityResource>
            {
                new(ResourceReservationType.CREW, ResourceReservationRole.DRIVER, trip.DriverUserId),
                new(ResourceReservationType.VEHICLE, ResourceReservationRole.VEHICLE, trip.VehicleId),
            };
            if (trip.AssistantUserId.HasValue)
            {
                result.Insert(1, new AvailabilityResource(
                    ResourceReservationType.CREW,
                    ResourceReservationRole.ASSISTANT,
                    trip.AssistantUserId.Value));
            }

            return result;
        }
    }

    private static async Task<Seed> SeedAsync(TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var stationA = Station.Create("A", $"a-{Guid.NewGuid():N}", "HCM", "Ward A", latitude: 10.00m, longitude: 106.00m, supportsShuttle: true);
        var stationB = Station.Create("B", $"b-{Guid.NewGuid():N}", "Can Tho", "Ward B", latitude: 10.50m, longitude: 105.50m, supportsShuttle: true);
        var stationC = Station.Create("C", $"c-{Guid.NewGuid():N}", "Phan Thiet", "Ward C", latitude: 11.00m, longitude: 108.00m, supportsShuttle: true);
        var stationD = Station.Create("D", $"d-{Guid.NewGuid():N}", "Da Nang", "Ward D", latitude: 16.00m, longitude: 108.20m, supportsShuttle: true);
        var missing = Station.Create("Missing", $"missing-{Guid.NewGuid():N}", "Unknown", "Ward M");
        var routeAb = VietRide.Trip.Domain.Entities.Route.Create(operatorId, "A-B", stationA.Id, stationB.Id, Money.FromRaw(100_000), 100m, 120);
        var routeBa = VietRide.Trip.Domain.Entities.Route.Create(operatorId, "B-A", stationB.Id, stationA.Id, Money.FromRaw(100_000), 100m, 120);
        var routeCd = VietRide.Trip.Domain.Entities.Route.Create(operatorId, "C-D", stationC.Id, stationD.Id, Money.FromRaw(100_000), 500m, 120);
        var routeMissing = VietRide.Trip.Domain.Entities.Route.Create(operatorId, "Missing-B", missing.Id, stationB.Id, Money.FromRaw(100_000), 10m, 60);
        var vehicleType = VehicleType.Create($"TEST-{Guid.NewGuid():N}", "Test", 10, 1);
        var layout = JsonSerializer.SerializeToElement(new
        {
            version = 1,
            vehicleTypeCode = vehicleType.Code,
            totalSeats = 1,
            rows = 1,
            cols = 1,
            decks = 1,
            aisles = Array.Empty<object>(),
            seats = new[]
            {
                new
                {
                    seatNumber = "A01",
                    row = 1,
                    col = 1,
                    deck = 1,
                    type = "STANDARD",
                    isWindow = true,
                    isAisle = false,
                    disabled = false,
                },
            },
        });
        var vehicle1 = Vehicle.Create(operatorId, vehicleType.Id, $"TEST-{Guid.NewGuid():N}"[..15], layout, 1, 500m, 5m);
        var vehicle2 = Vehicle.Create(operatorId, vehicleType.Id, $"TEST-{Guid.NewGuid():N}"[..15], layout, 1, 500m, 5m);
        var vehicle3 = Vehicle.Create(operatorId, vehicleType.Id, $"TEST-{Guid.NewGuid():N}"[..15], layout, 1, 500m, 5m);
        db.AddRange(stationA, stationB, stationC, stationD, missing, routeAb, routeBa, routeCd, routeMissing, vehicleType, vehicle1, vehicle2, vehicle3);
        await db.SaveChangesAsync();
        return new Seed(
            operatorId,
            routeAb.Id,
            routeBa.Id,
            routeCd.Id,
            routeMissing.Id,
            vehicle1.Id,
            vehicle2.Id,
            vehicle3.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            layout.Clone());
    }

    private sealed record Seed(
        Guid OperatorId,
        Guid RouteAbId,
        Guid RouteBaId,
        Guid RouteCdId,
        Guid RouteMissingCoordinatesId,
        Guid Vehicle1Id,
        Guid Vehicle2Id,
        Guid Vehicle3Id,
        Guid Driver1Id,
        Guid Driver2Id,
        Guid Driver3Id,
        Guid Assistant1Id,
        JsonElement SeatLayout);

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubTravelTimeClient(int durationMinutes) : IRepositionTravelTimeClient
    {
        private string? unavailableMessage;

        public void SetUnavailable(string message) => unavailableMessage = message;

        public Task<RepositionTravelTimeResult> CalculateAsync(
            decimal originLatitude,
            decimal originLongitude,
            decimal destinationLatitude,
            decimal destinationLongitude,
            CancellationToken cancellationToken = default)
        {
            _ = originLatitude;
            _ = originLongitude;
            _ = destinationLatitude;
            _ = destinationLongitude;
            _ = cancellationToken;
            return Task.FromResult(unavailableMessage is null
                ? RepositionTravelTimeResult.Success(durationMinutes, durationMinutes * 1_000)
                : RepositionTravelTimeResult.Unavailable(unavailableMessage));
        }
    }
}
