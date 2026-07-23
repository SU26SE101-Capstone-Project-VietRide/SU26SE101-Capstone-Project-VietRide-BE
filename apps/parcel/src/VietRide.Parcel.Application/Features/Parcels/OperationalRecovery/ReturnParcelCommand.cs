using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed record ReturnParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ReturnedByUserId,
    string Reason,
    bool IsStatusOverride = false,
    Guid? IdempotencyKey = null) : IRequest<OperationalParcelResponse>;
