namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public sealed record RepositionTravelTimeResult(
    bool IsAvailable,
    int? DurationMinutes,
    int? DistanceMeters,
    string? FailureMessage)
{
    public static RepositionTravelTimeResult Success(int durationMinutes, int distanceMeters) =>
        new(true, durationMinutes, distanceMeters, null);

    public static RepositionTravelTimeResult Unavailable(string message) =>
        new(false, null, null, message);
}

public interface IRepositionTravelTimeClient
{
    Task<RepositionTravelTimeResult> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default);
}
