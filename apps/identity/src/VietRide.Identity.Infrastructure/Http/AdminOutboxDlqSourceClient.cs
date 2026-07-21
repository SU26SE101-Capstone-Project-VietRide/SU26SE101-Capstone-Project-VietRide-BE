using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Features.Admin.OutboxDlq;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.Infrastructure.Http;

public sealed class AdminOutboxDlqSourceClient : IAdminOutboxDlqSourceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> SupportedServices = new HashSet<string>(StringComparer.Ordinal)
    {
        "trip",
        "booking",
        "payment",
        "parcel",
        "tracking",
    };

    private readonly IReadOnlyDictionary<string, HttpClient> _clients;

    public AdminOutboxDlqSourceClient(IHttpClientFactory httpClientFactory)
    {
        _clients = SupportedServices.ToDictionary(
            service => service,
            service => httpClientFactory.CreateClient($"admin-outbox-dlq-{service}"),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadAsync(
        string service,
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(service, out var client))
            throw new ArgumentException($"Unsupported DLQ service '{service}'.", nameof(service));

        var query = new List<string>
        {
            $"pageSize={Math.Clamp(pageSize, 1, 100)}",
            $"sortDir={(descending ? "desc" : "asc")}",
        };
        if (!string.IsNullOrWhiteSpace(eventType))
            query.Add($"eventType={Uri.EscapeDataString(eventType.Trim())}");
        if (afterTerminalAt.HasValue)
            query.Add($"afterTerminalAt={Uri.EscapeDataString(afterTerminalAt.Value.ToString("O"))}");
        if (afterId.HasValue)
            query.Add($"afterId={afterId.Value:D}");

        using var response = await client.GetAsync(
            $"/internal/v1/outbox/dlq?{string.Join("&", query)}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<List<OutboxDlqReadItem>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? [];

        return rows.Select(row => new AdminOutboxDlqItemDto(
            service,
            row.EventId,
            row.EventType,
            row.Payload,
            row.RetryCount,
            row.LastError,
            row.CreatedAt,
            row.TerminalAt)).ToArray();
    }
}
