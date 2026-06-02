using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class EmailVerificationToken : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public EmailVerificationPurpose Purpose { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private EmailVerificationToken() { }

    public static EmailVerificationToken Create(
        Guid userId,
        EmailVerificationPurpose purpose,
        string code,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            Code = code,
            ExpiresAt = expiresAt,
            FailedAttempts = 0,
        };
    }

    public void IncrementFailedAttempts()
    {
        FailedAttempts++;
    }

    public void MarkUsed(DateTimeOffset usedAt)
    {
        UsedAt = usedAt;
    }
}
