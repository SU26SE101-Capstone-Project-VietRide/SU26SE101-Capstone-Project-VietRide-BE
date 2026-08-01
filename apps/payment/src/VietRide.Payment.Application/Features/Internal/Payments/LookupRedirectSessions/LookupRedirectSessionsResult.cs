namespace VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

public sealed record LookupRedirectSessionsResult(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    long Amount,
    DateTimeOffset DueAt,
    string PaymentRedirectUrl);
