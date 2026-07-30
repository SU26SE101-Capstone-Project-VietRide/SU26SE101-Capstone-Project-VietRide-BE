namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record CargoTransferDto(
    Guid ParcelId,
    Guid SourceTripId,
    Guid TargetTripId,
    string TargetState,
    decimal WeightKg,
    decimal VolumeM3);
