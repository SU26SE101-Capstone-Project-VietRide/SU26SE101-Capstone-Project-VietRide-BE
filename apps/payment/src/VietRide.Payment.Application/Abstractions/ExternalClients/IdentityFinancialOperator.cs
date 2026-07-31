namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public sealed record IdentityFinancialOperator(
    Guid OperatorId,
    string Name,
    string? LogoUrl,
    string? ContactPhone);
