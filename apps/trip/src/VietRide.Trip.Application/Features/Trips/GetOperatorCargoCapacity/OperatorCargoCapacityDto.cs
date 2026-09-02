namespace VietRide.Trip.Application.Features.Trips.GetOperatorCargoCapacity;

public sealed record OperatorCargoCapacityDto(
    Guid TripId,
    decimal ReservedWeightKg,
    decimal ReservedVolumeM3,
    decimal LoadedWeightKg,
    decimal LoadedVolumeM3,
    decimal MaxCargoWeightKg,
    decimal MaxCargoVolumeM3,
    decimal AvailableWeightKg,
    decimal AvailableVolumeM3,
    decimal PercentFull,
    decimal HistoricalLoadedWeightKg,
    decimal HistoricalLoadedVolumeM3);
