using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

public sealed class LockSeatsHandler : IRequestHandler<LockSeatsCommand, LockSeatsResult>
{
    private readonly ISeatLockStore seatLockStore;
    private readonly ISeatLockTtlProvider seatLockTtlProvider;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IUnitOfWork unitOfWork;

    public LockSeatsHandler(
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ISeatLockStore seatLockStore,
        ISeatLockTtlProvider seatLockTtlProvider,
        IUnitOfWork unitOfWork)
    {
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.seatLockStore = seatLockStore;
        this.seatLockTtlProvider = seatLockTtlProvider;
        this.unitOfWork = unitOfWork;
    }

    public async Task<LockSeatsResult> Handle(LockSeatsCommand request, CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (trip.Status != TripStatus.SCHEDULED)
        {
            throw new ConflictException("BOOKING_TRIP_NOT_BOOKABLE", "Trip is not bookable.");
        }

        var requestedSeats = Normalize(request.SeatNumbers);
        var seats = tripSeatRepository.Query()
            .Where(seat => seat.TripId == request.TripId && requestedSeats.Contains(seat.SeatNumber))
            .ToArray();
        await ReconcileExpiredHeldSeatsAsync(request.TripId, seats, cancellationToken);

        var unavailableSeats = requestedSeats
            .Where(seatNumber => seats.FirstOrDefault(seat => SeatEquals(seat.SeatNumber, seatNumber))?.Status != TripSeatStatus.AVAILABLE)
            .ToArray();
        if (unavailableSeats.Length > 0)
        {
            ThrowSeatUnavailable(unavailableSeats);
        }

        var seatLockToken = Guid.NewGuid();
        var lockOwner = seatLockToken.ToString("D");
        var ttl = request.TtlSeconds is { } ttlSeconds && ttlSeconds > 0
            ? TimeSpan.FromSeconds(ttlSeconds)
            : seatLockTtlProvider.DefaultTtl;
        var acquiredAt = DateTimeOffset.UtcNow;
        var acquired = await seatLockStore.TryAcquireAsync(request.TripId, requestedSeats, lockOwner, ttl, cancellationToken);
        if (!acquired)
        {
            ThrowSeatUnavailable(requestedSeats);
        }

        try
        {
            foreach (var seat in seats)
            {
                seat.MarkHeld();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await seatLockStore.ReleaseAsync(request.TripId, requestedSeats, lockOwner, cancellationToken);
            throw;
        }

        return new LockSeatsResult(seatLockToken, requestedSeats, acquiredAt.Add(ttl));
    }

    private async Task ReconcileExpiredHeldSeatsAsync(Guid tripId, IReadOnlyCollection<TripSeat> seats, CancellationToken cancellationToken)
    {
        var releasedAny = false;
        foreach (var seat in seats.Where(seat => seat.Status == TripSeatStatus.HELD))
        {
            if (!await seatLockStore.IsLockedAsync(tripId, seat.SeatNumber, cancellationToken))
            {
                seat.Release();
                releasedAny = true;
            }
        }

        if (releasedAny)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static string[] Normalize(IReadOnlyList<string> seatNumbers) => seatNumbers
        .Select(seatNumber => seatNumber.Trim())
        .Where(seatNumber => seatNumber.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool SeatEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void ThrowSeatUnavailable(IReadOnlyList<string> seatNumbers) =>
        throw new CodedConflictException(
            "BOOKING_SEAT_UNAVAILABLE",
            "One or more requested seats are unavailable.",
            seatNumbers.Select(seatNumber => new ValidationError("seatNumbers", seatNumber)).ToArray());
}
