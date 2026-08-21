namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityParcelSummaryResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    string? Description,
    string? PhotoUrl,
    int Quantity,
    long? DeclaredValueVnd);
