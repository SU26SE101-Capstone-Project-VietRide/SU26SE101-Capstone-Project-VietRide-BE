using MediatR;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.PendingActions;

[SkipTransaction]
public sealed record ResolveRouteChangePendingActionCommand(
    Guid BookingId,
    Guid ActionId,
    Guid PassengerUserId,
    string IdempotencyKey,
    string? Action,
    Guid? SelectedStopId,
    Guid? SelectedStationId,
    string? Note,
    IReadOnlyCollection<string> ExtraFields) : IRequest<ResolvePendingActionResult>;
