namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityLocationResponse(
    string? Type,
    Guid? Id,
    string? Name,
    int? OrderIndex = null,
    DateTimeOffset? Eta = null);
