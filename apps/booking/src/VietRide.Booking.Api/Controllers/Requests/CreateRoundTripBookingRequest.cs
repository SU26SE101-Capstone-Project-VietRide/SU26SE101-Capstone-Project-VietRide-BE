namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// HTTP request body for POST /v1/bookings/round-trip.
/// Shape per VietRide_API_Contract_v1.md lines 727-744.
/// </summary>
public sealed class CreateRoundTripBookingRequest
{
    public RoundTripBookingLegRequest Outbound { get; init; } = new();

    public RoundTripBookingLegRequest Return { get; init; } = new();

    /// <summary>Optional voucher code — accepted but ignored until Day 14.</summary>
    public string? VoucherCode { get; init; }

    /// <summary>WALLET or VNPAY.</summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary>Required as MOBILE_SDK when paymentMethod is VNPAY.</summary>
    public string? PaymentReturnMode { get; init; }

    public sealed class RoundTripBookingLegRequest
    {
        public Guid TripId { get; init; }

        public PickupRequest? Pickup { get; init; }

        public DropoffRequest? Dropoff { get; init; }
        public ShuttlePickupRequest? ShuttlePickup { get; init; }
        public ShuttleDropoffRequest? ShuttleDropoff { get; init; }

        public IReadOnlyList<SeatBookingRequest> Seats { get; init; } = [];
    }
}
