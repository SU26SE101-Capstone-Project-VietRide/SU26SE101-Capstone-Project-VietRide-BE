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

    public IReadOnlyList<SeatBookingRequest> Seats { get; init; } = [];

    /// <summary>Optional voucher code — no-op this day; discount applied on Day 14.</summary>
    public string? VoucherCode { get; init; }

    /// <summary>WALLET or VNPAY.</summary>
    public string PaymentMethod { get; init; } = string.Empty;
}

public sealed class ShuttlePickupRequest
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
/// Per-seat booking request. PII (fullName, phoneNumber, idNumber) is validated but
/// NOT persisted (schema.sql line 149 — passengers is operational-only).
/// </summary>
public sealed class SeatBookingRequest
{
    public string SeatNumber { get; init; } = string.Empty;
    public PassengerPiiRequest Passenger { get; init; } = new();
}

/// <summary>
/// Passenger PII — validated at application layer then discarded.
/// </summary>
public sealed class PassengerPiiRequest
{
    public string FullName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string IdNumber { get; init; } = string.Empty;
}
