namespace VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;

public sealed record VnPayReturnStatusResponse(
    string VnPayTxnRef,
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    string Status);
