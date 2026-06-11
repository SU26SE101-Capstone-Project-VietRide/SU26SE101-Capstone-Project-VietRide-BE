using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// Booking aggregate root. Represents one buyer's booking for one trip
/// covering 1–5 seats (sub-entity <see cref="Passenger"/>).
/// No soft-delete on Booking — cancellation is tracked via <see cref="CancellationReason"/>
/// and <see cref="CancelledAt"/>.
/// <see cref="TotalAmount"/> is immutable after INSERT (snapshot of fare at booking time).
/// </summary>
public sealed class Booking : BaseEntity<Guid>
{
    private readonly List<Passenger> _passengers = [];
    private readonly List<BookingPendingAction> _pendingActions = [];

    public BookingCode BookingCode { get; private set; }

    // Logical FKs — no REFERENCES at DB layer (cross-service references)
    public Guid PassengerUserId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid OperatorId { get; private set; }

    // Pickup/dropoff — exactly one pickup, at most one dropoff (CHECK constraints)
    public Guid? PickupStationId { get; private set; }
    public Guid? PickupStopId { get; private set; }
    public Guid? DropoffStationId { get; private set; }
    public Guid? DropoffStopId { get; private set; }

    // Amounts — BIGINT VND; total_amount immutable after INSERT
    public Money BaseFare { get; private set; }
    public Money DiscountAmount { get; private set; }
    public Money TotalAmount { get; private set; }

    public BookingStatus Status { get; private set; }
    public BookingCancellationReason? CancellationReason { get; private set; }
    public bool RefundOverride { get; private set; }

    // Round-trip
    public Guid? BookingGroupId { get; private set; }
    public TripDirection? TripDirection { get; private set; }

    // Trip snapshot (avoid cross-service call for history list)
    public string? TripSnapshotOriginName { get; private set; }
    public string? TripSnapshotDestName { get; private set; }
    public DateTimeOffset? TripSnapshotDeparture { get; private set; }
    public string? TripSnapshotRouteName { get; private set; }

    // Lifecycle timestamps
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // Collections
    public IReadOnlyList<Passenger> Passengers => _passengers.AsReadOnly();
    public IReadOnlyList<BookingPendingAction> PendingActions => _pendingActions.AsReadOnly();

    private Booking() { }

    /// <summary>
    /// Creates a new booking in <see cref="BookingStatus.PENDING_PAYMENT"/> state.
    /// Validates pickup constraint (exactly one of pickupStationId/pickupStopId must be provided).
    /// </summary>
    public static Booking CreatePendingPayment(
        BookingCode bookingCode,
        Guid passengerUserId,
        Guid tripId,
        Guid operatorId,
        Guid? pickupStationId,
        Guid? pickupStopId,
        Guid? dropoffStationId,
        Guid? dropoffStopId,
        Money baseFare,
        Money discountAmount,
        Money totalAmount,
        string? tripSnapshotOriginName = null,
        string? tripSnapshotDestName = null,
        DateTimeOffset? tripSnapshotDeparture = null,
        string? tripSnapshotRouteName = null,
        Guid? bookingGroupId = null,
        Enums.TripDirection? tripDirection = null)
    {
        // Pickup: exactly one must be set
        var pickupCount = (pickupStationId.HasValue ? 1 : 0) + (pickupStopId.HasValue ? 1 : 0);
        if (pickupCount != 1)
            throw new ArgumentException("Exactly one of pickupStationId or pickupStopId must be provided.");

        // Dropoff: at most one
        var dropoffCount = (dropoffStationId.HasValue ? 1 : 0) + (dropoffStopId.HasValue ? 1 : 0);
        if (dropoffCount > 1)
            throw new ArgumentException("At most one of dropoffStationId or dropoffStopId may be provided.");

        // Amount invariants
        if (totalAmount > baseFare)
            throw new ArgumentException("Total amount cannot exceed base fare.");

        return new Booking
        {
            Id = Guid.NewGuid(),
            BookingCode = bookingCode,
            PassengerUserId = passengerUserId,
            TripId = tripId,
            OperatorId = operatorId,
            PickupStationId = pickupStationId,
            PickupStopId = pickupStopId,
            DropoffStationId = dropoffStationId,
            DropoffStopId = dropoffStopId,
            BaseFare = baseFare,
            DiscountAmount = discountAmount,
            TotalAmount = totalAmount,
            Status = BookingStatus.PENDING_PAYMENT,
            RefundOverride = false,
            TripSnapshotOriginName = tripSnapshotOriginName,
            TripSnapshotDestName = tripSnapshotDestName,
            TripSnapshotDeparture = tripSnapshotDeparture,
            TripSnapshotRouteName = tripSnapshotRouteName,
            BookingGroupId = bookingGroupId,
            TripDirection = tripDirection,
        };
    }

    /// <summary>
    /// Adds a passenger seat to this booking. Maximum 5 passengers enforced here and at DB layer.
    /// </summary>
    public Passenger AddPassenger(string seatNumber)
    {
        if (_passengers.Count >= 5)
            throw new InvalidOperationException("A booking cannot have more than 5 passengers.");

        if (_passengers.Any(p => p.SeatNumber.Equals(seatNumber, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Seat '{seatNumber}' is already added to this booking.");

        var passenger = Passenger.Create(Id, seatNumber);
        _passengers.Add(passenger);
        return passenger;
    }

    /// <summary>
    /// Marks the booking as CONFIRMED (after successful payment).
    /// Only valid from PENDING_PAYMENT state.
    /// </summary>
    public void Confirm(DateTimeOffset confirmedAt)
    {
        if (Status != BookingStatus.PENDING_PAYMENT)
            throw new InvalidOperationException($"Cannot confirm booking in status {Status}.");

        Status = BookingStatus.CONFIRMED;
        ConfirmedAt = confirmedAt;
    }

    /// <summary>
    /// Guard: asserts the booking is still in <see cref="BookingStatus.PENDING_PAYMENT"/> state.
    /// Throws <see cref="InvalidOperationException"/> if the booking has already transitioned away.
    /// Does NOT change state — call before operations that are only valid while awaiting payment.
    /// </summary>
    public void MarkPendingPayment()
    {
        if (Status != BookingStatus.PENDING_PAYMENT)
            throw new InvalidOperationException($"Booking is already in status {Status}; cannot set back to PENDING_PAYMENT.");
    }

    /// <summary>Cancels the booking with the given reason.</summary>
    public void Cancel(BookingCancellationReason reason, DateTimeOffset cancelledAt, bool refundOverride = false)
    {
        if (Status is BookingStatus.CANCELLED or BookingStatus.REFUNDED)
            throw new InvalidOperationException($"Booking is already {Status}.");

        Status = BookingStatus.CANCELLED;
        CancellationReason = reason;
        CancelledAt = cancelledAt;
        RefundOverride = refundOverride;
    }
}
