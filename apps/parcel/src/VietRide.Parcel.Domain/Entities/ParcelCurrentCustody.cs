using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelCurrentCustody : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public ParcelCustodyEventType LastEventType { get; private set; }
    public ParcelCustodyLocationType? LastLocationType { get; private set; }
    public Guid? LastLocationId { get; private set; }
    public string? LastLocationSnapshot { get; private set; }
    public DateTimeOffset LastConfirmedAt { get; private set; }
    public Guid? CurrentTripId { get; private set; }
    public Guid? CurrentVehicleId { get; private set; }
    public ParcelCustodyTrackingConfidence TrackingConfidence { get; private set; }
    public int LastSequence { get; private set; }

    private ParcelCurrentCustody()
    {
    }

    public static ParcelCurrentCustody Create(
        Guid parcelId,
        ParcelCustodyEvent custodyEvent)
        => new()
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            LastEventType = custodyEvent.EventType,
            LastLocationType = custodyEvent.ActualLocationType,
            LastLocationId = custodyEvent.ActualLocationId,
            LastLocationSnapshot = custodyEvent.LocationSnapshot,
            LastConfirmedAt = custodyEvent.OccurredAt,
            CurrentTripId = custodyEvent.TripId,
            CurrentVehicleId = custodyEvent.VehicleId,
            TrackingConfidence = custodyEvent.EventType == ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION
                ? ParcelCustodyTrackingConfidence.MANUAL_EXCEPTION
                : ParcelCustodyTrackingConfidence.CONFIRMED_SCAN,
            LastSequence = custodyEvent.Sequence,
        };

    public void Apply(ParcelCustodyEvent custodyEvent)
    {
        if (custodyEvent.Sequence <= LastSequence)
            return;

        LastEventType = custodyEvent.EventType;
        LastLocationType = custodyEvent.ActualLocationType;
        LastLocationId = custodyEvent.ActualLocationId;
        LastLocationSnapshot = custodyEvent.LocationSnapshot;
        LastConfirmedAt = custodyEvent.OccurredAt;
        CurrentTripId = custodyEvent.TripId;
        CurrentVehicleId = custodyEvent.VehicleId;
        TrackingConfidence = custodyEvent.EventType == ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION
            ? ParcelCustodyTrackingConfidence.MANUAL_EXCEPTION
            : ParcelCustodyTrackingConfidence.CONFIRMED_SCAN;
        LastSequence = custodyEvent.Sequence;
    }
}
