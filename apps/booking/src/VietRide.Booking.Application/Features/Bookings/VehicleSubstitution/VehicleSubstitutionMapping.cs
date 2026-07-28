namespace VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;

public sealed record VehicleSubstitutionMapping(
    Guid BookingId,
    Guid PassengerId,
    string? OriginalSeatNumber,
    string? NewSeatNumber,
    string OriginalBoardingStatus);
