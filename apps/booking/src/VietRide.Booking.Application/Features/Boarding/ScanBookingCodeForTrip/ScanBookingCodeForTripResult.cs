namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed record ScanBookingCodeForTripResult(
    IReadOnlyList<ScanBookingCodePassengerItem> Items);
