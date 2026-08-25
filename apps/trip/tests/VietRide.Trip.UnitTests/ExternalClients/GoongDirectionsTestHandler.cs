using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VietRide.Trip.UnitTests.ExternalClients;

internal sealed class GoongDirectionsTestHandler(
    Func<GoongDirectionsRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    public List<GoongDirectionsRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        var captured = Capture(request);
        Requests.Add(captured);
        return Task.FromResult(respond(captured));
    }

    public static HttpResponseMessage Success(
        GoongDirectionsRequest request,
        Func<int, double>? distanceFactory = null,
        Func<int, double>? durationFactory = null,
        int? legCount = null,
        bool reverseFirstLeg = false)
    {
        var count = legCount ?? request.Destinations.Count;
        var legs = Enumerable.Range(0, count)
            .Select(index =>
            {
                var start = index == 0
                    ? request.Origin
                    : request.Destinations[Math.Min(index - 1, request.Destinations.Count - 1)];
                var end = request.Destinations[Math.Min(index, request.Destinations.Count - 1)];
                if (index == 0 && reverseFirstLeg)
                {
                    (start, end) = (end, start);
                }

                return new
                {
                    distance = new { value = distanceFactory?.Invoke(index) ?? 1_000d },
                    duration = new { value = durationFactory?.Invoke(index) ?? 60d },
                    start_location = new { lat = start.Latitude, lng = start.Longitude },
                    end_location = new { lat = end.Latitude, lng = end.Longitude },
                };
            })
            .ToArray();
        return Raw(HttpStatusCode.OK, JsonSerializer.Serialize(new { routes = new[] { new { legs } } }));
    }

    public static HttpResponseMessage Raw(HttpStatusCode statusCode, string content = "") => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static GoongDirectionsRequest Capture(HttpRequestMessage request)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
        var query = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : string.Empty),
                StringComparer.Ordinal);
        var origin = ParseCoordinate(query["origin"]);
        var destinations = query["destination"]
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseCoordinate)
            .ToArray();
        return new GoongDirectionsRequest(
            request.Method,
            uri.AbsolutePath,
            origin,
            destinations,
            query["vehicle"],
            query["alternatives"],
            query["api_key"]);
    }

    private static GoongTestCoordinate ParseCoordinate(string value)
    {
        var parts = value.Split(',', 2);
        return new GoongTestCoordinate(
            decimal.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
            decimal.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture));
    }
}

internal sealed record GoongDirectionsRequest(
    HttpMethod Method,
    string Path,
    GoongTestCoordinate Origin,
    IReadOnlyList<GoongTestCoordinate> Destinations,
    string Vehicle,
    string Alternatives,
    string ApiKey);

internal readonly record struct GoongTestCoordinate(decimal Latitude, decimal Longitude);

internal sealed class GoongDelayedHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages => messages;

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(messages);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}
