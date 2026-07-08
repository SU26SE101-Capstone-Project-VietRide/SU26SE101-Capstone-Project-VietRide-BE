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
    decimal EstimatedWeightKg,
    string DeliveryMethod,
    string PaymentMethod,
    string? VoucherCode = null) : IRequest<CreateParcelResponse>;
