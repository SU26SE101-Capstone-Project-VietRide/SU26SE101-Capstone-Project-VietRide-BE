namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record CargoCapacityDto(
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
