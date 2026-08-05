using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoogleRoutesShuttleDistanceClient : IShuttleDistanceClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly string _apiKey;

    public GoogleRoutesShuttleDistanceClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _enabled = bool.TryParse(configuration["GOOGLE_ROUTES_ENABLED"], out var enabled) && enabled;
        _apiKey = configuration["GOOGLE_ROUTES_API_KEY"] ?? string.Empty;
        var timeoutMs = int.TryParse(configuration["TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS"], out var configured)
            ? configured
            : 1_500;
        _httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
    }

    public async Task<ShuttleDistanceOutcome> CalculateAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
        {
            return new ShuttleDistanceOutcome.Unavailable("Google Routes is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new
            {
                origin = new { location = new { latLng = new { latitude = originLatitude, longitude = originLongitude } } },
                destination = new { location = new { latLng = new { latitude = destinationLatitude, longitude = destinationLongitude } } },
                travelMode = "DRIVE",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _apiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", "routes.distanceMeters");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ShuttleDistanceOutcome.Unavailable($"Google Routes returned {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<RoutesResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var distance = payload?.Routes?.FirstOrDefault()?.DistanceMeters;
            return distance is >= 0
                ? new ShuttleDistanceOutcome.Success(distance.Value)
                : new ShuttleDistanceOutcome.Unavailable("Google Routes returned no distance.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ShuttleDistanceOutcome.Unavailable("Google Routes timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new ShuttleDistanceOutcome.Unavailable(exception.Message);
        }
        catch (JsonException exception)
        {
            return new ShuttleDistanceOutcome.Unavailable(exception.Message);
        }
    }

    private sealed record RoutesResponse(
        [property: JsonPropertyName("routes")] IReadOnlyList<RouteResponse>? Routes);

    private sealed record RouteResponse(
        [property: JsonPropertyName("distanceMeters")] int? DistanceMeters);
}
