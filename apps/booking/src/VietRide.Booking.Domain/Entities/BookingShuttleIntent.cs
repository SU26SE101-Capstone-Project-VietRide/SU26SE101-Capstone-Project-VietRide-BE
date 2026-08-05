using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

public sealed class BookingShuttleIntent : BaseEntity<Guid>
{
    public const string InboundDirection = "INBOUND_TO_STATION";
    public const string OutboundDirection = "OUTBOUND_FROM_STATION";

    public Guid BookingId { get; private set; }
    public string Direction { get; private set; } = InboundDirection;
    public string PickupAddress { get; private set; } = string.Empty;
    public decimal PickupLatitude { get; private set; }
    public decimal PickupLongitude { get; private set; }
    public int? RoadDistanceMeters { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? CancelledAt { get; private set; }

    public Booking Booking { get; private set; } = null!;

    private BookingShuttleIntent() { }

    public static BookingShuttleIntent Create(
        Guid bookingId,
        string pickupAddress,
        decimal pickupLatitude,
        decimal pickupLongitude,
        string direction = InboundDirection,
        int? roadDistanceMeters = null)
    {
        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("Booking id is required.", nameof(bookingId));
        }

        if (string.IsNullOrWhiteSpace(pickupAddress))
        {
            throw new ArgumentException("Shuttle pickup address is required.", nameof(pickupAddress));
        }

        if (pickupLatitude is < -90m or > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(pickupLatitude));
        }

        if (pickupLongitude is < -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(pickupLongitude));
        }

        if (!string.Equals(direction, InboundDirection, StringComparison.Ordinal)
            && !string.Equals(direction, OutboundDirection, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Shuttle direction is invalid.");
        }

        if (roadDistanceMeters is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roadDistanceMeters));
        }

        return new BookingShuttleIntent
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            Direction = direction,
            PickupAddress = pickupAddress.Trim(),
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            RoadDistanceMeters = roadDistanceMeters,
            IsActive = true,
        };
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        IsActive = false;
        CancelledAt ??= cancelledAt;
    }
}
