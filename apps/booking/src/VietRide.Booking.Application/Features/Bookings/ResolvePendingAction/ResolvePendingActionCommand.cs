using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

[SkipTransaction]
public sealed record ResolvePendingActionCommand(
    Guid BookingId,
    Guid ActionId,
    Guid PassengerUserId,
    string IdempotencyKey,
    string? Action,
    string? Note,
    IReadOnlyCollection<string> ExtraFields,
    Guid? SelectedStopId = null,
    Guid? SelectedStationId = null) : IRequest<ResolvePendingActionResult>;
