namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripCargoOutcomeKind
{
    Success,
    TripNotFound,
    CapacityExceeded,
    InvalidState,
    TransportError,
}

public sealed record TripCargoOutcome(
    TripCargoOutcomeKind Kind,
    string? ErrorMessage,
    TripCargoCapacitySnapshot? Capacity = null);

public sealed record TripCargoCapacitySnapshot(
    Guid TripId,
    decimal ReservedWeightKg,
    decimal ReservedVolumeM3,
    decimal LoadedWeightKg,
    decimal LoadedVolumeM3,
    decimal MaxCargoWeightKg,
    decimal MaxCargoVolumeM3,
    decimal AvailableWeightKg,
    decimal AvailableVolumeM3,
    decimal PercentFull);
