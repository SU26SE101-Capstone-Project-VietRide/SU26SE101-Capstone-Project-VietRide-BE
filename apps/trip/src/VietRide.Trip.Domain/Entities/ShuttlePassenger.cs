using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttlePassenger : BaseEntity<Guid>
{
    public const string InboundDirection = "INBOUND_TO_STATION";
    public const string OutboundDirection = "OUTBOUND_FROM_STATION";
    public const string PendingAssignmentStatus = "PENDING_ASSIGNMENT";
    public const string PendingStatus = "PENDING";
    public const string PickedUpStatus = "PICKED_UP";
    public const string DeliveredStatus = "DELIVERED";
    public const string NoShowStatus = "NO_SHOW";
    public const string CancelledStatus = "CANCELLED";
    public Guid? ShuttleTripId { get; private set; }
    public Guid MainTripId { get; private set; }
    public Guid? BookingId { get; private set; }
    public string? BookingCode { get; private set; }
    public Guid? TicketId { get; private set; }
    public Guid? PassengerUserId { get; private set; }
    public string Direction { get; private set; } = InboundDirection;
    public string PickupAddress { get; private set; } = string.Empty;
    public decimal PickupLat { get; private set; }
    public decimal PickupLng { get; private set; }
    public int? RoadDistanceMeters { get; private set; }
    public DateTimeOffset? ScheduledPickupTime { get; private set; }
    public int? PickupOrder { get; private set; }
    public string Status { get; private set; } = PendingAssignmentStatus;
    public DateTimeOffset? PickedUpAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? CancelReason { get; private set; }

    private ShuttlePassenger() { }

    public static ShuttlePassenger Request(
        Guid mainTripId,
        Guid bookingId,
        Guid ticketId,
        Guid passengerUserId,
        string pickupAddress,
        decimal pickupLat,
        decimal pickupLng,
        string direction = InboundDirection,
        int? roadDistanceMeters = null,
        string? bookingCode = null)
    {
        ValidateId(mainTripId, nameof(mainTripId));
        ValidateId(bookingId, nameof(bookingId));
        ValidateId(ticketId, nameof(ticketId));
        ValidateId(passengerUserId, nameof(passengerUserId));
        if (string.IsNullOrWhiteSpace(pickupAddress))
        {
            throw new ArgumentException("Pickup address is required.", nameof(pickupAddress));
        }

        if (pickupLat is < -90m or > 90m || pickupLng is < -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(pickupLat), "Pickup coordinates are invalid.");
        }

        if (direction is not (InboundDirection or OutboundDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (roadDistanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roadDistanceMeters));
        }

        if (bookingCode is not null && string.IsNullOrWhiteSpace(bookingCode))
        {
            throw new ArgumentException("Booking code cannot be whitespace.", nameof(bookingCode));
        }

        return new ShuttlePassenger
        {
            Id = Guid.NewGuid(),
            MainTripId = mainTripId,
            BookingId = bookingId,
            BookingCode = bookingCode?.Trim(),
            TicketId = ticketId,
            PassengerUserId = passengerUserId,
            Direction = direction,
            PickupAddress = pickupAddress.Trim(),
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            RoadDistanceMeters = roadDistanceMeters,
        };
    }

    public void Assign(Guid shuttleTripId, int pickupOrder, DateTimeOffset? scheduledPickupTime = null)
    {
        ValidateId(shuttleTripId, nameof(shuttleTripId));
        if (pickupOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pickupOrder));
        }

        if (Status != PendingAssignmentStatus)
        {
            throw new InvalidOperationException("Only pending shuttle requests can be assigned.");
        }

        ShuttleTripId = shuttleTripId;
        PickupOrder = pickupOrder;
        ScheduledPickupTime = scheduledPickupTime;
        Status = PendingStatus;
    }

    public bool Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        if (Status is DeliveredStatus or NoShowStatus or CancelledStatus)
        {
            return false;
        }

        Status = CancelledStatus;
        CancelReason = reason.Trim();
        return true;
    }

    public bool MarkPickedUp(DateTimeOffset pickedUpAt)
    {
        if (Status is PickedUpStatus or DeliveredStatus)
        {
            return false;
        }

        if (Status != PendingStatus)
        {
            throw new InvalidOperationException("Only pending Shuttle passengers can be picked up.");
        }

        Status = PickedUpStatus;
        PickedUpAt = pickedUpAt;
        return true;
    }

    public bool MarkDelivered(DateTimeOffset deliveredAt)
    {
        if (Status == DeliveredStatus)
        {
            return false;
        }

        if (Status != PickedUpStatus)
        {
            throw new InvalidOperationException("Only picked-up Shuttle passengers can be delivered.");
        }

        Status = DeliveredStatus;
        DeliveredAt = deliveredAt;
        return true;
    }

    public bool MarkNoShow(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A no-show reason is required.", nameof(reason));
        }

        if (Status == NoShowStatus)
        {
            return false;
        }

        if (Status != PendingStatus)
        {
            throw new InvalidOperationException("Only pending Shuttle passengers can be marked no-show.");
        }

        Status = NoShowStatus;
        CancelReason = reason.Trim();
        return true;
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
