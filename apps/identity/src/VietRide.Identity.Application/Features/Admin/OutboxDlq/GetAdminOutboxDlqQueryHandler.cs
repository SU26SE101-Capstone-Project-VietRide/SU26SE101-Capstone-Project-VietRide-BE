using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Admin.OutboxDlq;

public sealed class GetAdminOutboxDlqQueryHandler : IRequestHandler<GetAdminOutboxDlqQuery, AdminOutboxDlqResponseDto>
{
    private static readonly string[] AllServices = ["identity", "trip", "booking", "payment", "parcel", "tracking"];
    private static readonly Guid MaxGuid = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private readonly IAdminOutboxDlqRepository _localRepository;
    private readonly IAdminOutboxDlqSourceClient _sourceClient;
    private readonly ILogger<GetAdminOutboxDlqQueryHandler> _logger;

    public GetAdminOutboxDlqQueryHandler(
        IAdminOutboxDlqRepository localRepository,
        IAdminOutboxDlqSourceClient sourceClient,
        ILogger<GetAdminOutboxDlqQueryHandler> logger)
    {
        _localRepository = localRepository;
        _sourceClient = sourceClient;
        _logger = logger;
    }

    public async Task<AdminOutboxDlqResponseDto> Handle(GetAdminOutboxDlqQuery request, CancellationToken cancellationToken)
    {
        var descending = !string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var cursor = DecodeCursor(request.Cursor);
        var services = request.Service is null
            ? AllServices
            : [request.Service.ToLowerInvariant()];
        var unavailable = new List<string>();
        var allItems = new List<AdminOutboxDlqItemDto>();

        foreach (var service in services)
        {
            try
            {
                var sourceCursor = GetSourceCursor(service, cursor, descending);
                var items = await ReadSourceWithLookaheadAsync(
                    service,
                    request.EventType,
                    request.PageSize,
                    sourceCursor.TerminalAt,
                    sourceCursor.EventId,
                    descending,
                    cancellationToken);
                allItems.AddRange(items);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    exception,
                    "Outbox DLQ source {SourceService} is unavailable.",
                    service);
                unavailable.Add(service);
            }
        }

        var ordered = descending
            ? allItems.OrderByDescending(item => item.TerminalAt).ThenByDescending(item => item.Service, StringComparer.Ordinal).ThenByDescending(item => item.EventId)
            : allItems.OrderBy(item => item.TerminalAt).ThenBy(item => item.Service, StringComparer.Ordinal).ThenBy(item => item.EventId);

        if (cursor is not null)
        {
            ordered = descending
                ? ordered.Where(item => IsBefore(item, cursor)).OrderByDescending(item => item.TerminalAt).ThenByDescending(item => item.Service, StringComparer.Ordinal).ThenByDescending(item => item.EventId)
                : ordered.Where(item => IsAfter(item, cursor)).OrderBy(item => item.TerminalAt).ThenBy(item => item.Service, StringComparer.Ordinal).ThenBy(item => item.EventId);
        }

        var page = ordered.Take(request.PageSize).ToArray();
        var hasMore = ordered.Skip(request.PageSize).Any();
        var nextCursor = hasMore && page.Length > 0 ? EncodeCursor(page[^1]) : null;
        var publicItems = page.Select(item => new AdminOutboxDlqResponseItemDto(
            item.Service, item.EventId, item.EventType, item.Payload, item.RetryCount,
            item.LastError, item.CreatedAt, item.TerminalAt)).ToArray();

        return new AdminOutboxDlqResponseDto(publicItems, nextCursor, unavailable.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsBefore(AdminOutboxDlqItemDto item, Cursor cursor)
        => item.TerminalAt < cursor.TerminalAt
            || (item.TerminalAt == cursor.TerminalAt && string.CompareOrdinal(item.Service, cursor.Service) < 0)
            || (item.TerminalAt == cursor.TerminalAt && item.Service == cursor.Service && item.EventId.CompareTo(cursor.EventId) < 0);

    private static bool IsAfter(AdminOutboxDlqItemDto item, Cursor cursor)
        => item.TerminalAt > cursor.TerminalAt
            || (item.TerminalAt == cursor.TerminalAt && string.CompareOrdinal(item.Service, cursor.Service) > 0)
            || (item.TerminalAt == cursor.TerminalAt && item.Service == cursor.Service && item.EventId.CompareTo(cursor.EventId) > 0);

    private static string EncodeCursor(AdminOutboxDlqItemDto item)
    {
        var json = JsonSerializer.Serialize(new Cursor(item.Service, item.TerminalAt, item.EventId));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadSourceWithLookaheadAsync(
        string service,
        string? eventType,
        int publicPageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterEventId,
        bool descending,
        CancellationToken cancellationToken)
    {
        var fetchSize = Math.Min(publicPageSize + 1, 100);
        var items = await ReadSourceAsync(
            service,
            eventType,
            fetchSize,
            afterTerminalAt,
            afterEventId,
            descending,
            cancellationToken);

        if (publicPageSize < 100 || items.Count < 100)
            return items;

        var last = items[^1];
        var probe = await ReadSourceAsync(
            service,
            eventType,
            1,
            last.TerminalAt,
            last.EventId,
            descending,
            cancellationToken);
        return items.Concat(probe).ToArray();
    }

    private Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadSourceAsync(
        string service,
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterEventId,
        bool descending,
        CancellationToken cancellationToken)
        => service == "identity"
            ? _localRepository.ReadAsync(
                eventType,
                pageSize,
                afterTerminalAt,
                afterEventId,
                descending,
                cancellationToken)
            : _sourceClient.ReadAsync(
                service,
                eventType,
                pageSize,
                afterTerminalAt,
                afterEventId,
                descending,
                cancellationToken);

    private static (DateTimeOffset? TerminalAt, Guid? EventId) GetSourceCursor(
        string service,
        Cursor? cursor,
        bool descending)
    {
        if (cursor is null)
            return (null, null);

        var serviceComparison = string.CompareOrdinal(service, cursor.Service);
        if (serviceComparison == 0)
            return (cursor.TerminalAt, cursor.EventId);

        if (descending && serviceComparison < 0)
            return (cursor.TerminalAt, MaxGuid);

        if (descending)
            return (cursor.TerminalAt, Guid.Empty);

        if (!descending && serviceComparison > 0)
            return (cursor.TerminalAt, Guid.Empty);

        return (cursor.TerminalAt, MaxGuid);
    }

    private static Cursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(normalized));
        }
        catch (Exception) when (value.Length <= 512)
        {
            throw new ArgumentException("cursor is invalid.", nameof(value));
        }
    }

    private sealed record Cursor(string Service, DateTimeOffset TerminalAt, Guid EventId);
}
