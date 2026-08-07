using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class Day32CargoTransferRepositoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReservedTransfer_RestoresTargetLedgerAndMovesCountersAtomically()
    {
        var databaseName =
            $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(
                db,
                TripCargoParcel.ReservedState,
                12.5m,
                targetMaxCargoWeightKg: 100m,
                targetHasReleasedLedger: true);
            var handler = CreateHandler(db);

            var result = await handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.ReservedState,
                    AllowCapacityOverflow: false),
                CancellationToken.None);

            result.WeightKg.Should().Be(12.5m);
            result.VolumeM3.Should().Be(1m);
            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var sourceTrip = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(trip => trip.Id == seed.SourceTripId);
            var targetTrip = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(trip => trip.Id == seed.TargetTripId);
            var sourceCargo = await assertionDb.TripCargoParcels.AsNoTracking()
                .SingleAsync(cargo =>
                    cargo.TripId == seed.SourceTripId && cargo.ParcelId == seed.ParcelId);
            var targetCargo = await assertionDb.TripCargoParcels.AsNoTracking()
                .SingleAsync(cargo =>
                    cargo.TripId == seed.TargetTripId && cargo.ParcelId == seed.ParcelId);

            sourceCargo.State.Should().Be(TripCargoParcel.ReleasedState);
            sourceTrip.ReservedParcelWeightKg.Should().Be(0m);
            targetCargo.Id.Should().Be(seed.ReleasedTargetCargoId!.Value);
            targetCargo.State.Should().Be(TripCargoParcel.ReservedState);
            targetCargo.WeightKg.Should().Be(12.5m);
            targetCargo.VolumeM3.Should().Be(1m);
            targetTrip.ReservedParcelWeightKg.Should().Be(12.5m);
            targetTrip.TotalLoadedWeightKg.Should().Be(0m);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                db,
                databaseName);
        }
    }

    [Fact]
    public async Task ReservedAlwaysEnforcesCapacity_LoadedAllowsOnlyExplicitOverflow()
    {
        var databaseName =
            $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(
                db,
                TripCargoParcel.LoadedState,
                30m,
                targetMaxCargoWeightKg: 20m,
                targetSource: TripSource.VEHICLE_SUBSTITUTION);
            var handler = CreateHandler(db);

            var reservedAction = () => handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.ReservedState,
                    AllowCapacityOverflow: true),
                CancellationToken.None);
            var reservedFailure =
                await reservedAction.Should().ThrowAsync<CodedValidationException>();
            reservedFailure.Which.ErrorCode.Should().Be("TRIP_CARGO_CAPACITY_EXCEEDED");

            var loadedAction = () => handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.LoadedState,
                    AllowCapacityOverflow: false),
                CancellationToken.None);
            var loadedFailure =
                await loadedAction.Should().ThrowAsync<CodedValidationException>();
            loadedFailure.Which.ErrorCode.Should().Be("TRIP_CARGO_CAPACITY_EXCEEDED");

            var result = await handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.LoadedState,
                    AllowCapacityOverflow: true),
                CancellationToken.None);

            result.TargetState.Should().Be(TripCargoParcel.LoadedState);
            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var trips = await assertionDb.Trips.AsNoTracking()
                .Where(trip =>
                    trip.Id == seed.SourceTripId || trip.Id == seed.TargetTripId)
                .ToDictionaryAsync(trip => trip.Id);
            trips[seed.SourceTripId].TotalLoadedWeightKg.Should().Be(0m);
            trips[seed.TargetTripId].TotalLoadedWeightKg.Should().Be(30m);
            var targetCargo = await assertionDb.TripCargoParcels.AsNoTracking()
                .SingleAsync(cargo =>
                    cargo.TripId == seed.TargetTripId && cargo.ParcelId == seed.ParcelId);
            targetCargo.State.Should().Be(TripCargoParcel.LoadedState);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                db,
                databaseName);
        }
    }

    [Fact]
    public async Task LoadedOverflowFlag_RequiresServerVerifiedVehicleSubstitutionTarget()
    {
        var databaseName =
            $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(
                db,
                TripCargoParcel.LoadedState,
                10m,
                targetMaxCargoWeightKg: 100m,
                targetSource: TripSource.MANUAL);
            var handler = CreateHandler(db);

            var action = () => handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.LoadedState,
                    AllowCapacityOverflow: true),
                CancellationToken.None);

            var failure = await action.Should().ThrowAsync<CodedValidationException>();
            failure.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var source = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(trip => trip.Id == seed.SourceTripId);
            source.TotalLoadedWeightKg.Should().Be(10m);
            (await assertionDb.TripCargoParcels.AsNoTracking()
                    .CountAsync(cargo =>
                        cargo.ParcelId == seed.ParcelId
                        && cargo.State != TripCargoParcel.ReleasedState))
                .Should().Be(1);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                db,
                databaseName);
        }
    }

    [Fact]
    public async Task CrossOperatorTransfer_IsRejectedWithoutMovingCargo()
    {
        var databaseName =
            $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(
                db,
                TripCargoParcel.ReservedState,
                10m,
                targetMaxCargoWeightKg: 100m,
                targetUsesOtherOperator: true);
            var handler = CreateHandler(db);

            var action = () => handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    seed.TargetTripId,
                    TripCargoParcel.ReservedState,
                    AllowCapacityOverflow: false),
                CancellationToken.None);

            var failure = await action.Should().ThrowAsync<CodedConflictException>();
            failure.Which.ErrorCode.Should().Be("TRIP_CARGO_TRANSFER_CONFLICT");
            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var source = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(trip => trip.Id == seed.SourceTripId);
            source.ReservedParcelWeightKg.Should().Be(10m);
            (await assertionDb.TripCargoParcels.AsNoTracking()
                    .CountAsync(cargo =>
                        cargo.ParcelId == seed.ParcelId
                        && cargo.State != TripCargoParcel.ReleasedState))
                .Should().Be(1);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                db,
                databaseName);
        }
    }

    [Fact]
    public async Task ConcurrentDifferentTransfers_HaveExactlyOneLedgerWinner()
    {
        var databaseName =
            $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var ownerDb =
            Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await ownerDb.Database.MigrateAsync();
            var seed = await SeedAsync(
                ownerDb,
                TripCargoParcel.ReservedState,
                15m,
                targetMaxCargoWeightKg: 100m);

            bool[] outcomes;
            await using (var firstDb =
                         Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName))
            await using (var secondDb =
                         Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName))
            {
                var first = AttemptTransferAsync(
                    CreateHandler(firstDb),
                    seed,
                    seed.TargetTripId);
                var second = AttemptTransferAsync(
                    CreateHandler(secondDb),
                    seed,
                    seed.SecondTargetTripId);
                outcomes = await Task.WhenAll(first, second);
            }

            outcomes.Should().ContainSingle(outcome => outcome);
            await using var assertionDb =
                Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var activeLedgers = await assertionDb.TripCargoParcels.AsNoTracking()
                .Where(cargo =>
                    cargo.ParcelId == seed.ParcelId
                    && cargo.State != TripCargoParcel.ReleasedState)
                .ToArrayAsync();
            activeLedgers.Should().ContainSingle();
            new[] { seed.TargetTripId, seed.SecondTargetTripId }
                .Should().Contain(activeLedgers[0].TripId);
            var source = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(trip => trip.Id == seed.SourceTripId);
            source.ReservedParcelWeightKg.Should().Be(0m);
            var targetReservedTotal = await assertionDb.Trips.AsNoTracking()
                .Where(trip =>
                    trip.Id == seed.TargetTripId
                    || trip.Id == seed.SecondTargetTripId)
                .SumAsync(trip => trip.ReservedParcelWeightKg);
            targetReservedTotal.Should().Be(15m);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                ownerDb,
                databaseName);
        }
    }

    private static async Task<bool> AttemptTransferAsync(
        TransferCargoCommandHandler handler,
        Seed seed,
        Guid targetTripId)
    {
        try
        {
            await handler.Handle(
                new TransferCargoCommand(
                    seed.SourceTripId,
                    seed.ParcelId,
                    targetTripId,
                    TripCargoParcel.ReservedState,
                    AllowCapacityOverflow: false),
                CancellationToken.None);
            return true;
        }
        catch (CodedConflictException exception)
            when (exception.ErrorCode == "TRIP_CARGO_TRANSFER_CONFLICT")
        {
            return false;
        }
    }

    private static TransferCargoCommandHandler CreateHandler(TripDbContext db)
    {
        var clock = new FixedClock();
        return new TransferCargoCommandHandler(
            CreateRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, clock)),
            new EfUnitOfWork(db),
            clock);
    }

    private static ITripRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(type, db)!;
    }

    private static async Task<Seed> SeedAsync(
        TripDbContext db,
        string sourceState,
        decimal sourceWeightKg,
        decimal targetMaxCargoWeightKg,
        bool targetUsesOtherOperator = false,
        bool targetHasReleasedLedger = false,
        TripSource targetSource = TripSource.MANUAL)
    {
        var operatorId = Guid.NewGuid();
        var otherOperatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Day 32 cargo origin",
            $"day32-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City");
        var destination = Station.Create(
            "Day 32 cargo destination",
            $"day32-destination-{Guid.NewGuid():N}",
            "Da Nang",
            "Da Nang");
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Day 32 cargo route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100m,
            240);
        var vehicleType = VehicleType.Create(
            $"DAY32_{Guid.NewGuid():N}",
            "Day 32 cargo vehicle",
            null,
            1);
        var vehicles = Enumerable.Range(0, 3)
            .Select(index => Vehicle.Create(
                index == 0 || !targetUsesOtherOperator ? operatorId : otherOperatorId,
                vehicleType.Id,
                $"D32-{index}-{Guid.NewGuid():N}"[..20],
                JsonSerializer.SerializeToElement(new
                {
                    version = 1,
                    vehicleTypeCode = "DAY32",
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
                }),
                1,
                100m,
                10m))
            .ToArray();
        var departure = Now.AddDays(1);
        var sourceTrip = CreateTrip(
            operatorId,
            route.Id,
            vehicles[0].Id,
            departure,
            100m,
            vehicles[0].SeatLayoutJson);
        var targetTrip = CreateTrip(
            targetUsesOtherOperator ? otherOperatorId : operatorId,
            route.Id,
            vehicles[1].Id,
            departure.AddMinutes(5),
            targetMaxCargoWeightKg,
            vehicles[1].SeatLayoutJson,
            targetSource);
        var secondTargetTrip = CreateTrip(
            operatorId,
            route.Id,
            vehicles[2].Id,
            departure.AddMinutes(10),
            targetMaxCargoWeightKg,
            vehicles[2].SeatLayoutJson);
        var parcelId = Guid.NewGuid();
        var sourceCargo = TripCargoParcel.Reserve(
            sourceTrip.Id,
            parcelId,
            sourceWeightKg,
            1m);
        if (sourceState == TripCargoParcel.LoadedState)
        {
            sourceCargo.MarkLoaded(Now);
            sourceTrip.UpdateCargoCounters(0m, 0m, sourceWeightKg, 1m);
        }
        else
        {
            sourceTrip.UpdateCargoCounters(sourceWeightKg, 1m, 0m, 0m);
        }

        TripCargoParcel? releasedTargetCargo = null;
        if (targetHasReleasedLedger)
        {
            releasedTargetCargo = TripCargoParcel.Reserve(
                targetTrip.Id,
                parcelId,
                1m,
                0.1m);
            releasedTargetCargo.Release(Now.AddDays(-1));
        }

        db.AddRange(
            origin,
            destination,
            route,
            vehicleType,
            vehicles[0],
            vehicles[1],
            vehicles[2],
            sourceTrip,
            targetTrip,
            secondTargetTrip,
            sourceCargo);
        if (releasedTargetCargo is not null)
        {
            db.Add(releasedTargetCargo);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(
            sourceTrip.Id,
            targetTrip.Id,
            secondTargetTrip.Id,
            parcelId,
            releasedTargetCargo?.Id);
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(
        Guid operatorId,
        Guid routeId,
        Guid vehicleId,
        DateTimeOffset departure,
        decimal maxCargoWeightKg,
        JsonElement seatLayoutSnapshotJson,
        TripSource source = TripSource.MANUAL) =>
        VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            routeId,
            vehicleId,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(4),
            source,
            Money.FromRaw(100_000),
            maxCargoWeightKg,
            10m,
            0m,
            seatLayoutSnapshotJson: seatLayoutSnapshotJson);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed record Seed(
        Guid SourceTripId,
        Guid TargetTripId,
        Guid SecondTargetTripId,
        Guid ParcelId,
        Guid? ReleasedTargetCargoId);
}
