using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

public sealed class BookingShuttleIntent : BaseEntity<Guid>
{
    public Guid BookingId { get; private set; }
    public string PickupAddress { get; private set; } = string.Empty;
    public decimal PickupLatitude { get; private set; }
    public decimal PickupLongitude { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? CancelledAt { get; private set; }

    public Booking Booking { get; private set; } = null!;

    private BookingShuttleIntent() { }

    public static BookingShuttleIntent Create(
        Guid bookingId,
        string pickupAddress,
        decimal pickupLatitude,
        decimal pickupLongitude)
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

        return new BookingShuttleIntent
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            PickupAddress = pickupAddress.Trim(),
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            IsActive = true,
        };
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        IsActive = false;
        CancelledAt ??= cancelledAt;
    }
}
