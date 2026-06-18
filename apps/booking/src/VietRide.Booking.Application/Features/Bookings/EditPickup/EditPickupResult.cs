namespace VietRide.Booking.Application.Features.Bookings.EditPickup;

/// <summary>
/// Response DTO for POST /v1/bookings/{bookingId}/edit-pickup.
/// Shape per VietRide_API_Contract_v1.md lines 848-859.
/// </summary>
public sealed record EditPickupResult(
    Guid BookingId,
    EditPickupResult.PickupDto Pickup,
    long FareDelta,
    long RefundAmount,
    string? PaymentRedirectUrl)
{
    public sealed record PickupDto(Guid? StationId, Guid? StopId);
}
