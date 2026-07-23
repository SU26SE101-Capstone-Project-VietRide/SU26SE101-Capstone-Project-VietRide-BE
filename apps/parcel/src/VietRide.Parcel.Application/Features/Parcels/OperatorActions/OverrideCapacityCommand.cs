using MediatR;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorActions;

[SkipTransaction]
public sealed record OverrideCapacityCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ActorUserId,
    string Reason,
    Guid? IdempotencyKey = null) : IRequest<OperationalParcelResponse>;
