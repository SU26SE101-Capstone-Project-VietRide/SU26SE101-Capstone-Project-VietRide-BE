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
    public decimal EstimatedLengthCm { get; private set; }
    public decimal EstimatedWidthCm { get; private set; }
    public decimal EstimatedHeightCm { get; private set; }
    public decimal EstimatedWeightKg { get; private set; }
    public decimal EstimatedVolumeM3 { get; private set; }
    public decimal EstimatedDimWeightKg { get; private set; }
    public decimal EstimatedChargeableWeightKg { get; private set; }
    public decimal? ActualLengthCm { get; private set; }
    public decimal? ActualWidthCm { get; private set; }
    public decimal? ActualHeightCm { get; private set; }
    public decimal? ActualWeightKg { get; private set; }
    public decimal? ActualVolumeM3 { get; private set; }
    public decimal? ActualDimWeightKg { get; private set; }
    public decimal? ActualChargeableWeightKg { get; private set; }
    public ParcelDeliveryMethod DeliveryMethod { get; private set; }

    public Money TotalPrice { get; private set; }
    public decimal DepositPercent { get; private set; }
    public Money DepositAmount { get; private set; }
    public Money OriginalDepositAmount { get; private set; }
    public Money DiscountAmount { get; private set; }
    public string? VoucherCode { get; private set; }
    public Guid? VoucherUsageId { get; private set; }
    public Money AdditionalAmount { get; private set; }
    public Money RefundAmount { get; private set; }
    public Guid? AdditionalPaymentId { get; private set; }
    public DateTimeOffset? AdditionalPaymentDeadline { get; private set; }

    public ParcelStatus Status { get; private set; }
    public PendingActionType? PendingActionType { get; private set; }
    public string? PendingActionReason { get; private set; }
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
        Money depositAmount,
        Money? originalDepositAmount = null,
        Money? discountAmount = null,
        string? voucherCode = null,
        Guid? voucherUsageId = null)
        => CreatePendingPayment(
            parcelCode,
            senderUserId,
            recipientUserId,
            recipientName,
            recipientPhone,
            recipientEmail,
            operatorId,
            tripId,
            dropoffStopId,
            bookingId,
            description,
            photoUrl,
            sizeCategory,
            estimatedLengthCm: 1m,
            estimatedWidthCm: 1m,
            estimatedHeightCm: 1m,
            estimatedWeightKg,
            estimatedVolumeM3: 0.0001m,
            estimatedDimWeightKg: 0.01m,
            estimatedChargeableWeightKg: estimatedWeightKg,
            deliveryMethod,
            totalPrice: depositAmount,
            depositPercent: 100m,
            depositAmount,
            originalDepositAmount,
            discountAmount,
            voucherCode,
            voucherUsageId);

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
        decimal estimatedLengthCm,
        decimal estimatedWidthCm,
        decimal estimatedHeightCm,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        decimal estimatedDimWeightKg,
        decimal estimatedChargeableWeightKg,
        ParcelDeliveryMethod deliveryMethod,
        Money totalPrice,
        decimal depositPercent,
        Money depositAmount,
        Money? originalDepositAmount = null,
        Money? discountAmount = null,
        string? voucherCode = null,
        Guid? voucherUsageId = null)
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
            PhotoUrl = NormalizeOptional(photoUrl),
            SizeCategory = sizeCategory,
            EstimatedLengthCm = estimatedLengthCm,
            EstimatedWidthCm = estimatedWidthCm,
            EstimatedHeightCm = estimatedHeightCm,
            EstimatedWeightKg = estimatedWeightKg,
            EstimatedVolumeM3 = estimatedVolumeM3,
            EstimatedDimWeightKg = estimatedDimWeightKg,
            EstimatedChargeableWeightKg = estimatedChargeableWeightKg,
            DeliveryMethod = deliveryMethod,
            TotalPrice = totalPrice,
            DepositPercent = depositPercent,
            DepositAmount = depositAmount,
            OriginalDepositAmount = originalDepositAmount ?? depositAmount,
            DiscountAmount = discountAmount ?? Money.Zero,
            VoucherCode = voucherCode,
            VoucherUsageId = voucherUsageId,
            AdditionalAmount = Money.Zero,
            RefundAmount = Money.Zero,
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
        Money depositAmount,
        Money? originalDepositAmount = null,
        Money? discountAmount = null,
        string? voucherCode = null,
        Guid? voucherUsageId = null)
        => CreatePendingOperatorReview(
            parcelCode,
            senderUserId,
            recipientUserId,
            recipientName,
            recipientPhone,
            recipientEmail,
            operatorId,
            tripId,
            dropoffStopId,
            bookingId,
            description,
            photoUrl,
            sizeCategory,
            estimatedLengthCm: 1m,
            estimatedWidthCm: 1m,
            estimatedHeightCm: 1m,
            estimatedWeightKg,
            estimatedVolumeM3: 0.0001m,
            estimatedDimWeightKg: 0.01m,
            estimatedChargeableWeightKg: estimatedWeightKg,
            deliveryMethod,
            totalPrice: depositAmount,
            depositPercent: 100m,
            depositAmount,
            originalDepositAmount,
            discountAmount,
            voucherCode,
            voucherUsageId);

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
        decimal estimatedLengthCm,
        decimal estimatedWidthCm,
        decimal estimatedHeightCm,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        decimal estimatedDimWeightKg,
        decimal estimatedChargeableWeightKg,
        ParcelDeliveryMethod deliveryMethod,
        Money totalPrice,
        decimal depositPercent,
        Money depositAmount,
        Money? originalDepositAmount = null,
        Money? discountAmount = null,
        string? voucherCode = null,
        Guid? voucherUsageId = null)
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
            PhotoUrl = NormalizeOptional(photoUrl),
            SizeCategory = sizeCategory,
            EstimatedLengthCm = estimatedLengthCm,
            EstimatedWidthCm = estimatedWidthCm,
            EstimatedHeightCm = estimatedHeightCm,
            EstimatedWeightKg = estimatedWeightKg,
            EstimatedVolumeM3 = estimatedVolumeM3,
            EstimatedDimWeightKg = estimatedDimWeightKg,
            EstimatedChargeableWeightKg = estimatedChargeableWeightKg,
            DeliveryMethod = deliveryMethod,
            TotalPrice = totalPrice,
            DepositPercent = depositPercent,
            DepositAmount = depositAmount,
            OriginalDepositAmount = originalDepositAmount ?? depositAmount,
            DiscountAmount = discountAmount ?? Money.Zero,
            VoucherCode = voucherCode,
            VoucherUsageId = voucherUsageId,
            AdditionalAmount = Money.Zero,
            RefundAmount = Money.Zero,
            Status = ParcelStatus.PENDING_OPERATOR_REVIEW,
            ReviewDecision = ParcelReviewDecision.PENDING,
        };
    }

    public void AttachVoucherUsage(Guid voucherUsageId)
    {
        VoucherUsageId = voucherUsageId;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
