using System.Globalization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Infrastructure.Services;

public sealed class TripBoardingWindowProvider : ITripBoardingWindowProvider
{
    public const string ConfigurationKey = "TRIP_MANUAL_BOARDING_EARLY_WINDOW_MINUTES";
    public const int DefaultMinutes = 180;

    private TripBoardingWindowProvider(TimeSpan manualEarlyWindow)
    {
        ManualEarlyWindow = manualEarlyWindow;
    }

    public TimeSpan ManualEarlyWindow { get; }

    public static TripBoardingWindowProvider Create(IConfiguration configuration)
    {
        var rawValue = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new TripBoardingWindowProvider(TimeSpan.FromMinutes(DefaultMinutes));
        }

        if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes <= 0)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be a positive integer number of minutes.");
        }

        return new TripBoardingWindowProvider(TimeSpan.FromMinutes(minutes));
    }
}
