namespace VietRide.Booking.Application.Events;

/// <summary>Existing Booking cancellation payload consumed by Payment and Notification.</summary>
public sealed record BookingCancelledIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid BookingId,
    string BookingCode,
    Guid UserId,
    long RefundAmount,
    bool RefundOverride,
    string CancellationReason,
    IReadOnlyCollection<string> TicketCodes,
    int TicketCount)
{
    public const string EventTypeValue = "booking.booking.cancelled";
}
