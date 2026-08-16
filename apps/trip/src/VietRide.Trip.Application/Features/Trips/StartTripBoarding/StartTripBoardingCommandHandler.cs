using MediatR;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Trips.StartTripBoarding;

public sealed class StartTripBoardingCommandHandler
    : IRequestHandler<StartTripBoardingCommand, StartTripBoardingResponse>
{
    private readonly ITripBoardingTransitionCoordinator coordinator;
    private readonly IClock clock;

    public StartTripBoardingCommandHandler(
        ITripBoardingTransitionCoordinator coordinator,
        IClock clock)
    {
        this.coordinator = coordinator;
        this.clock = clock;
    }

    public async Task<StartTripBoardingResponse> Handle(
        StartTripBoardingCommand request,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.StartManualAsync(
            request.TripId,
            request.ActorUserId,
            request.ActorRole,
            request.OperatorId,
            clock.UtcNow,
            cancellationToken);
        return new StartTripBoardingResponse(result.TripId, result.Status);
    }
}
