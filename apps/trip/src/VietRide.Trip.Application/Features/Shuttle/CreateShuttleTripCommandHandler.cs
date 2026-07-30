using MediatR;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class CreateShuttleTripCommandHandler : IRequestHandler<CreateShuttleTripCommand, CreateShuttleTripResult>
{
    private readonly IIdentityInternalClient _identityInternalClient;
    private readonly IShuttleDispatchService _service;

    public CreateShuttleTripCommandHandler(
        IIdentityInternalClient identityInternalClient,
        IShuttleDispatchService service)
    {
        _identityInternalClient = identityInternalClient;
        _service = service;
    }

    public async Task<CreateShuttleTripResult> Handle(
        CreateShuttleTripCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identityInternalClient,
            request.OperatorId,
            cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            _identityInternalClient,
            request.OperatorId,
            requireShuttleModule: true,
            cancellationToken);

        return await _service.CreateAsync(new CreateShuttleTripInput(
            request.OperatorId,
            request.MainTripId,
            request.DriverUserId,
            request.VehicleId,
            request.ScheduledDepartureTime,
            request.ScheduledEndTime,
            request.OrderedBookingIds,
            request.Notes), cancellationToken);
    }
}
