namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record RecipientRequest(
    string FullName,
    string PhoneNumber,
    string? Email);
