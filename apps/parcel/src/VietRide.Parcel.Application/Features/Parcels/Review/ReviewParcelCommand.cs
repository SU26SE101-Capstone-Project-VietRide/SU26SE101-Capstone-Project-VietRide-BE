using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

[SkipTransaction]
public sealed record ReviewParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ReviewedByUserId,
    string Decision,
    long? DepositAmount,
    string? Reason,
    string? PaymentMethod,
    string? IdempotencyKey = null) : IRequest<ReviewParcelResponse>;
