using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// Per-seat travel entitlement issued from a booking. Booking remains the order;
/// Ticket is the boarding/QR proof for one passenger seat.
/// </summary>
public sealed class Ticket : BaseEntity<Guid>
{
    public Guid BookingId { get; private set; }
    public Guid PassengerId { get; private set; }
    public TicketCode TicketCode { get; private set; }
    public string SeatNumber { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; } = TicketStatus.PENDING_PAYMENT;
    public Money FareAmount { get; private set; }
    public Money DiscountAmount { get; private set; }
    public Money PaidAmount { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    public Booking? Booking { get; private set; }
    public Passenger? Passenger { get; private set; }

    private Ticket() { }

    internal static Ticket CreatePendingPayment(
        Guid bookingId,
        Guid passengerId,
        TicketCode ticketCode,
        string seatNumber,
        Money fareAmount,
        Money discountAmount,
        Money paidAmount)
    {
        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("Booking id cannot be empty.", nameof(bookingId));
        }

        if (passengerId == Guid.Empty)
        {
            throw new ArgumentException("Passenger id cannot be empty.", nameof(passengerId));
        }

        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new ArgumentException("Seat number cannot be null or whitespace.", nameof(seatNumber));
        }

        if (paidAmount > fareAmount)
        {
            throw new ArgumentException("Paid amount cannot exceed fare amount.", nameof(paidAmount));
        }

        return new Ticket
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            PassengerId = passengerId,
            TicketCode = ticketCode,
            SeatNumber = seatNumber.Trim(),
            FareAmount = fareAmount,
            DiscountAmount = discountAmount,
            PaidAmount = paidAmount,
            Status = TicketStatus.PENDING_PAYMENT,
        };
    }

    public void Issue(DateTimeOffset issuedAt)
    {
        EnsureStatus(TicketStatus.PENDING_PAYMENT, nameof(Issue));
        Status = TicketStatus.ISSUED;
        IssuedAt = issuedAt;
    }

    public void MarkUsed(DateTimeOffset usedAt)
    {
        EnsureStatus(TicketStatus.ISSUED, nameof(MarkUsed));
        Status = TicketStatus.USED;
        UsedAt = usedAt;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (Status is not (TicketStatus.PENDING_PAYMENT or TicketStatus.ISSUED))
        {
            throw new InvalidOperationException($"Cannot cancel ticket in status {Status}.");
        }

        Status = TicketStatus.CANCELLED;
        CancelledAt = cancelledAt;
    }

    public void Refund(DateTimeOffset refundedAt)
    {
        EnsureStatus(TicketStatus.CANCELLED, nameof(Refund));
        Status = TicketStatus.REFUNDED;
        RefundedAt = refundedAt;
    }

    public void Expire(DateTimeOffset expiredAt)
    {
        EnsureStatus(TicketStatus.PENDING_PAYMENT, nameof(Expire));
        Status = TicketStatus.EXPIRED;
        ExpiredAt = expiredAt;
    }

    private void EnsureStatus(TicketStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Ticket must be {expected} before {operation}.");
        }
    }
}
