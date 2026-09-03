using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record PreviewParcelClaimAwardQuery(
    Guid ClaimId,
    Guid OperatorId,
    string? ProofStatus,
    long? ProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds) : IRequest<ParcelCompensationPreviewResponse>;
