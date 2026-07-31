using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;

[SkipTransaction]
public sealed record BatchParcelRouteFareCommand(
    Guid OperatorId,
    Guid RouteId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    IReadOnlyList<BatchParcelRouteFareItem> Items) : IRequest<BatchParcelRouteFareResponse>;
