namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record CreateParcelRequest(
    Guid TripId,
    Guid? DropoffStopId,
    Guid? BookingId,
    string? ItemName,
    string? Description,
    string SizeCategory,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal EstimatedWeightKg,
    string? PhotoUrl,
    RecipientRequest Recipient,
    string DeliveryMethod,
    string PaymentMethod,
    string? VoucherCode);
