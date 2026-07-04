using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;

public sealed record ConfirmPaymentForParcelCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    long Amount) : IRequest<bool>;
