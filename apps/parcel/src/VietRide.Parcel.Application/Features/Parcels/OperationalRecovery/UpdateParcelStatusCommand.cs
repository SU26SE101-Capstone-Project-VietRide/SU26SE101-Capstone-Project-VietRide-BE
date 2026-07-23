using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record UpdateParcelStatusCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid UserId,
    string Status,
    string? Reason,
    Guid? IdempotencyKey = null) : IRequest<OperationalParcelResponse>;
