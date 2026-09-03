using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record DecideParcelClaimCommand(
    Guid ClaimId,
    Guid OperatorId,
    Guid DecidedBy,
    string Decision,
    string? ProofStatus,
    long? ProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds,
    string Reason) : IRequest<ParcelClaimResponse>;
