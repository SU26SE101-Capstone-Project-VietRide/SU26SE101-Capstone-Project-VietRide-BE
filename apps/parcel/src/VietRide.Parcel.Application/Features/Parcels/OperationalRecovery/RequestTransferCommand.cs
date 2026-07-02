using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record RequestTransferCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid TargetTripId,
    string? Reason) : IRequest<OperationalParcelResponse>;
