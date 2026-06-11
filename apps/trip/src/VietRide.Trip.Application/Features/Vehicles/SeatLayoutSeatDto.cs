namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record SeatLayoutSeatDto(
    string SeatNumber,
    int Row,
    int Col,
    int Deck,
    string Type,
    bool IsWindow,
    bool IsAisle,
    bool Disabled);
