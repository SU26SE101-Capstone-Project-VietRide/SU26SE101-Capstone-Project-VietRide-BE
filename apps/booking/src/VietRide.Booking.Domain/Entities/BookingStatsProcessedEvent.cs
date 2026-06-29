namespace VietRide.Booking.Domain.Entities;

public sealed class BookingStatsProcessedEvent
{
    private BookingStatsProcessedEvent() { }

    public string EventType { get; private set; } = string.Empty;
    public Guid BookingId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static BookingStatsProcessedEvent Create(
        string eventType,
        Guid bookingId,
        DateTimeOffset processedAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));

        return new BookingStatsProcessedEvent
        {
            EventType = eventType.Trim(),
            BookingId = bookingId,
            ProcessedAt = processedAt,
        };
    }
}
