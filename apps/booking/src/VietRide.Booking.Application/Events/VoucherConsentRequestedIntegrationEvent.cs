using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class VoucherConsentRequestedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.voucher.consent_requested";

    public VoucherConsentRequestedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid voucherId,
        Guid operatorId,
        string voucherCode,
        string voucherType,
        long voucherValue)
        : base(eventId, occurredAt.UtcDateTime)
    {
        VoucherId = voucherId;
        OperatorId = operatorId;
        VoucherCode = voucherCode;
        VoucherType = voucherType;
        VoucherValue = voucherValue;
    }

    [JsonPropertyName("voucherId")]
    public Guid VoucherId { get; }

    [JsonPropertyName("operatorId")]
    public Guid OperatorId { get; }

    [JsonPropertyName("voucherCode")]
    public string VoucherCode { get; }

    [JsonPropertyName("voucherType")]
    public string VoucherType { get; }

    [JsonPropertyName("voucherValue")]
    public long VoucherValue { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
