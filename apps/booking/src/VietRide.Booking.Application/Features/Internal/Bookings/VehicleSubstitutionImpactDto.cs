namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record VehicleSubstitutionImpactDto(
    Guid OldTripId,
    Guid OperatorId,
    IReadOnlyList<VehicleSubstitutionImpactDto.BookingImpact> Bookings)
{
    public sealed record BookingImpact(
        Guid BookingId,
        string BookingStatus,
        IReadOnlyList<PassengerImpact> Passengers);

    public sealed record PassengerImpact(
        Guid PassengerId,
        string BoardingStatus,
        string? OriginalSeatNumber);
}
