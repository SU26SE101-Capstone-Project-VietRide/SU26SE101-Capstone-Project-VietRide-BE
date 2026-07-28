using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;

public sealed record ConfirmPaymentForParcelCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    long Amount,
    string? Method = null,
    DateTimeOffset? PaidAt = null,
    DateTimeOffset? DueAt = null) : IRequest<bool>;
