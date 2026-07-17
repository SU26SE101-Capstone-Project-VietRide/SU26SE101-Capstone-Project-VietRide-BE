using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Http;

internal sealed class IdentityOperatorSummaryClient : IIdentityOperatorSummaryClient
{
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public IdentityOperatorSummaryClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default)
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
        using var response = await PlatformReportHttpClientSupport.SendAsync(
            _client, request, cancellationToken);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(
            response, propagateOverflow: false, cancellationToken);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(
            response, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new UpstreamUnavailableException();
        }

        var result = new List<OperatorSummaryItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !item.TryGetProperty("operatorName", out var nameProperty)
                || nameProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameProperty.GetString())
                || !seen.Add(operatorId))
            {
                throw new UpstreamUnavailableException();
            }

            result.Add(new OperatorSummaryItem(operatorId, nameProperty.GetString()!));
        }

        return result;
    }
}
