using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BookingCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.cancelled";

    private Guid? _eventId;
    private DateTimeOffset? _occurredAtOffset;
    private Guid? _tripId;
    private string? _previousStatus;
    private IReadOnlyCollection<string>? _seatNumbers;

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

    [JsonIgnore]
    public bool HasTripId { get; private init; }

    [JsonIgnore]
    public bool HasPreviousStatus { get; private init; }

    [JsonIgnore]
    public bool HasSeatNumbers { get; private init; }

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

    [JsonPropertyName("tripId")]
    public Guid? TripId
    {
        get => _tripId;
        init
        {
            _tripId = value;
            HasTripId = true;
        }
    }

    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus
    {
        get => _previousStatus;
        init
        {
            _previousStatus = value;
            HasPreviousStatus = true;
        }
    }

    [JsonPropertyName("seatNumbers")]
    public IReadOnlyCollection<string>? SeatNumbers
    {
        get => _seatNumbers;
        init
        {
            _seatNumbers = value;
            HasSeatNumbers = true;
        }
    }

    Guid IIntegrationEvent.EventId => EventId ?? BookingId ?? Guid.Empty;
    DateTime IIntegrationEvent.OccurredAt => (OccurredAtOffset ?? DateTimeOffset.UnixEpoch).UtcDateTime;
    string IIntegrationEvent.EventType => EventType;

    public void Validate()
    {
        var canonical = HasEventId && EventId.HasValue && HasOccurredAt && OccurredAtOffset.HasValue;
        var legacy = !HasEventId && !HasOccurredAt;
        var operational = HasTripId && HasPreviousStatus && HasSeatNumbers;
        var preOperational = !HasTripId && !HasPreviousStatus && !HasSeatNumbers;
        var supportedShape = legacy && preOperational
            || canonical && preOperational
            || canonical && operational;
        if (!supportedShape
            || EventId == Guid.Empty
            || !BookingId.HasValue || BookingId.Value == Guid.Empty
            || !UserId.HasValue || UserId.Value == Guid.Empty
            || RefundAmount is null or < 0
            || RefundOverride is null
            || string.IsNullOrWhiteSpace(CancellationReason)
            || BookingCode is not null && string.IsNullOrWhiteSpace(BookingCode)
            || TicketCount is < 0
            || TicketCodes?.Any(string.IsNullOrWhiteSpace) == true
            || operational && (!TripId.HasValue || TripId.Value == Guid.Empty
                || PreviousStatus is not ("PENDING_PAYMENT" or "CONFIRMED")
                || SeatNumbers is null
                || SeatNumbers.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("Booking-cancelled event is malformed.");
        }
    }
}
