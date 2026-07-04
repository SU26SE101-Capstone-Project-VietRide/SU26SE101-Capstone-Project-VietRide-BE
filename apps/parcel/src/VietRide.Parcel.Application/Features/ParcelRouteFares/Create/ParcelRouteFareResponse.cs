namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

public sealed record ParcelRouteFareResponse(
    Guid RouteId,
    string SizeCategory,
    Guid OperatorId,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
