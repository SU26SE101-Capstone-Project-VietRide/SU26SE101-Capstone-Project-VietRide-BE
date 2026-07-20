namespace VietRide.Trip.Application.Features.OperatorReports;

public sealed record TripOperatorOccupancyRow(
    Guid TripId,
    Guid RouteId,
    string Status,
    DateTimeOffset DepartureAt,
    long SellableSeatCount,
    long BookedSeatCount);
