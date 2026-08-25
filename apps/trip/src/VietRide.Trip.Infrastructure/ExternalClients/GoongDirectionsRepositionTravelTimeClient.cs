using System.Globalization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoongDirectionsRepositionTravelTimeClient : IRepositionTravelTimeClient
{
    private readonly GoongDirectionsClient directionsClient;

    public GoongDirectionsRepositionTravelTimeClient(HttpClient httpClient, IConfiguration configuration)
    {
        var timeoutMs = int.TryParse(
            configuration["RESOURCE_TRAVEL_TIME_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 3_000;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
        directionsClient = new GoongDirectionsClient(httpClient, configuration);
    }

    public async Task<RepositionTravelTimeResult> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        if (!directionsClient.IsConfigured)
        {
            return RepositionTravelTimeResult.Unavailable("Goong Directions is not configured.");
        }

        try
        {
            var legs = await directionsClient.GetLegsAsync(
                new GoongCoordinate(originLatitude, originLongitude),
                [new GoongCoordinate(destinationLatitude, destinationLongitude)],
                cancellationToken).ConfigureAwait(false);
            if (legs is not { Count: 1 })
            {
                return RepositionTravelTimeResult.Unavailable(
                    "Goong Directions returned no usable duration.");
            }

            var durationMinutes = Math.Max(0, checked((int)Math.Ceiling(legs[0].DurationSeconds / 60d)));
            return RepositionTravelTimeResult.Success(durationMinutes, legs[0].DistanceMeters);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RepositionTravelTimeResult.Unavailable("Goong Directions timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            return RepositionTravelTimeResult.Unavailable("Goong Directions is unavailable.");
        }
    }
}
