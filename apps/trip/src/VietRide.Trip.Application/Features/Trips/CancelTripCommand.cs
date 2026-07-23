using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips;

[SkipTransaction]
public sealed record CancelTripCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    string Reason) : IRequest<CancelTripResponse>;

public sealed record CancelTripResponse(Guid TripId, string Status);
