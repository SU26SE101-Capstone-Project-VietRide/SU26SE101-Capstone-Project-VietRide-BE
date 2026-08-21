namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityOperatorResponse(
    Guid OperatorId,
    string? Name,
    string? LogoUrl,
    string? ContactPhone);
