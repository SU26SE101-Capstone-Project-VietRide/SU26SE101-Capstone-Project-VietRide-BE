using MediatR;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

namespace VietRide.Trip.Application.Features.Trips.SeatOperations;

public sealed class EnableTripSeatCommandHandler : IRequestHandler<EnableTripSeatCommand, TripSeatMapDto>
{
    private readonly TripSeatMutationExecutor executor;

    public EnableTripSeatCommandHandler(TripSeatMutationExecutor executor) => this.executor = executor;

    public Task<TripSeatMapDto> Handle(EnableTripSeatCommand request, CancellationToken cancellationToken)
        => executor.EnableAsync(request, cancellationToken);
}
