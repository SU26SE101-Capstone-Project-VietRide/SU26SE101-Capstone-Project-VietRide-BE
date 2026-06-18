using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.SeatLock;

public sealed class ExpiredSeatLockReleaser : IExpiredSeatLockReleaser
{
    private readonly ISeatLockStore seatLockStore;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IUnitOfWork unitOfWork;

    public ExpiredSeatLockReleaser(
        ITripSeatRepository tripSeatRepository,
        ISeatLockStore seatLockStore,
        IUnitOfWork unitOfWork)
    {
        this.tripSeatRepository = tripSeatRepository;
        this.seatLockStore = seatLockStore;
        this.unitOfWork = unitOfWork;
    }

    public async Task<int> ReleaseExpiredAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var limit = batchSize <= 0 ? 500 : batchSize;
        var heldSeats = tripSeatRepository.Query()
            .Where(seat => seat.Status == TripSeatStatus.HELD)
            .OrderBy(seat => seat.UpdatedAt)
            .Take(limit)
            .ToArray();
        var released = 0;

        foreach (var seat in heldSeats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await seatLockStore.IsLockedAsync(seat.TripId, seat.SeatNumber, cancellationToken))
            {
                continue;
            }

            seat.Release();
            released++;
        }

        if (released > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return released;
    }
}
