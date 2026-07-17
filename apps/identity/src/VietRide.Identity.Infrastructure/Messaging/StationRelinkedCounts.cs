namespace VietRide.Identity.Infrastructure.Messaging;

public sealed record StationRelinkedCounts
{
    public int OperatorMappings { get; init; }
    public int CollapsedOperatorMappings { get; init; }
    public int RouteOrigins { get; init; }
    public int RouteDestinations { get; init; }
    public int AlternativeRoutes { get; init; }
    public int ShuttleTrips { get; init; }
    public int FlattenedRedirects { get; init; }
}
