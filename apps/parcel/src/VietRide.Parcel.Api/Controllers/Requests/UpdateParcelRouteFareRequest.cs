namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record UpdateParcelRouteFareRequest(
    long? PriceVnd,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveUntil);
