using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed record TripSeatMapDto(
    Guid TripId,
    string VehicleType,
    IReadOnlyList<TripSeatMapSeatDto> Seats)
{
    public IReadOnlyList<SeatLayoutAisleDto> Aisles { get; init; } = [];
}
