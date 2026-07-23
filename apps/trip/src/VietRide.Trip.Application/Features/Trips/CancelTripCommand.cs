using MediatR;

namespace VietRide.Trip.Application.Features.Trips;

public sealed record CancelTripCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    string Reason) : IRequest<CancelTripResponse>;

public sealed record CancelTripResponse(Guid TripId, string Status);
