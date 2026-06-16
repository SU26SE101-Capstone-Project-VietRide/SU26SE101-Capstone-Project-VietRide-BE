namespace VietRide.Trip.Application.Features.Internal.Trips.Requests;

public sealed record PassengerSeatAssignmentRequest(Guid PassengerId, string SeatNumber);
