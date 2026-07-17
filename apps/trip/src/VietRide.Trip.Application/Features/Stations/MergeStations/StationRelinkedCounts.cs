namespace VietRide.Trip.Application.Features.Stations.MergeStations;

public sealed record StationRelinkedCounts(
    int OperatorMappings,
    int CollapsedOperatorMappings,
    int RouteOrigins,
    int RouteDestinations,
    int AlternativeRoutes,
    int ShuttleTrips,
    int FlattenedRedirects);
