namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public sealed record VehicleSubstitutionImpactProjection(
    Guid OldTripId,
    Guid OperatorId,
    IReadOnlyList<VehicleSubstitutionImpactProjection.Booking> Bookings)
{
    public sealed record Booking(
        Guid BookingId,
        string BookingStatus,
        IReadOnlyList<Passenger> Passengers);

    public sealed record Passenger(
        Guid PassengerId,
        string BoardingStatus,
        string? OriginalSeatNumber);
}
