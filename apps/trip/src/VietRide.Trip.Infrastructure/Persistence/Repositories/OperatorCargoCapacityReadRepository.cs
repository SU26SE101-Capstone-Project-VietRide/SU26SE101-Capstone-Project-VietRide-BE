using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorCargoCapacityReadRepository : IOperatorCargoCapacityReadRepository
{
    private readonly TripDbContext dbContext;

    public OperatorCargoCapacityReadRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<OperatorCargoCapacityReadModel?> GetAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
        => dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Id == tripId)
            .Select(trip => new OperatorCargoCapacityReadModel(
                trip.Id,
                trip.OperatorId,
                trip.ReservedParcelWeightKg,
                trip.ReservedParcelVolumeM3,
                trip.TotalLoadedWeightKg,
                trip.TotalLoadedVolumeM3,
                trip.MaxCargoWeightKg ?? 0m,
                trip.MaxCargoVolumeM3 ?? 0m,
                dbContext.TripCargoParcels
                    .Where(cargo => cargo.TripId == trip.Id && cargo.LoadedAt != null)
                    .Sum(cargo => (decimal?)cargo.WeightKg) ?? 0m,
                dbContext.TripCargoParcels
                    .Where(cargo => cargo.TripId == trip.Id && cargo.LoadedAt != null)
                    .Sum(cargo => (decimal?)cargo.VolumeM3) ?? 0m))
            .SingleOrDefaultAsync(cancellationToken);
}
