using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class OperatorCargoCapacityReadRepositoryTests
{
    [Fact]
    public async Task GetAsync_SumsEveryPreviouslyLoadedLedgerAndExcludesReservedOnlyCargo()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var loadedAt = DateTimeOffset.Parse("2026-08-31T03:00:00Z");

            var releasedCargo = TripCargoParcel.Reserve(seed.TripId, Guid.NewGuid(), 12.5m, 0.2m);
            releasedCargo.MarkLoaded(loadedAt);
            releasedCargo.Release(loadedAt.AddMinutes(30));

            var activeCargo = TripCargoParcel.Reserve(seed.TripId, Guid.NewGuid(), 3m, 0.05m);
            activeCargo.MarkLoaded(loadedAt.AddMinutes(5));

            var reservedOnlyCargo = TripCargoParcel.Reserve(seed.TripId, Guid.NewGuid(), 7m, 0.1m);

            db.TripCargoParcels.AddRange(releasedCargo, activeCargo, reservedOnlyCargo);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await CreateRepository(db).GetAsync(seed.TripId, CancellationToken.None);

            result.Should().NotBeNull();
            result!.HistoricalLoadedWeightKg.Should().Be(15.5m);
            result.HistoricalLoadedVolumeM3.Should().Be(0.25m);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static IOperatorCargoCapacityReadRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.OperatorCargoCapacityReadRepository",
            throwOnError: true)!;
        return (IOperatorCargoCapacityReadRepository)Activator.CreateInstance(type, db)!;
    }
}
