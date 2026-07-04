using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Domain.Entities;

public sealed class Parcel : BaseEntity<Guid>
{
    public string ParcelCode { get; private set; } = null!;

    public Guid SenderUserId { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public PhoneNumber RecipientPhone { get; private set; }
    public string? RecipientEmail { get; private set; }

    public Guid OperatorId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid? DropoffStopId { get; private set; }
    public Guid? BookingId { get; private set; }

    public string? Description { get; private set; }
    public string? PhotoUrl { get; private set; }
    public ParcelSizeCategory SizeCategory { get; private set; }
    public decimal EstimatedWeightKg { get; private set; }
    public decimal? ActualWeightKg { get; private set; }
    public ParcelDeliveryMethod DeliveryMethod { get; private set; }

    public Money DepositAmount { get; private set; }
    public Money AdditionalAmount { get; private set; }
    public Guid? AdditionalPaymentId { get; private set; }
    public DateTimeOffset? AdditionalPaymentDeadline { get; private set; }

    public ParcelStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? CancellationReason { get; private set; }

    public ParcelReviewDecision? ReviewDecision { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    public Guid? DeliveryToken { get; private set; }
    public DateTimeOffset? DeliveryTokenExpiresAt { get; private set; }
    public DateTimeOffset? DeliveryTokenRevokedAt { get; private set; }

    public DateTimeOffset? LoadedAt { get; private set; }
    public Guid? LoadedByUserId { get; private set; }
    public DateTimeOffset? UnloadedAt { get; private set; }
    public DateTimeOffset? DeliveredPendingConfirmAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public string? ConfirmedByIp { get; private set; }
    public string? ConfirmNote { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public DateTimeOffset? LastReminderAt { get; private set; }

    public Guid? TransferTargetTripId { get; private set; }
    public DateTimeOffset? TransferRequestedAt { get; private set; }
    public DateTimeOffset? TransferConfirmedAt { get; private set; }
    public Guid? TransferConfirmedByUserId { get; private set; }

    public string? ReturnReason { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public Guid? ReturnedByUserId { get; private set; }

    private Parcel() { }

    public void AssignAdditionalPaymentId(Guid paymentId)
    {
        AdditionalPaymentId = paymentId;
    }

    public static Parcel CreatePendingPayment(
        string parcelCode,
        Guid senderUserId,
        Guid? recipientUserId,
        string recipientName,
        PhoneNumber recipientPhone,
        string? recipientEmail,
        Guid operatorId,
        Guid tripId,
        Guid? dropoffStopId,
        Guid? bookingId,
        string? description,
        string? photoUrl,
        ParcelSizeCategory sizeCategory,
        decimal estimatedWeightKg,
        ParcelDeliveryMethod deliveryMethod,
        Money depositAmount)
    {
        return new Parcel
        {
            Id = Guid.NewGuid(),
            ParcelCode = parcelCode,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone,
            RecipientEmail = recipientEmail,
            OperatorId = operatorId,
            TripId = tripId,
            DropoffStopId = dropoffStopId,
            BookingId = bookingId,
            Description = description,
            PhotoUrl = photoUrl,
            SizeCategory = sizeCategory,
            EstimatedWeightKg = estimatedWeightKg,
            DeliveryMethod = deliveryMethod,
            DepositAmount = depositAmount,
            AdditionalAmount = Money.Zero,
            Status = ParcelStatus.PENDING_PAYMENT,
            ReviewDecision = ParcelReviewDecision.PENDING,
        };
    }

    public static Parcel CreatePendingOperatorReview(
        string parcelCode,
        Guid senderUserId,
        Guid? recipientUserId,
        string recipientName,
        PhoneNumber recipientPhone,
        string? recipientEmail,
        Guid operatorId,
        Guid tripId,
        Guid? dropoffStopId,
        Guid? bookingId,
        string? description,
        string? photoUrl,
        ParcelSizeCategory sizeCategory,
        decimal estimatedWeightKg,
        ParcelDeliveryMethod deliveryMethod,
        Money depositAmount)
    {
        return new Parcel
        {
            Id = Guid.NewGuid(),
            ParcelCode = parcelCode,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone,
            RecipientEmail = recipientEmail,
            OperatorId = operatorId,
            TripId = tripId,
            DropoffStopId = dropoffStopId,
            BookingId = bookingId,
            Description = description,
            PhotoUrl = photoUrl,
            SizeCategory = sizeCategory,
            EstimatedWeightKg = estimatedWeightKg,
            DeliveryMethod = deliveryMethod,
            DepositAmount = depositAmount,
            AdditionalAmount = Money.Zero,
            Status = ParcelStatus.PENDING_OPERATOR_REVIEW,
            ReviewDecision = ParcelReviewDecision.PENDING,
        };
    }
}
