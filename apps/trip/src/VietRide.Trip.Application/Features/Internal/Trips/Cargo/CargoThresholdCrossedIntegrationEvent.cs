using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

/// <summary>
/// Published when a Trip's loaded cargo crosses the canonical 80% threshold.
/// </summary>
public sealed class CargoThresholdCrossedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.cargo.threshold_crossed";

    public CargoThresholdCrossedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        decimal loadedWeightKg,
        decimal maxCargoWeightKg,
        decimal percentFull)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        OperatorId = operatorId;
        LoadedWeightKg = loadedWeightKg;
        MaxCargoWeightKg = maxCargoWeightKg;
        PercentFull = percentFull;
    }

    public Guid TripId { get; }

    public Guid OperatorId { get; }

    public decimal LoadedWeightKg { get; }

    public decimal MaxCargoWeightKg { get; }

    public decimal PercentFull { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
