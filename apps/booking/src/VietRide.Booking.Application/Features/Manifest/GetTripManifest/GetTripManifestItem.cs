namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

/// <summary>
/// PII-free operational data for one passenger seat in a confirmed booking.
/// A null pickup stop denotes terminal pickup at the trip origin.
/// </summary>
public sealed record GetTripManifestItem(
    string SeatNumber,
    string BookingCode,
    Guid? PickupStop,
    string BoardingStatus);
