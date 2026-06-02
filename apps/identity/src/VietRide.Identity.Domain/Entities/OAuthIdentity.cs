using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class OAuthIdentity : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public OAuthProvider Provider { get; private set; }
    public string ProviderSubject { get; private set; } = string.Empty;
    public string? ProviderEmail { get; private set; }
    public DateTimeOffset LinkedAt { get; private set; }

    private OAuthIdentity() { }

    public static OAuthIdentity Create(
        Guid userId,
        OAuthProvider provider,
        string providerSubject,
        string? providerEmail,
        DateTimeOffset linkedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);

        return new OAuthIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            ProviderSubject = providerSubject,
            ProviderEmail = providerEmail,
            LinkedAt = linkedAt,
        };
    }
}
