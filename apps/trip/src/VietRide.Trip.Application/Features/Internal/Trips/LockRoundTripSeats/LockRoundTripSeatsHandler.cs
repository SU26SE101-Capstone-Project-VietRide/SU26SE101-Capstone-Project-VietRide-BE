using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;

public sealed class LockRoundTripSeatsHandler : IRequestHandler<LockRoundTripSeatsCommand, LockRoundTripSeatsResult>
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromMinutes(30);

    private readonly ITripRepository tripRepository;
    private readonly IRoundTripSeatLockStore seatLockStore;
    private readonly IUnitOfWork unitOfWork;

    public LockRoundTripSeatsHandler(
        ITripRepository tripRepository,
        IRoundTripSeatLockStore seatLockStore,
        IUnitOfWork unitOfWork)
    {
        this.tripRepository = tripRepository;
        this.seatLockStore = seatLockStore;
        this.unitOfWork = unitOfWork;
    }

    public async Task<LockRoundTripSeatsResult> Handle(
        LockRoundTripSeatsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Outbound.TripId == request.Return.TripId)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Outbound and return trip ids must be different.",
                [new ValidationError("return.tripId", "Return trip id must be different from outbound trip id.")]);
        }

        var outboundSeats = NormalizeSeatNumbers(request.Outbound.SeatNumbers);
        var returnSeats = NormalizeSeatNumbers(request.Return.SeatNumbers);
        var ttl = ResolveTtl(request.TtlSeconds);
        var outboundToken = Guid.NewGuid();
        var returnToken = Guid.NewGuid();

        var outboundTrip = await GetBookableTripAsync(request.Outbound.TripId, cancellationToken);
        var returnTrip = await GetBookableTripAsync(request.Return.TripId, cancellationToken);

        var storeResult = await seatLockStore.TryLockAsync(
            new RoundTripSeatLockStoreRequest(
                new RoundTripSeatLockLeg(request.Outbound.TripId, outboundSeats, outboundToken),
                new RoundTripSeatLockLeg(request.Return.TripId, returnSeats, returnToken),
                request.HoldOwnerId,
                request.IdempotencyKey,
                ttl),
            cancellationToken);

        if (storeResult.IsReplay)
        {
            if (storeResult.Replay is null)
            {
                throw new InvalidOperationException("Invalid Redis idempotency replay payload.");
            }

            return new LockRoundTripSeatsResult(
                ToResult(storeResult.Replay.Outbound),
                ToResult(storeResult.Replay.Return));
        }

        if (!storeResult.Succeeded)
        {
            throw SeatUnavailable(storeResult.UnavailableSeats);
        }

        var keys = BuildKeys(request.Outbound.TripId, outboundSeats, request.Return.TripId, returnSeats);
        var locksAcquired = true;
        try
        {
            var unavailableSeats = outboundTrip.FindUnavailableSeats(outboundSeats)
                .Concat(returnTrip.FindUnavailableSeats(returnSeats))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unavailableSeats.Length > 0)
            {
                throw SeatUnavailable(unavailableSeats);
            }

            outboundTrip.HoldSeats(outboundSeats);
            returnTrip.HoldSeats(returnSeats);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            locksAcquired = false;
        }
        catch
        {
            if (locksAcquired)
            {
                await seatLockStore.ReleaseAsync(keys, request.IdempotencyKey, CancellationToken.None);
            }

            throw;
        }

        if (storeResult.Replay is not null)
        {
            return new LockRoundTripSeatsResult(
                ToResult(storeResult.Replay.Outbound),
                ToResult(storeResult.Replay.Return));
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        return new LockRoundTripSeatsResult(
            new LockRoundTripSeatsLegResult(request.Outbound.TripId, outboundToken, outboundSeats, expiresAt),
            new LockRoundTripSeatsLegResult(request.Return.TripId, returnToken, returnSeats, expiresAt));
    }

    private async Task<Domain.Entities.Trip> GetBookableTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetWithSeatsAsync(tripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        }

        if (!trip.IsBookable())
        {
            throw new ConflictException("BOOKING_TRIP_NOT_BOOKABLE", "Trip is not bookable.");
        }

        return trip;
    }

    private static IReadOnlyList<string> NormalizeSeatNumbers(IReadOnlyList<string> seatNumbers)
        => seatNumbers
            .Where(seatNumber => !string.IsNullOrWhiteSpace(seatNumber))
            .Select(seatNumber => seatNumber.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static TimeSpan ResolveTtl(int? ttlSeconds)
    {
        if (!ttlSeconds.HasValue || ttlSeconds.Value <= 0)
        {
            return DefaultTtl;
        }

        var requested = TimeSpan.FromSeconds(ttlSeconds.Value);
        return requested > MaxTtl ? MaxTtl : requested;
    }

    private static IReadOnlyList<RoundTripSeatLockKey> BuildKeys(
        Guid outboundTripId,
        IReadOnlyList<string> outboundSeats,
        Guid returnTripId,
        IReadOnlyList<string> returnSeats)
        => outboundSeats.Select(seatNumber => new RoundTripSeatLockKey(outboundTripId, seatNumber))
            .Concat(returnSeats.Select(seatNumber => new RoundTripSeatLockKey(returnTripId, seatNumber)))
            .ToArray();

    private static LockRoundTripSeatsLegResult ToResult(RoundTripSeatLockReplayLeg leg)
        => new(leg.TripId, leg.SeatLockToken, leg.LockedSeats, leg.ExpiresAt);

    private static ConflictException SeatUnavailable(IReadOnlyList<string> seatNumbers)
        => new(
            "BOOKING_SEAT_UNAVAILABLE",
            "One or more requested seats are unavailable.",
            seatNumbers.Select(seatNumber => new ValidationError("seatNumbers", seatNumber)).ToArray());
}
