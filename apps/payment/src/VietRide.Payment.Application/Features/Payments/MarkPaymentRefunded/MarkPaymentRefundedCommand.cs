namespace VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;

public sealed record MarkPaymentRefundedCommand(
    string ReferenceType,
    Guid ReferenceId,
    Guid? SourceEventId = null,
    Guid? UserId = null,
    long? Amount = null,
    Guid? PaymentId = null);
