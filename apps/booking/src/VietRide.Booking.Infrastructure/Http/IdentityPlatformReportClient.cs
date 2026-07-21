using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Http;

internal sealed class IdentityPlatformReportClient : IIdentityPlatformReportClient
{
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public IdentityPlatformReportClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken ct = default)
    {
        if (operatorIds.Count > 500
            || operatorIds.Any(id => id == Guid.Empty)
            || operatorIds.Distinct().Count() != operatorIds.Count)
        {
            throw new ArgumentException(
                "Operator IDs must be distinct, non-empty and limited to 500.",
                nameof(operatorIds));
        }

        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Post,
            "internal/v1/operators/summaries/batch",
            _tokens,
            JsonContent.Create(new { operatorIds }));
        using var response = await PlatformReportHttpClientSupport.SendAsync(_client, request, ct)
            .ConfigureAwait(false);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(response, false, ct).ConfigureAwait(false);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(response, ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new PlatformReportUnavailableException();
        }

        var result = new List<OperatorSummaryItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !item.TryGetProperty("operatorName", out var name)
                || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString())
                || !seen.Add(operatorId))
            {
                throw new PlatformReportUnavailableException();
            }

            result.Add(new OperatorSummaryItem(operatorId, name.GetString()!));
        }

        return result;
    }
}
