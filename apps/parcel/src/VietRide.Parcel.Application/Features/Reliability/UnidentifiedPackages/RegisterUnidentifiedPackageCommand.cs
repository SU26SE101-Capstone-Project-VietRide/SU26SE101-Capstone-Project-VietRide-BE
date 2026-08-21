using MediatR;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record RegisterUnidentifiedPackageCommand(
    Guid OperatorId,
    Guid ActorUserId,
    string TemporaryExceptionTag,
    Guid? TripId,
    string LocationType,
    Guid LocationId,
    string? LocationSnapshot,
    string Description,
    decimal? ObservedWeightKg,
    IReadOnlyCollection<string> EvidenceReferences) : IRequest<UnidentifiedPackageResponse>;

public sealed record UnidentifiedPackageResponse(
    Guid PackageId,
    string TemporaryExceptionTag,
    Guid OperatorId,
    string Status,
    string LocationType,
    Guid LocationId,
    Guid? MatchedParcelId,
    DateTimeOffset CreatedAt,
    Guid? TripId = null,
    string? LocationSnapshot = null,
    string? Description = null,
    decimal? ObservedWeightKg = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    Guid? CreatedByUserId = null,
    DateTimeOffset? MatchedAt = null,
    Guid? MatchedByUserId = null,
    ReliabilityTripResponse? Trip = null,
    ReliabilityParcelSummaryResponse? MatchedParcel = null,
    IReadOnlyList<string>? AvailableActions = null);
