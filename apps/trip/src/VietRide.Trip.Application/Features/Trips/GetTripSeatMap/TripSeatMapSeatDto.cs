namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed record TripSeatMapSeatDto(
    string SeatNumber,
    string Status,
    string Type,
    int Row,
    int Col,
    int Deck);
