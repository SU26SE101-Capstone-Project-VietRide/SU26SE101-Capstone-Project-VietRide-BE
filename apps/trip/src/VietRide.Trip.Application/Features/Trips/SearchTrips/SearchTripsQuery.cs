using MediatR;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed record SearchTripsQuery(
    Guid? OriginStationId,
    Guid? DestinationStationId,
    DateOnly DepartureDate,
    int PassengerCount,
    bool? AllowAlongRoutePickup,
    string? OriginProvinceCode = null,
    string? OriginWardCode = null,
    string? DestinationProvinceCode = null,
    string? DestinationWardCode = null,
    string? OriginLocationCode = null,
    string? DestinationLocationCode = null)
    : IRequest<SearchTripsResult>;
