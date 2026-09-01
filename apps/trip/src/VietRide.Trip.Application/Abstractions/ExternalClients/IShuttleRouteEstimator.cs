namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public readonly record struct ShuttleRouteCoordinate(decimal Latitude, decimal Longitude);

public interface IShuttleRouteEstimator
{
    Task<TimeSpan?> EstimateDurationAsync(
        ShuttleRouteCoordinate origin,
        IReadOnlyList<ShuttleRouteCoordinate> destinations,
        CancellationToken cancellationToken = default);
}
