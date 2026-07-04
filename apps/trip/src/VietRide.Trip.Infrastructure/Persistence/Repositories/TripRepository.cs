using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripRepository : ITripRepository
{
    private readonly TripDbContext _dbContext;

    public TripRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Trips.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<Domain.Entities.Trip> AddAsync(Domain.Entities.Trip entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.Trips.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(Domain.Entities.Trip entity) => _dbContext.Trips.Update(entity);

    public void Remove(Domain.Entities.Trip entity) => _dbContext.Trips.Remove(entity);

    public IQueryable<Domain.Entities.Trip> Query() => _dbContext.Trips;

    public IQueryable<Domain.Entities.Trip> QueryNoTracking() => _dbContext.Trips.AsNoTracking();

    public Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
        _dbContext.Trips
            .Include(trip => trip.Seats)
            .FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);

    public async Task<TripCargoMutationResult?> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trip = await GetByIdAsync(tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var existing = await _dbContext.TripCargoParcels
            .FirstOrDefaultAsync(cargo => cargo.TripId == tripId && cargo.ParcelId == parcelId, cancellationToken);
        if (existing is not null)
        {
            return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
        }

        if (weightKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, "Cargo weight must be positive.");
        }

        var reserved = trip.ReservedParcelWeightKg + weightKg;
        if (trip.MaxCargoWeightKg.HasValue && reserved + trip.TotalLoadedWeightKg > trip.MaxCargoWeightKg.Value)
        {
            throw new InvalidOperationException("Trip cargo capacity would be exceeded.");
        }

        await _dbContext.TripCargoParcels.AddAsync(TripCargoParcel.Reserve(tripId, parcelId, weightKg), cancellationToken);
        trip.UpdateCargoCounters(reserved, trip.TotalLoadedWeightKg);
        trip.UpdatedAt = now;

        return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
    }

    public async Task<TripCargoMutationResult?> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trip = await GetByIdAsync(tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var wasNearFull = IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg);
        var cargo = await _dbContext.TripCargoParcels
            .FirstOrDefaultAsync(c => c.TripId == tripId && c.ParcelId == parcelId, cancellationToken);

        if (cargo is null)
        {
            cargo = TripCargoParcel.Reserve(tripId, parcelId, weightKg);
            await _dbContext.TripCargoParcels.AddAsync(cargo, cancellationToken);
            trip.UpdateCargoCounters(trip.ReservedParcelWeightKg + cargo.WeightKg, trip.TotalLoadedWeightKg);
        }

        if (cargo.State == TripCargoParcel.LoadedState)
        {
            return BuildCargoResult(trip, wasNearFull);
        }

        if (trip.MaxCargoWeightKg.HasValue && trip.TotalLoadedWeightKg + cargo.WeightKg > trip.MaxCargoWeightKg.Value)
        {
            throw new InvalidOperationException("Trip cargo capacity would be exceeded.");
        }

        cargo.MarkLoaded(now);
        trip.UpdateCargoCounters(
            Math.Max(0m, trip.ReservedParcelWeightKg - cargo.WeightKg),
            trip.TotalLoadedWeightKg + cargo.WeightKg);
        trip.UpdatedAt = now;

        return BuildCargoResult(trip, wasNearFull);
    }

    public async Task<TripCargoMutationResult?> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trip = await GetByIdAsync(tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var wasNearFull = IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg);
        var cargo = await _dbContext.TripCargoParcels
            .FirstOrDefaultAsync(c => c.TripId == tripId && c.ParcelId == parcelId, cancellationToken);
        if (cargo is null || cargo.State == TripCargoParcel.ReleasedState)
        {
            return BuildCargoResult(trip, wasNearFull);
        }

        var previousState = cargo.Release(now);
        var reserved = previousState == TripCargoParcel.ReservedState
            ? Math.Max(0m, trip.ReservedParcelWeightKg - cargo.WeightKg)
            : trip.ReservedParcelWeightKg;
        var loaded = previousState == TripCargoParcel.LoadedState
            ? Math.Max(0m, trip.TotalLoadedWeightKg - cargo.WeightKg)
            : trip.TotalLoadedWeightKg;

        trip.UpdateCargoCounters(reserved, loaded);
        trip.UpdatedAt = now;

        return BuildCargoResult(trip, wasNearFull);
    }

    private static TripCargoMutationResult BuildCargoResult(Domain.Entities.Trip trip, bool wasNearFullBefore)
    {
        var max = trip.MaxCargoWeightKg ?? 0m;
        var percentFull = max <= 0m ? 0m : Math.Round(trip.TotalLoadedWeightKg / max * 100m, 2);
        return new TripCargoMutationResult(
            trip.Id,
            trip.ReservedParcelWeightKg,
            trip.TotalLoadedWeightKg,
            max,
            percentFull,
            !wasNearFullBefore && IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
    }

    private static bool IsNearFull(decimal loadedWeightKg, decimal? maxCargoWeightKg)
        => maxCargoWeightKg is > 0m && loadedWeightKg >= maxCargoWeightKg.Value * 0.8m;
}
