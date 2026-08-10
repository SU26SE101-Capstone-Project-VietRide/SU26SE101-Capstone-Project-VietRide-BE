using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.DepositPayment;

[SkipTransaction]
public sealed record StartParcelDepositPaymentCommand(
    Guid ParcelId,
    Guid SenderUserId,
    string PaymentMethod,
    string IdempotencyKey,
    string? PaymentReturnMode = null) : IRequest<ParcelDepositPaymentResponse>;
