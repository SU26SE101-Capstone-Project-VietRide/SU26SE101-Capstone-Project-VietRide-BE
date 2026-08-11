using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoogleRoutesRepositionTravelTimeClient : IRepositionTravelTimeClient
{
    private readonly HttpClient httpClient;
    private readonly bool enabled;
    private readonly string apiKey;

    public GoogleRoutesRepositionTravelTimeClient(HttpClient httpClient, IConfiguration configuration)
    {
        this.httpClient = httpClient;
        enabled = bool.TryParse(configuration["GOOGLE_ROUTES_ENABLED"], out var configured) && configured;
        apiKey = configuration["GOOGLE_ROUTES_API_KEY"] ?? string.Empty;
        var timeoutMs = int.TryParse(
            configuration["RESOURCE_TRAVEL_TIME_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 3_000;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
    }

    public async Task<RepositionTravelTimeResult> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        if (!enabled || string.IsNullOrWhiteSpace(apiKey))
        {
            return RepositionTravelTimeResult.Unavailable("Google Routes is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new
            {
                origin = ToWaypoint(originLatitude, originLongitude),
                destination = ToWaypoint(destinationLatitude, destinationLongitude),
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_UNAWARE",
                computeAlternativeRoutes = false,
                units = "METRIC",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", "routes.duration,routes.distanceMeters");

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return RepositionTravelTimeResult.Unavailable(
                    $"Google Routes returned {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<RoutesResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var route = payload?.Routes?.FirstOrDefault();
            if (route?.DistanceMeters is not int distanceMeters
                || !TryParseDurationMinutes(route.Duration, out var durationMinutes))
            {
                return RepositionTravelTimeResult.Unavailable("Google Routes returned no usable duration.");
            }

            return RepositionTravelTimeResult.Success(durationMinutes, distanceMeters);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RepositionTravelTimeResult.Unavailable("Google Routes timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return RepositionTravelTimeResult.Unavailable(exception.Message);
        }
        catch (JsonException exception)
        {
            return RepositionTravelTimeResult.Unavailable(exception.Message);
        }
    }

    private static object ToWaypoint(decimal latitude, decimal longitude) => new
    {
        location = new
        {
            latLng = new { latitude, longitude },
        },
    };

    private static bool TryParseDurationMinutes(string? value, out int durationMinutes)
    {
        durationMinutes = default;
        if (string.IsNullOrWhiteSpace(value)
            || !value.EndsWith('s')
            || !double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0)
        {
            return false;
        }

        durationMinutes = Math.Max(0, checked((int)Math.Ceiling(seconds / 60d)));
        return true;
    }

    private sealed record RoutesResponse(
        [property: JsonPropertyName("routes")] IReadOnlyList<RouteResponse>? Routes);

    private sealed record RouteResponse(
        [property: JsonPropertyName("duration")] string? Duration,
        [property: JsonPropertyName("distanceMeters")] int? DistanceMeters);
}
