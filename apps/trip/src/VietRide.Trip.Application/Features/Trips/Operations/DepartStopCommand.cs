using MediatR;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record DepartStopCommand(
    Guid TripId,
    Guid StopId,
    Guid ActorUserId,
    string ActorRole,
    Guid OperatorId) : IRequest<DepartStopResponse>;
