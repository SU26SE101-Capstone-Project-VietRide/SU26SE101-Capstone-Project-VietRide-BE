namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record ParcelHistoryDetailsDto(
    Guid? BookingId,
    string RecipientName,
    string SizeCategory,
    string? PhotoUrl,
    string DeliveryMethod);
