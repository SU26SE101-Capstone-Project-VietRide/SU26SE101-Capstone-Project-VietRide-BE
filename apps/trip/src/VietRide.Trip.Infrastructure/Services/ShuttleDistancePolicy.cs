namespace VietRide.Trip.Infrastructure.Services;

internal static class ShuttleDistancePolicy
{
    public const int DefaultMaxDistanceKm = 10;
    public const int DefaultMaxDistanceMeters = DefaultMaxDistanceKm * 1_000;

    public static bool IsWithinDefaultLimit(int distanceMeters)
        => distanceMeters is >= 0 and <= DefaultMaxDistanceMeters;
}
