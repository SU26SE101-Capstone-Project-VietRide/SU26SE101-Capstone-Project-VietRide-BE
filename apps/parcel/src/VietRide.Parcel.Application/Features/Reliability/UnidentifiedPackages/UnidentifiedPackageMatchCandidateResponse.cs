using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record UnidentifiedPackageMatchCandidateResponse(
    Guid ParcelId,
    string ParcelCode,
    ReliabilityTripResponse Trip,
    string? PhotoUrl,
    string? Description,
    decimal WeightKg,
    ReliabilityLocationResponse ExpectedDropoff,
    IReadOnlyList<string> MatchReasons);
