namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record AssistantParcelManifestCounts(
    int Total,
    int CheckedIn,
    int Loaded,
    int ExpectedAtCurrentStop,
    int Unloaded,
    int ExceptionCount,
    int UnresolvedCount);
