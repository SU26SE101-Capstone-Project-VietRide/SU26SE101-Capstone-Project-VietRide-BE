namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public sealed record IdentityFinancialUser(
    Guid UserId,
    string DisplayName,
    string? Email,
    string Role,
    bool Deleted);
