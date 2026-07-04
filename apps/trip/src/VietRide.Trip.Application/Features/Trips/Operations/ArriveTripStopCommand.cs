using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips.Operations;

[SkipTransaction]
public sealed record ArriveTripStopCommand(
    Guid TripId,
    Guid StopId,
    Guid OperatorId,
    Guid ActorUserId) : IRequest<ArriveTripStopResponse>;
