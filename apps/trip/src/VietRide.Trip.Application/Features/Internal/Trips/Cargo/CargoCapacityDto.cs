namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record CargoCapacityDto(
    Guid TripId,
    decimal ReservedWeightKg,
    decimal LoadedWeightKg,
    decimal MaxCargoWeightKg,
    decimal PercentFull);
