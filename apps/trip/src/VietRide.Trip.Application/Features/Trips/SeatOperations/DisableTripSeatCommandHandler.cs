using MediatR;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

namespace VietRide.Trip.Application.Features.Trips.SeatOperations;

public sealed class DisableTripSeatCommandHandler : IRequestHandler<DisableTripSeatCommand, TripSeatMapDto>
{
    private readonly TripSeatMutationExecutor executor;

    public DisableTripSeatCommandHandler(TripSeatMutationExecutor executor) => this.executor = executor;

    public Task<TripSeatMapDto> Handle(DisableTripSeatCommand request, CancellationToken cancellationToken)
        => executor.DisableAsync(request, cancellationToken);
}
