namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record CreateParcelRequest(
    Guid TripId,
    Guid? BookingId,
    string? ItemName,
    string? Description,
    string SizeCategory,
    decimal EstimatedWeightKg,
    string? PhotoUrl,
    RecipientRequest Recipient,
    string DeliveryMethod,
    string PaymentMethod);
