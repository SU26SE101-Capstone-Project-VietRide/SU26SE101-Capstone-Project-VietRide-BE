namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public abstract record ShuttleDistanceOutcome
{
    private ShuttleDistanceOutcome() { }

    public sealed record Success(int DistanceMeters) : ShuttleDistanceOutcome;

    public sealed record Unavailable(string Message) : ShuttleDistanceOutcome;
}

public interface IShuttleDistanceClient
{
    Task<ShuttleDistanceOutcome> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken);
}
