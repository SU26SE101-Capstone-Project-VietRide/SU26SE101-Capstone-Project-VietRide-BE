using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

public sealed class LockSeatsHandler : IRequestHandler<LockSeatsCommand, LockSeatsResult>
{
    private static readonly TimeSpan PendingRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan PendingReplayTimeout = TimeSpan.FromSeconds(5);

    private readonly ISeatLockIdempotencyStore idempotencyStore;
    private readonly ISeatLockStore seatLockStore;
    private readonly ISeatLockTtlProvider seatLockTtlProvider;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IUnitOfWork unitOfWork;

    public LockSeatsHandler(
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ISeatLockStore seatLockStore,
        ISeatLockIdempotencyStore idempotencyStore,
        ISeatLockTtlProvider seatLockTtlProvider,
        IUnitOfWork unitOfWork)
    {
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.seatLockStore = seatLockStore;
        this.idempotencyStore = idempotencyStore;
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
        var ttl = request.TtlSeconds is { } ttlSeconds && ttlSeconds > 0
            ? TimeSpan.FromSeconds(ttlSeconds)
            : seatLockTtlProvider.DefaultTtl;
        var requestFingerprint = BuildRequestFingerprint(requestedSeats, request.HoldOwnerId, ttl);
        var idempotencyTtl = GetIdempotencyTtl(ttl);
        var reservation = await ReserveIdempotencyAsync(request, requestedSeats, requestFingerprint, idempotencyTtl, cancellationToken);
        if (reservation.Result is not null)
        {
            return reservation.Result;
        }

        var reservationToken = reservation.ReservationToken!;

        var seats = tripSeatRepository.Query()
            .Where(seat => seat.TripId == request.TripId && requestedSeats.Contains(seat.SeatNumber))
            .ToArray();
        await ReconcileExpiredHeldSeatsAsync(request.TripId, seats, cancellationToken);

        var unavailableSeats = requestedSeats
            .Where(seatNumber => seats.FirstOrDefault(seat => SeatEquals(seat.SeatNumber, seatNumber))?.Status != TripSeatStatus.AVAILABLE)
            .ToArray();
        if (unavailableSeats.Length > 0)
        {
            await RemoveReservationAsync(request.TripId, request.IdempotencyKey, reservationToken, cancellationToken);
            ThrowSeatUnavailable(unavailableSeats);
        }

        var seatLockToken = Guid.NewGuid();
        var lockOwner = seatLockToken.ToString("D");
        var acquiredAt = DateTimeOffset.UtcNow;
        var acquired = await seatLockStore.TryAcquireAsync(request.TripId, requestedSeats, lockOwner, ttl, cancellationToken);
        if (!acquired)
        {
            await RemoveReservationAsync(request.TripId, request.IdempotencyKey, reservationToken, cancellationToken);
            ThrowSeatUnavailable(requestedSeats);
        }

        var result = new LockSeatsResult(seatLockToken, requestedSeats, acquiredAt.Add(ttl));

        var releaseSeatLockOnFailure = true;
        var cleanupReservationOnFailure = true;
        try
        {
            foreach (var seat in seats)
            {
                seat.MarkHeld();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            var completed = await idempotencyStore.StoreCompletedAsync(
                request.TripId,
                request.IdempotencyKey,
                requestFingerprint,
                reservationToken,
                requestedSeats,
                result,
                idempotencyTtl,
                cancellationToken);
            if (!completed)
            {
                cleanupReservationOnFailure = false;
                foreach (var seat in seats.Where(seat => seat.Status == TripSeatStatus.HELD))
                {
                    seat.Release();
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                await seatLockStore.ReleaseAsync(request.TripId, requestedSeats, lockOwner, cancellationToken);
                releaseSeatLockOnFailure = false;
                throw new ConflictException("IDEMPOTENCY_REQUEST_PENDING", "A request with the same idempotency key is still being processed.");
            }
        }
        catch
        {
            if (cleanupReservationOnFailure)
            {
                await RemoveReservationAsync(request.TripId, request.IdempotencyKey, reservationToken, cancellationToken);
            }

            if (releaseSeatLockOnFailure)
            {
                await seatLockStore.ReleaseAsync(request.TripId, requestedSeats, lockOwner, cancellationToken);
            }

            throw;
        }

        return result;
    }

    private async Task<ReservationState> ReserveIdempotencyAsync(
        LockSeatsCommand request,
        IReadOnlyList<string> requestedSeats,
        string requestFingerprint,
        TimeSpan idempotencyTtl,
        CancellationToken cancellationToken)
    {
        var reservation = await idempotencyStore.TryReserveAsync(
            request.TripId,
            request.IdempotencyKey,
            requestFingerprint,
            requestedSeats,
            idempotencyTtl,
            cancellationToken);
        if (reservation.Reserved)
        {
            return new ReservationState(null, reservation.ReservationToken);
        }

        var timeoutAt = DateTimeOffset.UtcNow.Add(PendingReplayTimeout);
        var cached = reservation.ExistingEntry;
        while (true)
        {
            cached ??= await idempotencyStore.GetAsync(request.TripId, request.IdempotencyKey, cancellationToken);
            if (cached is null)
            {
                reservation = await idempotencyStore.TryReserveAsync(
                    request.TripId,
                    request.IdempotencyKey,
                    requestFingerprint,
                    requestedSeats,
                    idempotencyTtl,
                    cancellationToken);
                if (reservation.Reserved)
                {
                    return new ReservationState(null, reservation.ReservationToken);
                }
            }
            else if (cached.IsCompleted)
            {
                return new ReservationState(ReplayCachedResultOrThrow(cached, requestFingerprint), null);
            }
            else
            {
                ThrowIfFingerprintMismatch(cached, requestFingerprint);
            }

            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new ConflictException("IDEMPOTENCY_REQUEST_PENDING", "A request with the same idempotency key is still being processed.");
            }

            await Task.Delay(PendingRetryDelay, cancellationToken);
        }
    }

    private async Task RemoveReservationAsync(Guid tripId, string idempotencyKey, string reservationToken, CancellationToken cancellationToken) =>
        await idempotencyStore.RemoveReservationAsync(tripId, idempotencyKey, reservationToken, cancellationToken);

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

    private static LockSeatsResult ReplayCachedResultOrThrow(SeatLockIdempotencyEntry cached, string requestFingerprint)
    {
        ThrowIfFingerprintMismatch(cached, requestFingerprint);

        return cached.Result!;
    }

    private static void ThrowIfFingerprintMismatch(SeatLockIdempotencyEntry cached, string requestFingerprint)
    {
        if (!string.Equals(cached.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "The idempotency key was reused with a different lock-seats request.",
                [new ValidationError("Idempotency-Key", "The idempotency key was reused with a different lock-seats request.")]);
        }
    }

    private static TimeSpan GetIdempotencyTtl(TimeSpan seatTtl)
    {
        var minimumTtl = TimeSpan.FromMinutes(15);
        var pendingSafetyMargin = PendingReplayTimeout + TimeSpan.FromSeconds(1);
        var calculatedTtl = seatTtl + pendingSafetyMargin;
        return calculatedTtl > minimumTtl ? calculatedTtl : minimumTtl;
    }

    private static string BuildRequestFingerprint(IReadOnlyList<string> normalizedSeatNumbers, Guid holdOwnerId, TimeSpan ttl) =>
        $"seatNumbers={string.Join(',', normalizedSeatNumbers)};holdOwnerId={holdOwnerId:D};ttlSeconds={(long)ttl.TotalSeconds}";

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

    private sealed record ReservationState(LockSeatsResult? Result, string? ReservationToken);
}
