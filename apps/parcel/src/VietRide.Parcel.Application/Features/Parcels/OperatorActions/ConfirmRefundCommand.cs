using MediatR;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorActions;

public sealed record ConfirmRefundCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ActorUserId,
    string? Reason) : IRequest<OperationalParcelResponse>;
