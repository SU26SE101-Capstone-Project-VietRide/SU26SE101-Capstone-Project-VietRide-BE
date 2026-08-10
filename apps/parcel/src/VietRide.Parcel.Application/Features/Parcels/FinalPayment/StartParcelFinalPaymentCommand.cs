using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.FinalPayment;

public sealed record StartParcelFinalPaymentCommand(
    Guid ParcelId,
    Guid SenderUserId,
    string PaymentMethod,
    string IdempotencyKey,
    string? PaymentReturnMode = null) : IRequest<ParcelFinalPaymentResponse>;
