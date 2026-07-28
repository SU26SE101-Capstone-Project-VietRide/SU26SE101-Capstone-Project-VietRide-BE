using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

[SkipTransaction]
public sealed record ReviewParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ReviewedByUserId,
    string Decision,
    string? Reason,
    string? IdempotencyKey = null) : IRequest<ReviewParcelResponse>;
