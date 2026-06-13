using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

public sealed record LockSeatsCommand(
    Guid TripId,
    IReadOnlyList<string> SeatNumbers,
    Guid HoldOwnerId,
    int? TtlSeconds)
    : IRequest<LockSeatsResult>;
