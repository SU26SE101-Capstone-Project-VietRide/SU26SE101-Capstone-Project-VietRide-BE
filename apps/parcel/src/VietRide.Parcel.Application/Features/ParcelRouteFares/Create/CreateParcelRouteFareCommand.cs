using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

[SkipTransaction]
public sealed record CreateParcelRouteFareCommand(
    Guid OperatorId,
    Guid RouteId,
    string SizeCategory,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil) : IRequest<ParcelRouteFareResponse>;
