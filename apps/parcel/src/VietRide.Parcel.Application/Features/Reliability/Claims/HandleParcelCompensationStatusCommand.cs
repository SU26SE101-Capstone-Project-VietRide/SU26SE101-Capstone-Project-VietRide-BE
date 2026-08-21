using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record HandleParcelCompensationStatusCommand(
    Guid ClaimId,
    Guid PayoutId,
    string Status,
    DateTimeOffset OccurredAt) : IRequest<bool>;
