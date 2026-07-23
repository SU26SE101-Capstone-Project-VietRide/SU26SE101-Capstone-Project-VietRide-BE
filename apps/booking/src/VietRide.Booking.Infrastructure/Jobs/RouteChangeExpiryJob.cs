using System.Text.Json;
using Hangfire;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class RouteChangeExpiryJob(
    IBookingPendingActionRepository pendingActions,
    IBookingRepository bookings,
    IBookingStatusHistoryRepository statusHistory,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(Guid pendingActionId, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var action = await pendingActions.GetByIdForUpdateAsync(pendingActionId, cancellationToken);
            if (action is null
                || action.Reason != BookingPendingActionReason.ROUTE_CHANGE
                || action.ResolvedAt.HasValue
                || action.Deadline >= clock.UtcNow)
            {
                return false;
            }

            var booking = await bookings.FindByIdForUpdateAsync(action.BookingId, cancellationToken);
            if (booking is null || booking.Status != BookingStatus.CONFIRMED)
            {
                return false;
            }

            var now = clock.UtcNow;
            action.ExpireRouteChange(now);
            pendingActions.Update(action);
            booking.Cancel(BookingCancellationReason.ROUTE_CHANGED_REFUSED, now, refundOverride: true);
            bookings.Update(booking);
            await statusHistory.AddAsync(BookingStatusHistory.Create(
                booking.Id,
                BookingStatus.CANCELLED,
                now,
                BookingStatusHistorySource.CancelBooking,
                actorUserId: null,
                BookingCancellationReason.ROUTE_CHANGED_REFUSED.ToString()), cancellationToken);

            var eventId = Guid.NewGuid();
            var cancelled = new BookingCancelledIntegrationEvent(
                eventId,
                now,
                booking.Id,
                booking.BookingCode.Value,
                booking.PassengerUserId,
                booking.TotalAmount.Amount,
                true,
                BookingCancellationReason.ROUTE_CHANGED_REFUSED.ToString(),
                booking.Tickets.Select(ticket => ticket.TicketCode.Value).Order(StringComparer.Ordinal).ToArray(),
                booking.Tickets.Count);
            await outbox.EnqueueAsync(
                eventId,
                BookingCancelledIntegrationEvent.EventTypeValue,
                JsonSerializer.Serialize(cancelled, JsonOptions),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
