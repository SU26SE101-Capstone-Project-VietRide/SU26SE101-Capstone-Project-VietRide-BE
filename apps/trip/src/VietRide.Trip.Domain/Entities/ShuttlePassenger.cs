using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttlePassenger : BaseEntity<Guid>
{
    public const string InboundDirection = "INBOUND_TO_STATION";
    public const string PendingAssignmentStatus = "PENDING_ASSIGNMENT";
    public const string PendingStatus = "PENDING";
    public const string PickedUpStatus = "PICKED_UP";
    public const string DeliveredStatus = "DELIVERED";
    public const string CancelledStatus = "CANCELLED";
    public Guid? ShuttleTripId { get; private set; }
    public Guid MainTripId { get; private set; }
    public Guid? BookingId { get; private set; }
    public Guid? TicketId { get; private set; }
    public Guid? PassengerUserId { get; private set; }
    public string Direction { get; private set; } = InboundDirection;
    public string PickupAddress { get; private set; } = string.Empty;
    public decimal PickupLat { get; private set; }
    public decimal PickupLng { get; private set; }
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
        decimal pickupLng)
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

        return new ShuttlePassenger
        {
            Id = Guid.NewGuid(),
            MainTripId = mainTripId,
            BookingId = bookingId,
            TicketId = ticketId,
            PassengerUserId = passengerUserId,
            Direction = InboundDirection,
            PickupAddress = pickupAddress.Trim(),
            PickupLat = pickupLat,
            PickupLng = pickupLng,
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

    public void Cancel(string reason)
    {
        if (Status is DeliveredStatus or CancelledStatus)
        {
            return;
        }

        Status = CancelledStatus;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
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

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
