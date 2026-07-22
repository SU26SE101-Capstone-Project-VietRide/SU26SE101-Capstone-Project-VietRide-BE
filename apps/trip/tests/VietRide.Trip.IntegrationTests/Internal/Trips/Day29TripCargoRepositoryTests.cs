using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class Day29TripCargoRepositoryTests
{
    [Fact]
    public async Task RepeatedLoadForSameTripParcel_IsBehaviorIdempotentWithoutCounterLedgerDrift()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var handler = Day29CargoNearFullOutboxIntegrationTests.CreateHandler(
                db,
                new IntegrationEventOutbox(new OutboxStore(db, new Day29CargoNearFullOutboxIntegrationTests.FixedClock())));
            var parcelId = Guid.NewGuid();
            var command = Day29CargoNearFullOutboxIntegrationTests.CreateLoad(seed.TripId, parcelId, 80m);

            var first = await handler.Handle(command, CancellationToken.None);
            var replay = await handler.Handle(command, CancellationToken.None);

            first.LoadedWeightKg.Should().Be(80m);
            replay.LoadedWeightKg.Should().Be(80m);
            await using var assertionDb = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            var trip = await assertionDb.Trips.AsNoTracking().SingleAsync(item => item.Id == seed.TripId);
            var ledger = await assertionDb.TripCargoParcels.AsNoTracking()
                .Where(item => item.TripId == seed.TripId && item.ParcelId == parcelId)
                .ToArrayAsync();
            var thresholdEvents = await assertionDb.OutboxEvents.AsNoTracking()
                .Where(item => item.EventType == CargoThresholdCrossedIntegrationEvent.EventTypeValue)
                .ToArrayAsync();

            trip.TotalLoadedWeightKg.Should().Be(80m);
            trip.ReservedParcelWeightKg.Should().Be(0m);
            ledger.Should().ContainSingle();
            ledger[0].State.Should().Be(TripCargoParcel.LoadedState);
            ledger[0].WeightKg.Should().Be(80m);
            thresholdEvents.Should().ContainSingle();
            thresholdEvents[0].Status.Should().Be(OutboxEventStatus.PENDING);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }
}
