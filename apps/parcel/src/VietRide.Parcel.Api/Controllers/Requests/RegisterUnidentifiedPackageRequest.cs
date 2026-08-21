namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record RegisterUnidentifiedPackageRequest(
    string TemporaryExceptionTag,
    Guid? TripId,
    string LocationType,
    Guid LocationId,
    string? LocationSnapshot,
    string Description,
    decimal? ObservedWeightKg,
    IReadOnlyCollection<string>? EvidenceReferences);

public sealed record MatchUnidentifiedPackageRequest(Guid ParcelId);
