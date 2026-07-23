using MediatR;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

namespace VietRide.Booking.Application.Features.PendingActions;

public sealed class ResolveRouteChangePendingActionCommandHandler(ISender sender)
    : IRequestHandler<ResolveRouteChangePendingActionCommand, ResolvePendingActionResult>
{
    public Task<ResolvePendingActionResult> Handle(
        ResolveRouteChangePendingActionCommand request,
        CancellationToken cancellationToken)
        => sender.Send(new ResolvePendingActionCommand(
            request.BookingId,
            request.ActionId,
            request.PassengerUserId,
            request.IdempotencyKey,
            request.Action,
            request.Note,
            request.ExtraFields,
            request.SelectedStopId,
            request.SelectedStationId), cancellationToken);
}
