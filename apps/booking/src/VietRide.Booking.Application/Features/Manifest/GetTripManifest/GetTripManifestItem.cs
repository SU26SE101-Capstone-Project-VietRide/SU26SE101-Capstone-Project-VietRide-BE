namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

/// <summary>
/// Operational data for one passenger seat in a paid booking.
/// Buyer contact is exposed only during active crew operations.
/// A null pickup stop denotes terminal pickup at the trip origin.
/// </summary>
public sealed record GetTripManifestItem(
    Guid PassengerRecordId,
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string BookingCode,
    Guid? PickupStop,
    string BoardingStatus,
    string? PickupPointName,
    string? BuyerName,
    string? BuyerPhone);
