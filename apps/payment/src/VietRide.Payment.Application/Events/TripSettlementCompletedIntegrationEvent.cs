using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class TripSettlementCompletedIntegrationEvent(
    Guid settlementId,
    Guid tripId,
    Guid operatorId,
    long netAmount,
    OperatorTripSettlementMethod settlementMethod,
    DateTimeOffset settledAt) : IntegrationEventBase
{
    public override string EventType => "payment.trip_settlement.completed";
    public Guid SettlementId { get; } = settlementId;
    public Guid TripId { get; } = tripId;
    public Guid OperatorId { get; } = operatorId;
    public long NetAmount { get; } = netAmount;
    public string SettlementMethod { get; } = settlementMethod.ToString();
    public DateTimeOffset SettledAt { get; } = settledAt;
}
