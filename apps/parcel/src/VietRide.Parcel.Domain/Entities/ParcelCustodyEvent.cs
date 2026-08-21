using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelCustodyEvent : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid? LegId { get; private set; }
    public Guid? TripId { get; private set; }
    public ParcelCustodyEventType EventType { get; private set; }
    public ParcelCustodyLocationType? ExpectedLocationType { get; private set; }
    public Guid? ExpectedLocationId { get; private set; }
    public ParcelCustodyLocationType? ActualLocationType { get; private set; }
    public Guid? ActualLocationId { get; private set; }
    public string? LocationSnapshot { get; private set; }
    public Guid? VehicleId { get; private set; }
    public Guid? ActorId { get; private set; }
    public string ActorRole { get; private set; } = "SYSTEM";
    public DateTimeOffset OccurredAt { get; private set; }
    public string Source { get; private set; } = "API";
    public string? IdempotencyKey { get; private set; }
    public string? EvidenceReferencesJson { get; private set; }
    public string? Reason { get; private set; }
    public int Sequence { get; private set; }

    private ParcelCustodyEvent()
    {
    }

    public static ParcelCustodyEvent Create(
        Guid parcelId,
        Guid? legId,
        Guid? tripId,
        ParcelCustodyEventType eventType,
        ParcelCustodyLocationType? expectedLocationType,
        Guid? expectedLocationId,
        ParcelCustodyLocationType? actualLocationType,
        Guid? actualLocationId,
        string? locationSnapshot,
        Guid? vehicleId,
        Guid? actorId,
        string actorRole,
        DateTimeOffset occurredAt,
        string source,
        string? idempotencyKey,
        string? evidenceReferencesJson,
        string? reason,
        int sequence)
    {
        if (parcelId == Guid.Empty)
            throw new ArgumentException("Parcel id is required.", nameof(parcelId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));

        return new ParcelCustodyEvent
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            LegId = legId,
            TripId = tripId,
            EventType = eventType,
            ExpectedLocationType = expectedLocationType,
            ExpectedLocationId = expectedLocationId,
            ActualLocationType = actualLocationType,
            ActualLocationId = actualLocationId,
            LocationSnapshot = Normalize(locationSnapshot),
            VehicleId = vehicleId,
            ActorId = actorId,
            ActorRole = Normalize(actorRole) ?? "SYSTEM",
            OccurredAt = occurredAt,
            Source = Normalize(source) ?? "API",
            IdempotencyKey = Normalize(idempotencyKey),
            EvidenceReferencesJson = Normalize(evidenceReferencesJson),
            Reason = Normalize(reason),
            Sequence = sequence,
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
