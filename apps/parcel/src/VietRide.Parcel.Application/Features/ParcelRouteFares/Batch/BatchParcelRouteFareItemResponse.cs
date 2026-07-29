namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;

public sealed record BatchParcelRouteFareItemResponse(
    string SizeCategory,
    long PriceVnd,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    bool Created);
