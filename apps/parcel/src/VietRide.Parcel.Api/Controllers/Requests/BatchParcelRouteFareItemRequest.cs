namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record BatchParcelRouteFareItemRequest(
    string? SizeCategory,
    long PriceVnd);
