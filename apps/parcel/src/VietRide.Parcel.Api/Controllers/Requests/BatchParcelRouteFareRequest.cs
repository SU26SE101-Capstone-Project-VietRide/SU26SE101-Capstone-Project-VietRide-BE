namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record BatchParcelRouteFareRequest(
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    IReadOnlyList<BatchParcelRouteFareItemRequest?>? Items);
