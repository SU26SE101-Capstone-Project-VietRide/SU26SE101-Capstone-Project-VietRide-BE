using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.MarkBookingRefunded;

public sealed class MarkBookingRefundedCommandHandler
    : IRequestHandler<MarkBookingRefundedCommand, bool>
{
    private const string BookingRefundReferenceType = "BOOKING_REFUND";
    private const string EventType = "booking.booking.refunded";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<MarkBookingRefundedCommandHandler> _logger;

    public MarkBookingRefundedCommandHandler(
        IBookingRepository bookings,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<MarkBookingRefundedCommandHandler> logger)
    {
        _bookings = bookings;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(MarkBookingRefundedCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ReferenceType, BookingRefundReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var metadata = _bookings.QueryNoTracking()
            .Where(booking => booking.Id == request.ReferenceId && booking.Status == BookingStatus.CANCELLED)
            .Select(booking => new
            {
                BookingCode = booking.BookingCode.Value,
                TicketCodes = booking.Tickets
                    .Where(ticket => ticket.Status == TicketStatus.CANCELLED)
                    .OrderBy(ticket => ticket.SeatNumber)
                    .Select(ticket => ticket.TicketCode.Value)
                    .ToArray(),
            })
            .FirstOrDefault();

        var transitioned = await _bookings.TryMarkCancelledRefundedAsync(
            request.ReferenceId,
            _clock.UtcNow,
            cancellationToken);
        if (!transitioned)
        {
            _logger.LogInformation(
                "Wallet credited refund event ignored for booking {BookingId}; booking is not cancelled.",
                request.ReferenceId);
            return false;
        }

        var refundedEvent = new
        {
            bookingId = request.ReferenceId,
            bookingCode = metadata?.BookingCode,
            userId = request.UserId,
            amount = request.Amount,
            ticketCodes = metadata?.TicketCodes ?? [],
            ticketCount = metadata?.TicketCodes.Length ?? 0,
        };

        await _outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(refundedEvent, JsonOptions),
            cancellationToken);

        return true;
    }
}
