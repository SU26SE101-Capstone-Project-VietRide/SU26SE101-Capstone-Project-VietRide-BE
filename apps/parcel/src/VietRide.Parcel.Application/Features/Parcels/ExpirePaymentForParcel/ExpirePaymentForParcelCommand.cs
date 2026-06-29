using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;

public sealed record ExpirePaymentForParcelCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId) : IRequest<bool>;
