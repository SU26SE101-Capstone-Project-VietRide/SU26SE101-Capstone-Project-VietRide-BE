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
    private readonly List<Ticket> _tickets = [];
    private readonly List<BookingPendingAction> _pendingActions = [];
    private BookingShuttleIntent? _shuttleIntent;

    public BookingCode BookingCode { get; private set; }

    // Logical FKs — no REFERENCES at DB layer (cross-service references)
    public Guid PassengerUserId { get; private set; }
    public string? BuyerDisplayName { get; private set; }
    public string? BuyerPhone { get; private set; }
    public string? BuyerEmail { get; private set; }
    public string? BuyerAvatarUrl { get; private set; }
    public Guid TripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid? SeatLockToken { get; private set; }

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

    // Immutable trip snapshot (avoid cross-service call for history list)
    public string? TripSnapshotOriginName { get; private set; }
    public string? TripSnapshotDestName { get; private set; }
    public DateTimeOffset? TripSnapshotDeparture { get; private set; }

    // Mutable schedule projection, initialized from the departure snapshot.
    public DateTimeOffset? TripCurrentDeparture { get; private set; }
    public string? TripSnapshotRouteName { get; private set; }

    // Lifecycle timestamps
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // Collections
    public IReadOnlyList<Passenger> Passengers => _passengers.AsReadOnly();
    public IReadOnlyList<Ticket> Tickets => _tickets.AsReadOnly();
    public IReadOnlyList<BookingPendingAction> PendingActions => _pendingActions.AsReadOnly();
    public BookingShuttleIntent? ShuttleIntent => _shuttleIntent;

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
        Enums.TripDirection? tripDirection = null,
        Guid? seatLockToken = null,
        DateTimeOffset? tripCurrentDeparture = null,
        string? buyerDisplayName = null,
        string? buyerPhone = null,
        string? buyerEmail = null,
        string? buyerAvatarUrl = null)
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

        if (tripCurrentDeparture.HasValue && tripCurrentDeparture != tripSnapshotDeparture)
            throw new ArgumentException(
                "Trip current departure must initially match the trip snapshot departure.",
                nameof(tripCurrentDeparture));

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingCode = bookingCode,
            PassengerUserId = passengerUserId,
            TripId = tripId,
            OperatorId = operatorId,
            SeatLockToken = seatLockToken,
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
            TripCurrentDeparture = tripCurrentDeparture ?? tripSnapshotDeparture,
            TripSnapshotRouteName = tripSnapshotRouteName,
            BookingGroupId = bookingGroupId,
            TripDirection = tripDirection,
        };

        if (buyerDisplayName is not null
            || buyerPhone is not null
            || buyerEmail is not null
            || buyerAvatarUrl is not null)
        {
            booking.CaptureBuyerSnapshot(
                buyerDisplayName ?? throw new ArgumentException(
                    "Buyer display name is required when capturing a buyer snapshot.",
                    nameof(buyerDisplayName)),
                buyerPhone,
                buyerEmail,
                buyerAvatarUrl);
        }

        return booking;
    }

    public bool CaptureBuyerSnapshot(
        string displayName,
        string? phone,
        string? email,
        string? avatarUrl)
    {
        if (BuyerDisplayName is not null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Buyer display name is required.", nameof(displayName));
        }

        BuyerDisplayName = displayName.Trim();
        BuyerPhone = NormalizeOptional(phone);
        BuyerEmail = NormalizeOptional(email);
        BuyerAvatarUrl = NormalizeOptional(avatarUrl);
        return true;
    }

    /// <summary>
    /// Adds a passenger seat to this booking. Maximum 5 passengers enforced here and at DB layer.
    /// </summary>
    public Passenger AddPassenger(string seatNumber)
    {
        if (_passengers.Count >= 5)
            throw new InvalidOperationException("A booking cannot have more than 5 passengers.");

        if (_passengers.Any(p => string.Equals(
                p.SeatNumber,
                seatNumber,
                StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Seat '{seatNumber}' is already added to this booking.");

        var passenger = Passenger.Create(Id, seatNumber);
        _passengers.Add(passenger);
        return passenger;
    }

    public Ticket AddTicketedPassenger(
        string seatNumber,
        TicketCode ticketCode,
        Money fareAmount,
        Money discountAmount,
        Money paidAmount)
    {
        var passenger = AddPassenger(seatNumber);
        var passengerSeatNumber = passenger.SeatNumber
            ?? throw new InvalidOperationException("A newly created passenger must have a seat number.");
        var ticket = Ticket.CreatePendingPayment(
            Id,
            passenger.Id,
            ticketCode,
            passengerSeatNumber,
            fareAmount,
            discountAmount,
            paidAmount);
        _tickets.Add(ticket);
        return ticket;
    }

    public void RequestShuttle(string address, decimal latitude, decimal longitude)
    {
        if (_shuttleIntent is not null)
        {
            throw new InvalidOperationException("A shuttle intent already exists for this booking.");
        }

        if (!PickupStationId.HasValue || PickupStopId.HasValue)
        {
            throw new InvalidOperationException("Shuttle is available only for station pickup.");
        }

        _shuttleIntent = BookingShuttleIntent.Create(Id, address, latitude, longitude);
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

        foreach (var ticket in _tickets.Where(ticket => ticket.Status == TicketStatus.PENDING_PAYMENT))
        {
            ticket.Issue(confirmedAt);
        }
    }

    /// <summary>
    /// Marks an unpaid booking as EXPIRED after its payment window closes.
    /// Only valid from PENDING_PAYMENT state.
    /// </summary>
    public void ExpirePayment(DateTimeOffset expiredAt)
    {
        if (Status != BookingStatus.PENDING_PAYMENT)
            throw new InvalidOperationException($"Cannot expire booking in status {Status}.");

        Status = BookingStatus.EXPIRED;
        ExpiredAt = expiredAt;

        foreach (var ticket in _tickets.Where(ticket => ticket.Status == TicketStatus.PENDING_PAYMENT))
        {
            ticket.Expire(expiredAt);
        }
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

    /// <summary>
    /// Changes the pickup target. Exactly one pickup station or pickup stop must be provided.
    /// Only confirmed bookings may be edited.
    /// </summary>
    public void ChangePickup(Guid? pickupStationId, Guid? pickupStopId)
    {
        EnsureConfirmedForEdit();

        if (_shuttleIntent?.IsActive == true)
        {
            throw new InvalidOperationException("Pickup is locked while a shuttle intent is active.");
        }

        if (CountProvided(pickupStationId, pickupStopId) != 1)
            throw new ArgumentException("Exactly one of pickupStationId or pickupStopId must be provided.");

        PickupStationId = pickupStationId;
        PickupStopId = pickupStopId;
    }

    /// <summary>
    /// Changes the dropoff target. At most one dropoff station or dropoff stop may be provided.
    /// </summary>
    public void ChangeDropoff(Guid? dropoffStationId, Guid? dropoffStopId)
    {
        EnsureConfirmedForEdit();

        if (CountProvided(dropoffStationId, dropoffStopId) > 1)
            throw new ArgumentException("At most one of dropoffStationId or dropoffStopId may be provided.");

        DropoffStationId = dropoffStationId;
        DropoffStopId = dropoffStopId;
    }

    /// <summary>
    /// Links this booking to a round-trip display group and marks its trip direction.
    /// </summary>
    public void AssignRoundTripGroup(Guid bookingGroupId, TripDirection tripDirection)
    {
        if (bookingGroupId == Guid.Empty)
            throw new ArgumentException("Booking group id must not be empty.", nameof(bookingGroupId));

        if (!Enum.IsDefined(tripDirection))
            throw new ArgumentOutOfRangeException(nameof(tripDirection), tripDirection, "Trip direction is invalid.");

        BookingGroupId = bookingGroupId;
        TripDirection = tripDirection;
    }

    private void EnsureConfirmedForEdit()
    {
        if (Status != BookingStatus.CONFIRMED)
            throw new InvalidOperationException($"Cannot edit booking in status {Status}.");
    }

    private static int CountProvided(Guid? first, Guid? second)
        => (first.HasValue ? 1 : 0) + (second.HasValue ? 1 : 0);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Cancels the booking with the given reason.</summary>
    public void Cancel(BookingCancellationReason reason, DateTimeOffset cancelledAt, bool refundOverride = false)
    {
        if (Status is BookingStatus.CANCELLED or BookingStatus.REFUNDED)
            throw new InvalidOperationException($"Booking is already {Status}.");

        Status = BookingStatus.CANCELLED;
        CancellationReason = reason;
        CancelledAt = cancelledAt;
        RefundOverride = refundOverride;
        _shuttleIntent?.Cancel(cancelledAt);

        foreach (var ticket in _tickets.Where(ticket =>
            ticket.Status is TicketStatus.PENDING_PAYMENT or TicketStatus.ISSUED))
        {
            ticket.Cancel(cancelledAt);
        }
    }

    /// <summary>
    /// Marks an active in-progress booking as disrupted when its Trip cannot be substituted.
    /// </summary>
    public void Disrupt(DateTimeOffset disruptedAt)
    {
        if (Status is not BookingStatus.CONFIRMED and not BookingStatus.PARTIAL_NO_SHOW)
            throw new InvalidOperationException($"Cannot disrupt booking in status {Status}.");

        Status = BookingStatus.DISRUPTED;
        CancellationReason = BookingCancellationReason.OPERATOR_DISRUPTED_IN_PROGRESS;
        CancelledAt = disruptedAt;
        RefundOverride = true;
        _shuttleIntent?.Cancel(disruptedAt);

        foreach (var ticket in _tickets.Where(ticket =>
            ticket.Status is TicketStatus.PENDING_PAYMENT or TicketStatus.ISSUED))
        {
            ticket.Cancel(disruptedAt);
        }
    }

    /// <summary>
    /// Marks a cancelled or disrupted booking as REFUNDED after wallet credit succeeds.
    /// </summary>
    public void MarkRefunded(DateTimeOffset refundedAt)
    {
        if (Status is not BookingStatus.CANCELLED and not BookingStatus.DISRUPTED)
            throw new InvalidOperationException($"Cannot refund booking in status {Status}.");

        Status = BookingStatus.REFUNDED;
        RefundedAt = refundedAt;

        foreach (var ticket in _tickets.Where(ticket => ticket.Status == TicketStatus.CANCELLED))
        {
            ticket.Refund(refundedAt);
        }
    }

    public IReadOnlyList<Guid> MarkPendingPassengersNoShow()
    {
        if (Status != BookingStatus.CONFIRMED)
        {
            return [];
        }

        var newlyMarked = _passengers
            .Where(passenger => passenger.MarkNoShow())
            .Select(passenger => passenger.Id)
            .ToArray();
        if (newlyMarked.Length == 0)
        {
            return newlyMarked;
        }

        Status = _passengers.All(passenger => passenger.BoardingStatus == PassengerBoardingStatus.NO_SHOW)
            ? BookingStatus.NO_SHOW
            : BookingStatus.PARTIAL_NO_SHOW;
        return newlyMarked;
    }

    public void ApplyVehicleSubstitution(Guid originalTripId, Guid newTripId)
    {
        if (originalTripId == Guid.Empty)
            throw new ArgumentException("Original Trip id is required.", nameof(originalTripId));
        if (newTripId == Guid.Empty)
            throw new ArgumentException("New Trip id is required.", nameof(newTripId));
        if (originalTripId == newTripId)
            throw new ArgumentException("Replacement Trip must differ from the original Trip.", nameof(newTripId));
        if (TripId != originalTripId)
            throw new InvalidOperationException("Booking is no longer assigned to the original Trip.");
        if (Status is not BookingStatus.CONFIRMED and not BookingStatus.PARTIAL_NO_SHOW)
            throw new InvalidOperationException($"Booking status {Status} is not eligible for vehicle substitution.");

        TripId = newTripId;
    }
}
