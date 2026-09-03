using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record PreviewParcelClaimAppealAdjustmentQuery(
    Guid AppealId,
    Guid OperatorId,
    string? ProofStatus,
    long? RevisedProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds) : IRequest<ParcelCompensationPreviewResponse>;
