using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BookingShuttleCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.cancelled";
    private Guid? _eventId;
    private DateTimeOffset? _occurredAtOffset;

    public Guid? EventId
    {
        get => _eventId;
        init
        {
            _eventId = value;
            HasEventId = true;
        }
    }

    [JsonPropertyName("occurredAt")]
    public DateTimeOffset? OccurredAtOffset
    {
        get => _occurredAtOffset;
        init
        {
            _occurredAtOffset = value;
            HasOccurredAt = true;
        }
    }

    [JsonIgnore]
    public bool HasEventId { get; private init; }

    [JsonIgnore]
    public bool HasOccurredAt { get; private init; }
    [JsonRequired]
    public Guid? BookingId { get; init; }
    [JsonRequired]
    public Guid? UserId { get; init; }
    [JsonRequired]
    public long? RefundAmount { get; init; }
    [JsonRequired]
    public bool? RefundOverride { get; init; }
    [JsonRequired]
    public string? CancellationReason { get; init; }
    public string? BookingCode { get; init; }
    public IReadOnlyCollection<string>? TicketCodes { get; init; }
    public int? TicketCount { get; init; }

    Guid IIntegrationEvent.EventId => EventId ?? BookingId ?? Guid.Empty;
    DateTime IIntegrationEvent.OccurredAt => (OccurredAtOffset ?? DateTimeOffset.UnixEpoch).UtcDateTime;
    string IIntegrationEvent.EventType => EventType;

    public void Validate()
    {
        var canonical = HasEventId && EventId.HasValue && HasOccurredAt && OccurredAtOffset.HasValue;
        var legacy = !HasEventId && !HasOccurredAt;
        if (!canonical && !legacy
            || EventId == Guid.Empty
            || !BookingId.HasValue || BookingId.Value == Guid.Empty
            || !UserId.HasValue || UserId.Value == Guid.Empty
            || RefundAmount is null or < 0
            || RefundOverride is null
            || string.IsNullOrWhiteSpace(CancellationReason)
            || BookingCode is not null && string.IsNullOrWhiteSpace(BookingCode)
            || TicketCount is < 0
            || TicketCodes?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException("Booking-cancelled event is malformed.");
        }
    }
}
