using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Domain.Entities;

public sealed class BookingStatusHistory
{
    private BookingStatusHistory() { }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? ReasonCode { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Source { get; private set; } = string.Empty;

    public static BookingStatusHistory Create(
        Guid bookingId,
        BookingStatus status,
        DateTimeOffset occurredAt,
        string source,
        Guid? actorUserId = null,
        string? reasonCode = null)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));
        if (string.IsNullOrWhiteSpace(source) || !BookingStatusHistorySource.IsDefined(source))
            throw new ArgumentException("Source must be one of the reviewed booking lifecycle sources.", nameof(source));
        if (reasonCode?.Length > 100)
            throw new ArgumentException("Reason code cannot exceed 100 characters.", nameof(reasonCode));

        return new BookingStatusHistory
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            Status = status,
            OccurredAt = occurredAt,
            Source = source,
            ActorUserId = actorUserId,
            ReasonCode = reasonCode,
        };
    }
}
