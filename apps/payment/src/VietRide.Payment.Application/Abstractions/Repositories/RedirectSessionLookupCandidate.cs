using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public sealed record RedirectSessionLookupCandidate(
    Guid PaymentId,
    PaymentReferenceType ReferenceType,
    Guid ReferenceId,
    Guid? UserId,
    long Amount,
    PaymentMethod Method,
    PaymentStatus Status,
    DateTimeOffset? DueAt,
    string? PaymentRedirectUrl,
    string Context,
    bool ContextReconciliationRequired);
