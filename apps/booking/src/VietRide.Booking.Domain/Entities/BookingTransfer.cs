using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// Immutable seat-history record for one Passenger moved between a specific Trip pair.
/// Trip and Identity identifiers are logical cross-service references.
/// </summary>
public sealed class BookingTransfer
{
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid PassengerId { get; private set; }
    public Guid? TicketId { get; private set; }
    public Guid OriginalTripId { get; private set; }
    public Guid NewTripId { get; private set; }
    public string? OriginalSeatNumber { get; private set; }
    public string? NewSeatNumber { get; private set; }
    public BookingTransferConfirmationStatus ConfirmationStatus { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public DateTimeOffset TransferredAt { get; private set; }
    public Guid TransferredByUserId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private BookingTransfer() { }

    public static BookingTransfer Create(
        Guid bookingId,
        Guid passengerId,
        Guid? ticketId,
        Guid originalTripId,
        Guid newTripId,
        string? originalSeatNumber,
        string? newSeatNumber,
        BookingTransferConfirmationStatus confirmationStatus,
        DateTimeOffset transferredAt,
        Guid transferredByUserId,
        string? note = null)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));
        if (passengerId == Guid.Empty)
            throw new ArgumentException("Passenger id is required.", nameof(passengerId));
        if (ticketId == Guid.Empty)
            throw new ArgumentException("Ticket id must be null or non-empty.", nameof(ticketId));
        if (originalTripId == Guid.Empty)
            throw new ArgumentException("Original Trip id is required.", nameof(originalTripId));
        if (newTripId == Guid.Empty)
            throw new ArgumentException("New Trip id is required.", nameof(newTripId));
        if (transferredByUserId == Guid.Empty)
            throw new ArgumentException("Transferred-by user id is required.", nameof(transferredByUserId));
        if (confirmationStatus == BookingTransferConfirmationStatus.CONFIRMED)
            throw new ArgumentException("A transfer must be confirmed through Confirm().", nameof(confirmationStatus));

        return new BookingTransfer
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            PassengerId = passengerId,
            TicketId = ticketId,
            OriginalTripId = originalTripId,
            NewTripId = newTripId,
            OriginalSeatNumber = NormalizeSeat(originalSeatNumber),
            NewSeatNumber = NormalizeSeat(newSeatNumber),
            ConfirmationStatus = confirmationStatus,
            TransferredAt = transferredAt,
            TransferredByUserId = transferredByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = transferredAt,
        };
    }

    public void Confirm(Guid confirmedByUserId, DateTimeOffset confirmedAt)
    {
        if (ConfirmationStatus == BookingTransferConfirmationStatus.CONFIRMED)
        {
            return;
        }

        if (ConfirmationStatus != BookingTransferConfirmationStatus.PENDING_CONFIRM)
            throw new InvalidOperationException("Only a pending transfer may be confirmed.");
        if (NewSeatNumber is null)
            throw new InvalidOperationException("A transfer without a replacement seat cannot be confirmed.");
        if (confirmedByUserId == Guid.Empty)
            throw new ArgumentException("Confirmed-by user id is required.", nameof(confirmedByUserId));

        ConfirmationStatus = BookingTransferConfirmationStatus.CONFIRMED;
        ConfirmedAt = confirmedAt;
        ConfirmedByUserId = confirmedByUserId;
    }

    private static string? NormalizeSeat(string? seatNumber)
        => string.IsNullOrWhiteSpace(seatNumber) ? null : seatNumber.Trim();
}
