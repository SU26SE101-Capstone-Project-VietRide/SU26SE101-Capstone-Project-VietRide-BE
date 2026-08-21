using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record SubmitParcelClaimCommand(
    Guid ParcelId,
    Guid SenderUserId,
    string? IdempotencyKey) : IRequest<ParcelClaimResponse>;
