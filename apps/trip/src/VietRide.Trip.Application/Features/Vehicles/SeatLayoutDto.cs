namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record SeatLayoutDto(
    int Version,
    string VehicleTypeCode,
    int TotalSeats,
    int Rows,
    int Cols,
    int Decks,
    IReadOnlyList<SeatLayoutAisleDto> Aisles,
    IReadOnlyList<SeatLayoutSeatDto> Seats);
