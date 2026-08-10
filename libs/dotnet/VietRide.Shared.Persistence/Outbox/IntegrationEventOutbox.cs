using System.Text.Json;
using System.Text.Json.Nodes;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Shared.Persistence.Outbox;

/// <summary>
/// Persistence-side implementation of <see cref="IIntegrationEventOutbox"/>.
/// Maps the string-based application seam onto an <see cref="OutboxEvent"/> row
/// and enlists it in the ambient EF transaction via <see cref="IOutboxStore.AddAsync"/>.
/// The row id, JSON <c>eventId</c>, and broker MessageId are kept identical.
/// </summary>
public sealed class IntegrationEventOutbox : IIntegrationEventOutbox
{
    private readonly IOutboxStore _store;

    public IntegrationEventOutbox(IOutboxStore store)
    {
        _store = store;
    }

    public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
    {
        var normalized = NormalizePayloadIdentity(payloadJson, suppliedEventId: null);
        return AddAsync(normalized.EventId, eventType, normalized.PayloadJson, ct);
    }

    public Task EnqueueAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id must not be empty.", nameof(eventId));
        }

        var normalized = NormalizePayloadIdentity(payloadJson, eventId);
        return AddAsync(eventId, eventType, normalized.PayloadJson, ct);
    }

    private Task AddAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        CancellationToken ct)
    {

        // Status defaults to PENDING and RetryCount to 0 (entity initializers);
        // CreatedAt is stamped by OutboxStore.AddAsync from IClock.
        var outboxEvent = new OutboxEvent
        {
            Id = eventId,
            EventType = eventType,
            Payload = payloadJson,
        };

        return _store.AddAsync(outboxEvent, ct);
    }

    private static NormalizedPayload NormalizePayloadIdentity(
        string payloadJson,
        Guid? suppliedEventId)
    {
        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(payloadJson) as JsonObject
                ?? throw new JsonException("Integration-event payload must be a JSON object.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new ArgumentException(
                "Integration-event payload must be a valid JSON object.",
                nameof(payloadJson),
                exception);
        }

        var eventIdProperties = payload
            .Where(property => string.Equals(
                property.Key,
                "eventId",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (eventIdProperties.Length > 1)
        {
            throw new ArgumentException(
                "Integration-event payload contains ambiguous eventId properties.",
                nameof(payloadJson));
        }

        if (eventIdProperties.Length == 1)
        {
            var value = eventIdProperties[0].Value;
            if (value is not JsonValue jsonValue
                || !jsonValue.TryGetValue<string>(out var rawEventId)
                || !Guid.TryParse(rawEventId, out var payloadEventId)
                || payloadEventId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Integration-event payload eventId must be a non-empty UUID.",
                    nameof(payloadJson));
            }

            if (suppliedEventId.HasValue && suppliedEventId.Value != payloadEventId)
            {
                throw new ArgumentException(
                    "Integration-event payload eventId does not match the supplied event identity.",
                    nameof(payloadJson));
            }

            return new NormalizedPayload(payloadEventId, UtcJson.NormalizeInstants(payloadJson));
        }

        var canonicalEventId = suppliedEventId ?? Guid.NewGuid();
        payload["eventId"] = canonicalEventId;
        return new NormalizedPayload(canonicalEventId, UtcJson.NormalizeInstants(payload.ToJsonString()));
    }

    private sealed record NormalizedPayload(Guid EventId, string PayloadJson);
}
