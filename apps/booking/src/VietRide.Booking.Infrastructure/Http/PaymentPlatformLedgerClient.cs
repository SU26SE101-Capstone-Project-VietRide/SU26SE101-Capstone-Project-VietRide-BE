using System.Text.Json;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Http;

internal sealed class PaymentPlatformLedgerClient : IPaymentPlatformLedgerClient
{
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public PaymentPlatformLedgerClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<PlatformLedgerReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Get,
            $"internal/v1/reports/platform/ledger?from={PlatformReportHttpClientSupport.Format(fromUtc)}&to={PlatformReportHttpClientSupport.Format(toUtc)}",
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

        var result = new List<PlatformLedgerReportItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !PlatformReportHttpClientSupport.TryInt64(item, "bookingRevenueVnd", out var bookingRevenue)
                || !PlatformReportHttpClientSupport.TryInt64(item, "parcelRevenueVnd", out var parcelRevenue)
                || !seen.Add(operatorId))
            {
                throw new PlatformReportUnavailableException();
            }

            result.Add(new PlatformLedgerReportItem(operatorId, bookingRevenue, parcelRevenue));
        }

        return result;
    }
}
