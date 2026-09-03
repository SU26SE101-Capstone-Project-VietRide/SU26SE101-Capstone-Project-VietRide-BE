using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record DecideParcelClaimAppealCommand(
    Guid AppealId,
    Guid OperatorId,
    Guid DecidedByUserId,
    string Decision,
    string? ProofStatus,
    long? RevisedProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds,
    string Reason) : IRequest<ParcelClaimAppealResponse>;
