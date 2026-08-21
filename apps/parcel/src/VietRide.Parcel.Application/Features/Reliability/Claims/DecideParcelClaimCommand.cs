using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record DecideParcelClaimCommand(
    Guid ClaimId,
    Guid OperatorId,
    Guid DecidedBy,
    string Decision,
    long? ProvenDirectLossVnd,
    string Reason) : IRequest<ParcelClaimResponse>;
