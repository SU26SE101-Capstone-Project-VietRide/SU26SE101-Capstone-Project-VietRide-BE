using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.RecordRefund;

public sealed record RecordParcelRefundedCommand(
    Guid ParcelId,
    Guid UserId,
    long Amount,
    string ReferenceType) : IRequest<bool>;
