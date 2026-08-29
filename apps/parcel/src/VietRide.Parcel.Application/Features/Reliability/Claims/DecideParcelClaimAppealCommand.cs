using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record DecideParcelClaimAppealCommand(
    Guid AppealId,
    Guid OperatorId,
    Guid DecidedByUserId,
    string Decision,
    long? RevisedProvenDirectLossVnd,
    string Reason) : IRequest<ParcelClaimAppealResponse>;
