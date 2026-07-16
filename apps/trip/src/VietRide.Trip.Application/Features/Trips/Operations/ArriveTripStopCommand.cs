using MediatR;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record ArriveTripStopCommand(
    Guid TripId,
    Guid StopId,
    Guid ActorUserId) : IRequest<ArriveTripStopResponse>;
