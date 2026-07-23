using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;

public sealed class HandleTripCancelledCommandHandler(
    IBookingRepository bookings,
    IBookingStatusHistoryRepository statusHistory,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<HandleTripCancelledCommand, int>
{
    public const string DriverScheduleDayRemovedReason = "DRIVER_SCHEDULE_DAY_REMOVED";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> Handle(
        HandleTripCancelledCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await bookings.AcquireEventLockAsync(request.EventId, cancellationToken);
            var eligible = await bookings.GetCancellableByTripAsync(
                request.TripId,
                request.OperatorId,
                cancellationToken);

            foreach (var booking in eligible)
            {
                var refundAmount = booking.Status == BookingStatus.CONFIRMED
                    ? booking.TotalAmount.Amount
                    : 0L;
                booking.Cancel(
                    BookingCancellationReason.OPERATOR_CANCELLED_TRIP,
                    request.CancelledAt,
                    refundOverride: true);
                bookings.Update(booking);

                await statusHistory.AddAsync(
                    BookingStatusHistory.Create(
                        booking.Id,
                        BookingStatus.CANCELLED,
                        request.CancelledAt,
                        BookingStatusHistorySource.CancelBooking,
                        request.OperatorId,
                        BookingCancellationReason.OPERATOR_CANCELLED_TRIP.ToString()),
                    cancellationToken);

                var eventId = Guid.NewGuid();
                var occurredAt = clock.UtcNow;
                var cancelled = new BookingCancelledIntegrationEvent(
                    eventId,
                    occurredAt,
                    booking.Id,
                    booking.BookingCode.Value,
                    booking.PassengerUserId,
                    refundAmount,
                    true,
                    BookingCancellationReason.OPERATOR_CANCELLED_TRIP.ToString(),
                    booking.Tickets.Select(ticket => ticket.TicketCode.Value).Order(StringComparer.Ordinal).ToArray(),
                    booking.Tickets.Count);
                await outbox.EnqueueAsync(
                    eventId,
                    BookingCancelledIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(cancelled, JsonOptions),
                    cancellationToken);
            }

            return eligible.Count;
        }, cancellationToken);
    }

    private static void Validate(HandleTripCancelledCommand request)
    {
        if (request.EventId == Guid.Empty || request.TripId == Guid.Empty || request.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip-cancelled event, Trip, and Operator ids must be non-empty.");
        }

        if (request.OccurredAt == default
            || request.CancelledAt == default
            || request.CancelledAt != request.OccurredAt)
        {
            throw new ArgumentException("Trip-cancelled timestamps are invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.CancelReason))
        {
            throw new ArgumentException("Trip cancellation reason is required.");
        }
    }
}
