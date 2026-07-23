using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

[SkipTransaction]
public sealed record CreateParcelCommand(
    Guid SenderUserId,
    Guid? RecipientUserId,
    string RecipientName,
    string RecipientPhone,
    string? RecipientEmail,
    Guid TripId,
    Guid? DropoffStopId,
    Guid? BookingId,
    string? ItemName,
    string? Description,
    string? PhotoUrl,
    string SizeCategory,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal EstimatedWeightKg,
    string DeliveryMethod,
    string PaymentMethod,
    string? VoucherCode = null,
    string? IdempotencyKey = null) : IRequest<CreateParcelResponse>
{
    public CreateParcelCommand(
        Guid senderUserId,
        Guid? recipientUserId,
        string recipientName,
        string recipientPhone,
        string? recipientEmail,
        Guid tripId,
        Guid? dropoffStopId,
        Guid? bookingId,
        string? itemName,
        string? description,
        string? photoUrl,
        string sizeCategory,
        decimal estimatedWeightKg,
        string deliveryMethod,
        string paymentMethod,
        string? voucherCode = null)
        : this(
            senderUserId,
            recipientUserId,
            recipientName,
            recipientPhone,
            recipientEmail,
            tripId,
            dropoffStopId,
            bookingId,
            itemName,
            description,
            photoUrl,
            sizeCategory,
            LengthCm: 1m,
            WidthCm: 1m,
            HeightCm: 1m,
            estimatedWeightKg,
            deliveryMethod,
            paymentMethod,
            voucherCode,
            null)
    {
    }
}
