using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed record RequestTransferCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid TargetTripId,
    string? Reason) : IRequest<OperationalParcelResponse>;
