using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed record UpdateParcelStatusCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid UserId,
    string Status,
    string? Reason) : IRequest<OperationalParcelResponse>;
