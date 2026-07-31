using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Http;

internal sealed class IdentityFinancialProjectionClient : IIdentityFinancialProjectionClient
{
    private const int MaxBatchSize = 100;
    private readonly HttpClient _client;
    private readonly IInternalJwtTokenProvider _tokens;

    public IdentityFinancialProjectionClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        _client = client;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<IdentityFinancialOperator>> GetOperatorsAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(operatorIds, nameof(operatorIds));
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
            throw new UpstreamUnavailableException();

        var requested = operatorIds.ToHashSet();
        var seen = new HashSet<Guid>();
        var result = new List<IdentityFinancialOperator>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !requested.Contains(operatorId)
                || !seen.Add(operatorId)
                || !TryRequiredString(item, "operatorName", out var name)
                || !TryOptionalString(item, "logoUrl", out var logoUrl)
                || !TryOptionalString(item, "contactPhone", out var contactPhone))
            {
                throw new UpstreamUnavailableException();
            }

            result.Add(new IdentityFinancialOperator(operatorId, name!, logoUrl, contactPhone));
        }

        return result;
    }

    public async Task<IReadOnlyList<IdentityFinancialUser>> GetUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(userIds, nameof(userIds));
        var query = string.Join("&", userIds.Select(id => $"ids={id:D}"));
        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Get,
            $"internal/v1/users?{query}",
            _tokens);
        using var response = await PlatformReportHttpClientSupport.SendAsync(
            _client, request, cancellationToken);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(
            response, propagateOverflow: false, cancellationToken);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(
            response, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new UpstreamUnavailableException();

        var requested = userIds.ToHashSet();
        var seen = new HashSet<Guid>();
        var result = new List<IdentityFinancialUser>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "id", out var userId)
                || !requested.Contains(userId)
                || !seen.Add(userId)
                || !TryRequiredString(item, "displayName", out var displayName)
                || !TryRequiredString(item, "role", out var role)
                || !TryOptionalString(item, "email", out var email)
                || !item.TryGetProperty("deleted", out var deletedProperty)
                || deletedProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new UpstreamUnavailableException();
            }

            result.Add(new IdentityFinancialUser(
                userId,
                displayName!,
                email,
                role!,
                deletedProperty.GetBoolean()));
        }

        return result;
    }

    private static void ValidateIds(IReadOnlyList<Guid> ids, string parameterName)
    {
        if (ids.Count is 0 or > MaxBatchSize
            || ids.Any(id => id == Guid.Empty)
            || ids.Distinct().Count() != ids.Count)
        {
            throw new ArgumentException(
                $"IDs must be distinct, non-empty and limited to {MaxBatchSize}.",
                parameterName);
        }
    }

    private static bool TryRequiredString(JsonElement item, string name, out string? value)
    {
        value = null;
        if (!item.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryOptionalString(JsonElement item, string name, out string? value)
    {
        value = null;
        if (!item.TryGetProperty(name, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return true;
    }
}
