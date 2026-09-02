namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record OperatorCargoCapacityReadModel(
    Guid TripId,
    Guid OperatorId,
    decimal ReservedWeightKg,
    decimal ReservedVolumeM3,
    decimal LoadedWeightKg,
    decimal LoadedVolumeM3,
    decimal MaxCargoWeightKg,
    decimal MaxCargoVolumeM3,
    decimal HistoricalLoadedWeightKg,
    decimal HistoricalLoadedVolumeM3);
