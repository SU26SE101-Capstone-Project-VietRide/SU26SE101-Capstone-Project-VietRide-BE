namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record SubstituteVehicleSeatAssignment(
    Guid PassengerId,
    string NewSeatNumber);
