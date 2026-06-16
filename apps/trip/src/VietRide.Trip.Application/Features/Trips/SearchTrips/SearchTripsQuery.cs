using MediatR;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed record SearchTripsQuery(
    Guid OriginStationId,
    Guid DestinationStationId,
    DateOnly DepartureDate,
    int PassengerCount,
    bool? AllowAlongRoutePickup)
    : IRequest<SearchTripsResult>;
