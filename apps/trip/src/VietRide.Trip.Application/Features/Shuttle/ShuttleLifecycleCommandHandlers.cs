using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class MarkShuttleDeliveredCommandHandler : IRequestHandler<MarkShuttleDeliveredCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public MarkShuttleDeliveredCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(MarkShuttleDeliveredCommand request, CancellationToken cancellationToken)
        => _service.MarkDeliveredAsync(request.ShuttleTripId, request.PickupOrder, request.DriverUserId, cancellationToken);
}

public sealed class MarkShuttleNoShowCommandHandler : IRequestHandler<MarkShuttleNoShowCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public MarkShuttleNoShowCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(MarkShuttleNoShowCommand request, CancellationToken cancellationToken)
        => _service.MarkNoShowAsync(request.ShuttleTripId, request.PickupOrder, request.DriverUserId, request.Reason, cancellationToken);
}

public sealed class StartShuttleTripCommandHandler : IRequestHandler<StartShuttleTripCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public StartShuttleTripCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(StartShuttleTripCommand request, CancellationToken cancellationToken)
        => _service.StartAsync(request.ShuttleTripId, request.DriverUserId, cancellationToken);
}

public sealed class CompleteShuttleTripCommandHandler : IRequestHandler<CompleteShuttleTripCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public CompleteShuttleTripCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(CompleteShuttleTripCommand request, CancellationToken cancellationToken)
        => _service.CompleteAsync(request.ShuttleTripId, request.DriverUserId, cancellationToken);
}

public sealed class CancelShuttleRequestCommandHandler : IRequestHandler<CancelShuttleRequestCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public CancelShuttleRequestCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(CancelShuttleRequestCommand request, CancellationToken cancellationToken)
        => _service.CancelRequestAsync(request.OperatorId, request.MainTripId, request.BookingId, request.Direction, request.Reason, cancellationToken);
}

public sealed class CancelShuttleTripCommandHandler : IRequestHandler<CancelShuttleTripCommand, ShuttleLifecycleResult>
{
    private readonly IShuttleDispatchService _service;
    public CancelShuttleTripCommandHandler(IShuttleDispatchService service) => _service = service;
    public Task<ShuttleLifecycleResult> Handle(CancelShuttleTripCommand request, CancellationToken cancellationToken)
        => _service.CancelShuttleTripAsync(request.OperatorId, request.ShuttleTripId, request.Reason, cancellationToken);
}
