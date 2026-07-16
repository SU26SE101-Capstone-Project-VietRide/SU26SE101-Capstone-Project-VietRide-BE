using MediatR;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record ArriveTripDestinationCommand(
    Guid TripId,
    Guid ActorUserId) : IRequest<ArriveTripDestinationResponse>;
