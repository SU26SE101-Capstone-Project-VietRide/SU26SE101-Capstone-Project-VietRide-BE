namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record TripCargoMutationResult(
    Guid TripId,
    decimal ReservedWeightKg,
    decimal ReservedVolumeM3,
    decimal LoadedWeightKg,
    decimal LoadedVolumeM3,
    decimal MaxCargoWeightKg,
    decimal MaxCargoVolumeM3,
    decimal PercentFull,
    bool NearFullCrossed,
    Guid OperatorId);
