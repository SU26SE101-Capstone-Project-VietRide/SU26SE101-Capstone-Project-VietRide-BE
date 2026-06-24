namespace VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;

public sealed record MarkPaymentRefundedCommand(string ReferenceType, Guid ReferenceId);
