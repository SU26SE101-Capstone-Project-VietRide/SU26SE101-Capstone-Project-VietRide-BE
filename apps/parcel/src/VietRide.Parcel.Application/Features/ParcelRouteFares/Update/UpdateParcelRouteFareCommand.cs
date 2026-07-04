using MediatR;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Update;

public sealed record UpdateParcelRouteFareCommand(
    Guid OperatorId,
    Guid RouteId,
    string SizeCategory,
    long? PriceVnd,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveUntil) : IRequest<ParcelRouteFareResponse>;
