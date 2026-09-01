using System.Globalization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoongDirectionsShuttleRouteEstimator : IShuttleRouteEstimator
{
    private readonly GoongDirectionsClient directionsClient;

    public GoongDirectionsShuttleRouteEstimator(HttpClient httpClient, IConfiguration configuration)
    {
        var timeoutMs = int.TryParse(
            configuration["SHUTTLE_ROUTE_PREVIEW_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 3_000;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
        directionsClient = new GoongDirectionsClient(httpClient, configuration);
    }

    public async Task<TimeSpan?> EstimateDurationAsync(
        ShuttleRouteCoordinate origin,
        IReadOnlyList<ShuttleRouteCoordinate> destinations,
        CancellationToken cancellationToken = default)
    {
        if (!directionsClient.IsConfigured || destinations.Count == 0)
        {
            return null;
        }

        try
        {
            long totalDurationSeconds = 0;
            var targetOffset = 0;
            var chunkOrigin = ToGoong(origin);
            while (targetOffset < destinations.Count)
            {
                var chunk = destinations
                    .Skip(targetOffset)
                    .Take(directionsClient.MaxDestinationsPerRequest)
                    .ToArray();
                var legs = await directionsClient.GetLegsAsync(
                    chunkOrigin,
                    chunk.Select(ToGoong).ToArray(),
                    cancellationToken).ConfigureAwait(false);
                if (legs is null || legs.Count != chunk.Length)
                {
                    return null;
                }

                totalDurationSeconds = checked(
                    totalDurationSeconds + legs.Sum(leg => (long)leg.DurationSeconds));
                targetOffset += chunk.Length;
                if (targetOffset < destinations.Count)
                {
                    chunkOrigin = ToGoong(chunk[^1]);
                }
            }

            return TimeSpan.FromSeconds(totalDurationSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or System.Text.Json.JsonException
            or OverflowException)
        {
            return null;
        }
    }

    private static GoongCoordinate ToGoong(ShuttleRouteCoordinate coordinate) =>
        new(coordinate.Latitude, coordinate.Longitude);
}
