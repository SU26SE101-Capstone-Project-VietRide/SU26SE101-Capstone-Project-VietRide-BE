namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed record ParcelRouteFareListItemResponse(
    string SizeCategory,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);
