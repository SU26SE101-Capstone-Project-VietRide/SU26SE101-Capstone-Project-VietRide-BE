using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record ReturnParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ReturnedByUserId,
    string Reason,
    Guid IdempotencyKey,
    bool IsStatusOverride = false) : IRequest<OperationalParcelResponse>;
