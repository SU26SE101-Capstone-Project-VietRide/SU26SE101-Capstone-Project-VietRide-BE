using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record AddParcelClaimEvidenceCommand(
    Guid ParcelId,
    Guid ClaimId,
    Guid UploaderUserId,
    string EvidenceType,
    string Reference,
    string? Note) : IRequest<ParcelClaimResponse>;
