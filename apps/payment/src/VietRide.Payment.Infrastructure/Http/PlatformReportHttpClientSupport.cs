using System.Globalization;
using System.Net;
using System.Text.Json;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Http;

internal static class PlatformReportHttpClientSupport
{
    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        IInternalJwtTokenProvider tokens,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {tokens.IssueToken("payment")}");
        return request;
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamUnavailableException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UpstreamUnavailableException(exception);
        }
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        bool propagateOverflow,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        if (propagateOverflow
            && response.StatusCode == HttpStatusCode.InternalServerError
            && await IsOverflowAsync(response, cancellationToken))
        {
            throw new PlatformReportValueOverflowException();
        }

        throw new UpstreamUnavailableException();
    }

    public static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new UpstreamUnavailableException(exception);
        }
    }

    public static bool TryGuid(JsonElement item, string propertyName, out Guid value)
    {
        value = default;
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.TryGetGuid(out value)
            && value != Guid.Empty;
    }

    public static bool TryInt64(JsonElement item, string propertyName, out long value)
    {
        value = default;
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    public static string Format(DateTimeOffset value)
        => Uri.EscapeDataString(
            value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

    private static async Task<bool> IsOverflowAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() == "REPORT_VALUE_OVERFLOW";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
