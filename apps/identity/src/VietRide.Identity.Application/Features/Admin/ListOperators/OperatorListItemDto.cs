namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed record OperatorListItemDto(
    Guid OperatorId,
    string Name,
    string ContactEmail,
    string ContactPhone,
    string BusinessRegistrationNumber,
    string TaxCode,
    string RegistrationStatus,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? SuspendedAt);
