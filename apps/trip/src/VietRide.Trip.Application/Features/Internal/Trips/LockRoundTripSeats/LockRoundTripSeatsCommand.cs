using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;

public sealed record LockRoundTripSeatsCommand(
    LockRoundTripSeatsLegRequest Outbound,
    LockRoundTripSeatsLegRequest Return,
    Guid HoldOwnerId,
    int? TtlSeconds,
    string IdempotencyKey) : IRequest<LockRoundTripSeatsResult>;

public sealed record LockRoundTripSeatsLegRequest(Guid TripId, IReadOnlyList<string> SeatNumbers);
