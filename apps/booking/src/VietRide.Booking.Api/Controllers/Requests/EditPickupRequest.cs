namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// HTTP request body for POST /v1/bookings/{bookingId}/edit-pickup.
/// Shape per VietRide_API_Contract_v1.md lines 840-845.
/// </summary>
public sealed class EditPickupRequest
{
    public PickupRequest Pickup { get; init; } = new();

    /// <summary>WALLET or VNPAY. Day-13 price-neutral-only path does not call payment.</summary>
    public string PaymentMethod { get; init; } = string.Empty;
}
