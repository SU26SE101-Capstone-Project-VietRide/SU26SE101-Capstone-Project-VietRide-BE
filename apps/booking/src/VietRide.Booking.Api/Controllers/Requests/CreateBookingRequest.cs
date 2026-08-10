using System.Text.Json.Serialization;

namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// HTTP request body for POST /v1/bookings.
/// Shape per VietRide_API_Contract_v1.md lines 685-703.
/// </summary>
public sealed class CreateBookingRequest
{
    public Guid TripId { get; init; }

    public PickupRequest? Pickup { get; init; }
    public DropoffRequest? Dropoff { get; init; }
    public ShuttlePickupRequest? ShuttlePickup { get; init; }
    public ShuttleDropoffRequest? ShuttleDropoff { get; init; }

    public IReadOnlyList<SeatBookingRequest> Seats { get; init; } = [];

    /// <summary>Optional voucher code — no-op this day; discount applied on Day 14.</summary>
    public string? VoucherCode { get; init; }

    /// <summary>WALLET or VNPAY.</summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary>Required as MOBILE_SDK when paymentMethod is VNPAY.</summary>
    public string? PaymentReturnMode { get; init; }
}

public sealed class ShuttlePickupRequest
{
    public string Address { get; init; } = string.Empty;
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }
}

public sealed class ShuttleDropoffRequest
{
    public string Address { get; init; } = string.Empty;
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }
}

/// <summary>
/// Pickup location — exactly one of StationId/StopId.
/// </summary>
public sealed class PickupRequest
{
    [JsonPropertyName("stationId")]
    public Guid? StationId { get; init; }

    [JsonPropertyName("stopId")]
    public Guid? StopId { get; init; }
}

/// <summary>
/// Dropoff location — at most one of StationId/StopId.
/// </summary>
public sealed class DropoffRequest
{
    [JsonPropertyName("stationId")]
    public Guid? StationId { get; init; }

    [JsonPropertyName("stopId")]
    public Guid? StopId { get; init; }
}

/// <summary>
/// Per-seat booking request. Passenger records are operational-only and contain no PII.
/// </summary>
public sealed class SeatBookingRequest
{
    public string SeatNumber { get; init; } = string.Empty;
}
