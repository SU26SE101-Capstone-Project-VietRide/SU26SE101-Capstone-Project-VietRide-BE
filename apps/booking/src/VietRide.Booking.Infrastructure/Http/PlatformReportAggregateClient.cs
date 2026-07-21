using System.Globalization;
using System.Net;
using System.Text.Json;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Http;

internal sealed class PlatformReportAggregateClient : IPlatformReportAggregateClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public PlatformReportAggregateClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<PlatformReportResult> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/v1/reports/platform/aggregate?from={Format(fromUtc)}&to={Format(toUtc)}");
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {_tokens.IssueToken("booking")}");

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new PlatformReportUnavailableException(exception);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new PlatformReportUnavailableException();
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                var element = document.RootElement;
                if (element.TryGetProperty("data", out var data))
                {
                    element = data;
                }

                return element.Deserialize<PlatformReportResult>(JsonOptions)
                    ?? throw new PlatformReportUnavailableException();
            }
            catch (JsonException exception)
            {
                throw new PlatformReportUnavailableException(exception);
            }
        }
    }

    private static string Format(DateTimeOffset value)
        => Uri.EscapeDataString(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
}
