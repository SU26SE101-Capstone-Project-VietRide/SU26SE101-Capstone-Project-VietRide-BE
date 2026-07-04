namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CargoMutationRequest(
    Guid ParcelId,
    decimal WeightKg,
    string? IdempotencyKey);
