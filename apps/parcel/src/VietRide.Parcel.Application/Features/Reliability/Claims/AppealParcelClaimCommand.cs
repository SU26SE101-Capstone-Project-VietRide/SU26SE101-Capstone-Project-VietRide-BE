using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record AppealParcelClaimCommand(
    Guid ParcelId,
    Guid ClaimId,
    Guid SenderUserId,
    string Reason,
    Guid IdempotencyKey) : IRequest<ParcelClaimResponse>;
