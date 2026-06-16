namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed record TripSeatMapDto(
    Guid TripId,
    string VehicleType,
    IReadOnlyList<TripSeatMapSeatDto> Seats);
