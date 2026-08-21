using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Unload;

[SkipTransaction]
public sealed record UnloadParcelCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    Guid? IdempotencyKey = null,
    string? ActualLocationKind = null,
    Guid? ActualLocationId = null,
    IReadOnlyCollection<string>? PhotoUrls = null,
    string? ScannedParcelCode = null) : IRequest<UnloadParcelResponse>;
