using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.OperatorReports;

public sealed record TripOperatorOccupancyRow(
    Guid TripId,
    Guid RouteId,
    string TripCode,
    string RouteName,
    string VehicleLicensePlate,
    TripStatus Status,
    DateTimeOffset DepartureAt,
    long SellableSeatCount,
    long BookedSeatCount);
