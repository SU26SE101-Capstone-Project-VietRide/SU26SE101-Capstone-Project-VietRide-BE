using MediatR;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

public sealed record CreateParcelRouteFareCommand(
    Guid OperatorId,
    Guid RouteId,
    string SizeCategory,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil) : IRequest<ParcelRouteFareResponse>;
