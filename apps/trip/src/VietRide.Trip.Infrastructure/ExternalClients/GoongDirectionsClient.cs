using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoongDirectionsClient
{
    private const int DefaultMaxDestinationsPerRequest = 10;
    private const decimal EndpointToleranceDegrees = 0.02m;
    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public GoongDirectionsClient(HttpClient httpClient, IConfiguration configuration)
    {
        this.httpClient = httpClient;
        IsConfigured = string.Equals(
                configuration["ROUTING_PROVIDER"],
                "GOONG",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(configuration["GOONG_API_KEY"]);
        apiKey = configuration["GOONG_API_KEY"] ?? string.Empty;
        MaxDestinationsPerRequest = int.TryParse(
            configuration["GOONG_MAX_DESTINATIONS_PER_REQUEST"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredMax)
            ? Math.Max(1, configuredMax)
            : DefaultMaxDestinationsPerRequest;
    }

    public bool IsConfigured { get; }

    public int MaxDestinationsPerRequest { get; }

    public async Task<IReadOnlyList<GoongDirectionLeg>?> GetLegsAsync(
        GoongCoordinate origin,
        IReadOnlyList<GoongCoordinate> destinations,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured
            || destinations.Count == 0
            || destinations.Count > MaxDestinationsPerRequest)
        {
            return null;
        }

        var originValue = FormatCoordinate(origin);
        var destinationValue = string.Join(';', destinations.Select(FormatCoordinate));
        var requestTarget = $"v2/direction?origin={Uri.EscapeDataString(originValue)}"
            + $"&destination={Uri.EscapeDataString(destinationValue)}"
            + "&vehicle=car&alternatives=false"
            + $"&api_key={Uri.EscapeDataString(apiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestTarget);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<DirectionsResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var legs = payload?.Routes?.FirstOrDefault()?.Legs;
        if (legs is null || legs.Count != destinations.Count)
        {
            return null;
        }

        var result = new List<GoongDirectionLeg>(legs.Count);
        var expectedStart = origin;
        for (var index = 0; index < legs.Count; index++)
        {
            var leg = legs[index];
            var expectedEnd = destinations[index];
            if (!TryGetNonNegativeInt(leg.Distance?.Value, out var distanceMeters)
                || !TryGetNonNegativeInt(leg.Duration?.Value, out var durationSeconds)
                || !MatchesLegEndpoints(leg.StartLocation, leg.EndLocation, expectedStart, expectedEnd))
            {
                return null;
            }

            result.Add(new GoongDirectionLeg(distanceMeters, durationSeconds));
            expectedStart = expectedEnd;
        }

        return result;
    }

    private static string FormatCoordinate(GoongCoordinate coordinate) => string.Create(
        CultureInfo.InvariantCulture,
        $"{coordinate.Latitude},{coordinate.Longitude}");

    private static bool TryGetNonNegativeInt(double? value, out int parsed)
    {
        parsed = default;
        if (!value.HasValue
            || !double.IsFinite(value.Value)
            || value.Value < 0
            || value.Value > int.MaxValue
            || value.Value != Math.Truncate(value.Value))
        {
            return false;
        }

        parsed = checked((int)value.Value);
        return true;
    }

    private static bool MatchesLegEndpoints(
        LocationResponse? actualStart,
        LocationResponse? actualEnd,
        GoongCoordinate expectedStart,
        GoongCoordinate expectedEnd)
    {
        if (actualStart?.Latitude is not decimal startLatitude
            || actualStart.Longitude is not decimal startLongitude
            || actualEnd?.Latitude is not decimal endLatitude
            || actualEnd.Longitude is not decimal endLongitude)
        {
            return false;
        }

        var startError = CoordinateError(startLatitude, startLongitude, expectedStart);
        var endError = CoordinateError(endLatitude, endLongitude, expectedEnd);
        if (startError > EndpointToleranceDegrees || endError > EndpointToleranceDegrees)
        {
            return false;
        }

        var reversedError = CoordinateError(startLatitude, startLongitude, expectedEnd)
            + CoordinateError(endLatitude, endLongitude, expectedStart);
        return expectedStart == expectedEnd || startError + endError < reversedError;
    }

    private static decimal CoordinateError(
        decimal latitude,
        decimal longitude,
        GoongCoordinate expected) =>
        Math.Max(
            Math.Abs(latitude - expected.Latitude),
            Math.Abs(longitude - expected.Longitude));

    private sealed record DirectionsResponse(
        [property: JsonPropertyName("routes")] IReadOnlyList<RouteResponse>? Routes);

    private sealed record RouteResponse(
        [property: JsonPropertyName("legs")] IReadOnlyList<LegResponse>? Legs);

    private sealed record LegResponse(
        [property: JsonPropertyName("distance")] ValueResponse? Distance,
        [property: JsonPropertyName("duration")] ValueResponse? Duration,
        [property: JsonPropertyName("start_location")] LocationResponse? StartLocation,
        [property: JsonPropertyName("end_location")] LocationResponse? EndLocation);

    private sealed record ValueResponse(
        [property: JsonPropertyName("value")] double? Value);

    private sealed record LocationResponse(
        [property: JsonPropertyName("lat")] decimal? Latitude,
        [property: JsonPropertyName("lng")] decimal? Longitude);
}

internal readonly record struct GoongCoordinate(decimal Latitude, decimal Longitude);

internal readonly record struct GoongDirectionLeg(int DistanceMeters, int DurationSeconds);
