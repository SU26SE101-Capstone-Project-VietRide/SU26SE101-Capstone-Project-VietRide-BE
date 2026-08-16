using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips.StartTripBoarding;

[SkipTransaction]
public sealed record StartTripBoardingCommand(
    Guid TripId,
    Guid ActorUserId,
    string ActorRole,
    Guid? OperatorId) : IRequest<StartTripBoardingResponse>;
