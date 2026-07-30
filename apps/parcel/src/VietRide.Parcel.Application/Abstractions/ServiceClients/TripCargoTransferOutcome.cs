namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripCargoTransferOutcomeKind
{
    Success,
    TripNotFound,
    ParcelCargoNotFound,
    Conflict,
    CapacityExceeded,
    TransportError,
}

public sealed record TripCargoTransferOutcome(
    TripCargoTransferOutcomeKind Kind,
    string? ErrorMessage = null,
    TripCargoTransferSnapshot? Transfer = null);

public sealed record TripCargoTransferSnapshot(
    Guid ParcelId,
    Guid SourceTripId,
    Guid TargetTripId,
    string TargetState,
    decimal WeightKg,
    decimal VolumeM3);
