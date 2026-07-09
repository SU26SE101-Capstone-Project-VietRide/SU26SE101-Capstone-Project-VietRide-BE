namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CargoMutationRequest(
    Guid ParcelId,
    decimal WeightKg,
    decimal VolumeM3,
    bool AllowCapacityOverflow,
    string? IdempotencyKey);
