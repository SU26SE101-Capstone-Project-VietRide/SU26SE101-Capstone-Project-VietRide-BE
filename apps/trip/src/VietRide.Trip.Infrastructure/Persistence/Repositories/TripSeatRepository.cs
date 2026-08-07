using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripSeatRepository : ITripSeatRepository
{
    private readonly TripDbContext _dbContext;

    public TripSeatRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.TripSeats.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<TripSeat> AddAsync(TripSeat entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.TripSeats.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(TripSeat entity) => _dbContext.TripSeats.Update(entity);

    public void Remove(TripSeat entity) => _dbContext.TripSeats.Remove(entity);

    public IQueryable<TripSeat> Query() => _dbContext.TripSeats;

    public IQueryable<TripSeat> QueryNoTracking() => _dbContext.TripSeats.AsNoTracking();

    public async Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for vehicle-swap seat acquisition.");
        }

        var seats = await _dbContext.TripSeats
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.trip_seats
                WHERE trip_id = {tripId}
                ORDER BY seat_number, id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
        foreach (var seat in seats)
        {
            await _dbContext.Entry(seat).ReloadAsync(cancellationToken);
        }

        return seats;
    }

    public Task<TripSeat?> AcquireForUpdateAsync(
        Guid tripId,
        string seatNumber,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for trip-seat locking.");
        }

        return _dbContext.TripSeats
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.trip_seats
                WHERE trip_id = {tripId}
                  AND upper(seat_number) = upper({seatNumber})
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
