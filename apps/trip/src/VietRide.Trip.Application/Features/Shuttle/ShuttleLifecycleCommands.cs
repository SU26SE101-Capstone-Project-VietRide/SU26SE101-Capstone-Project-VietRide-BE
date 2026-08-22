using MediatR;
using VietRide.Shared.Application.Behaviors;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

[SkipTransaction]
public sealed record MarkShuttleDeliveredCommand(Guid ShuttleTripId, int PickupOrder, Guid DriverUserId)
    : IRequest<ShuttleLifecycleResult>;

[SkipTransaction]
public sealed record MarkShuttleNoShowCommand(Guid ShuttleTripId, int PickupOrder, Guid DriverUserId, string Reason)
    : IRequest<ShuttleLifecycleResult>;

[SkipTransaction]
public sealed record StartShuttleTripCommand(Guid ShuttleTripId, Guid DriverUserId)
    : IRequest<ShuttleLifecycleResult>;

[SkipTransaction]
public sealed record CompleteShuttleTripCommand(Guid ShuttleTripId, Guid DriverUserId)
    : IRequest<ShuttleLifecycleResult>;

[SkipTransaction]
public sealed record CancelShuttleRequestCommand(
    Guid OperatorId,
    Guid MainTripId,
    Guid BookingId,
    string Direction,
    string Reason) : IRequest<ShuttleLifecycleResult>;

[SkipTransaction]
public sealed record CancelShuttleTripCommand(
    Guid OperatorId,
    Guid ShuttleTripId,
    Guid ActorUserId,
    string Reason)
    : IRequest<ShuttleLifecycleResult>;
