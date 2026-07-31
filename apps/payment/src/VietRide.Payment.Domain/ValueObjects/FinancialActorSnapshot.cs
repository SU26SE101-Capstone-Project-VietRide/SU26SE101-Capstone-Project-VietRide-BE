namespace VietRide.Payment.Domain.ValueObjects;

public sealed record FinancialActorSnapshot
{
    public FinancialActorSnapshot(Guid userId, string displayName, string email, string role)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Actor user id is required.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        UserId = userId;
        DisplayName = displayName.Trim();
        Email = email.Trim().ToLowerInvariant();
        Role = role.Trim();
    }

    public Guid UserId { get; }
    public string DisplayName { get; }
    public string Email { get; }
    public string Role { get; }
}
