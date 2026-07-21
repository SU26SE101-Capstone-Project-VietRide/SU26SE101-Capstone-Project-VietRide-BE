using System.Text.Json;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Http;

internal sealed class ParcelPlatformReportClient : IParcelPlatformReportClient
{
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public ParcelPlatformReportClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<ParcelPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Get,
            $"internal/v1/reports/platform/parcels?from={PlatformReportHttpClientSupport.Format(fromUtc)}&to={PlatformReportHttpClientSupport.Format(toUtc)}",
            _tokens);
        using var response = await PlatformReportHttpClientSupport.SendAsync(_client, request, ct)
            .ConfigureAwait(false);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(response, true, ct).ConfigureAwait(false);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(response, ct).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new PlatformReportUnavailableException();
        }

        var result = new List<ParcelPlatformReportItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !PlatformReportHttpClientSupport.TryInt64(item, "deliveredParcelCount", out var count)
                || !PlatformReportHttpClientSupport.TryInt64(item, "parcelRevenueVnd", out var revenue)
                || count < 0
                || !seen.Add(operatorId))
            {
                throw new PlatformReportUnavailableException();
            }

            result.Add(new ParcelPlatformReportItem(operatorId, count, revenue));
        }

        return result;
    }
}
