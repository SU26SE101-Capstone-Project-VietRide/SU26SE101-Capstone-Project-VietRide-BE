using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.FailPaymentForParcel;

public sealed record FailPaymentForParcelCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId) : IRequest<bool>;
