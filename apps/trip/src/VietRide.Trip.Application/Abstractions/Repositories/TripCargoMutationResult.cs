namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record TripCargoMutationResult(
    Guid TripId,
    decimal ReservedWeightKg,
    decimal LoadedWeightKg,
    decimal MaxCargoWeightKg,
    decimal PercentFull,
    bool NearFullCrossed);
