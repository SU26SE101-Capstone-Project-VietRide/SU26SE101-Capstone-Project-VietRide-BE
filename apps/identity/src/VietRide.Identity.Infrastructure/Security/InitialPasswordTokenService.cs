using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

public sealed class InitialPasswordTokenService : IInitialPasswordTokenService
{
    public string GenerateCode()
        => Guid.NewGuid().ToString("D");

    public DateTimeOffset GetExpiresAt(DateTimeOffset now)
        => now.AddHours(48);
}
