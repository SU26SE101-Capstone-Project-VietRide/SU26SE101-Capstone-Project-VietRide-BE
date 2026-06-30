using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed record ScanBookingCodeForTripQuery(
    Guid TripId,
    string BookingCode,
    Guid CallerUserId) : IQuery<ScanBookingCodeForTripResult>;
