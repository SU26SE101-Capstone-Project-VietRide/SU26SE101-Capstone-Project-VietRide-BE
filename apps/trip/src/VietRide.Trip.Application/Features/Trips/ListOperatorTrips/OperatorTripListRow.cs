using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record OperatorTripListRow(
    Guid TripId,
    TripStatus Status,
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName,
    Guid VehicleId,
    string LicensePlate,
    VehicleStatus VehicleStatus,
    Guid DriverUserId,
    Guid? AssistantUserId,
    DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalEstimate);
