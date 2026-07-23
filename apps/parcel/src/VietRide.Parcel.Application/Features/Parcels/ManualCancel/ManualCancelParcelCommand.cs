using MediatR;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

namespace VietRide.Parcel.Application.Features.Parcels.ManualCancel;

public sealed record ManualCancelParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    string Reason,
    string? RefundChoice,
    Guid? IdempotencyKey = null) : IRequest<OperationalParcelResponse>;
