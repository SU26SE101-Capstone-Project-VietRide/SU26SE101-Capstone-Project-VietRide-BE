using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Unload;

[SkipTransaction]
public sealed record UnloadParcelCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId) : IRequest<UnloadParcelResponse>;
