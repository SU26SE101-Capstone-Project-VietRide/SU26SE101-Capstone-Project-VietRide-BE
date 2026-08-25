namespace VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;

public sealed record VehicleSubstitutionMapping(
    Guid BookingId,
    Guid PassengerId,
    string? OriginalSeatNumber,
    string? NewSeatNumber,
    string OriginalBoardingStatus,
    string? OriginalSeatType = null,
    string? NewSeatType = null,
    bool IsSeatDowngrade = false);
