namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record CreateParcelRouteFareRequest(
    Guid RouteId,
    string SizeCategory,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);
