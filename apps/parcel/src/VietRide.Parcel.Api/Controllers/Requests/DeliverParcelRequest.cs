namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DeliverParcelRequest(IReadOnlyCollection<string>? PhotoUrls);
