using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class CreateShuttleTripCommandHandler : IRequestHandler<CreateShuttleTripCommand, CreateShuttleTripResult>
{
    private readonly IShuttleDispatchService _service;

    public CreateShuttleTripCommandHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<CreateShuttleTripResult> Handle(CreateShuttleTripCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreateShuttleTripInput(
            request.OperatorId,
            request.MainTripId,
            request.DriverUserId,
            request.VehicleId,
            request.ScheduledDepartureTime,
            request.ScheduledEndTime,
            request.OrderedBookingIds,
            request.Notes), cancellationToken);
}
