using System.Globalization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoongDirectionsShuttleDistanceClient : IShuttleDistanceClient
{
    private readonly GoongDirectionsClient directionsClient;

    public GoongDirectionsShuttleDistanceClient(HttpClient httpClient, IConfiguration configuration)
    {
        var timeoutMs = int.TryParse(
            configuration["TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 1_500;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
        directionsClient = new GoongDirectionsClient(httpClient, configuration);
    }

    public async Task<ShuttleDistanceOutcome> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken)
    {
        if (!directionsClient.IsConfigured)
        {
            return new ShuttleDistanceOutcome.Unavailable("Goong Directions is not configured.");
        }

        try
        {
            var legs = await directionsClient.GetLegsAsync(
                new GoongCoordinate(originLatitude, originLongitude),
                [new GoongCoordinate(destinationLatitude, destinationLongitude)],
                cancellationToken).ConfigureAwait(false);
            return legs is { Count: 1 }
                ? new ShuttleDistanceOutcome.Success(legs[0].DistanceMeters)
                : new ShuttleDistanceOutcome.Unavailable("Goong Directions returned no usable distance.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ShuttleDistanceOutcome.Unavailable("Goong Directions timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            return new ShuttleDistanceOutcome.Unavailable("Goong Directions is unavailable.");
        }
    }
}
