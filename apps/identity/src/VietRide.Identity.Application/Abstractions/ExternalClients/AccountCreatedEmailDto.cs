namespace VietRide.Identity.Application.Abstractions.ExternalClients;

public sealed record AccountCreatedEmailDto(
    Guid UserId,
    string DisplayName,
    string SetInitialPasswordUrl,
    DateTimeOffset ExpiresAt);
