using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Commits refresh-token family revocation on a fresh DbContext scope so reuse
/// detection is not rolled back when the handler throws UnauthorizedException.
/// </summary>
internal sealed class RefreshTokenFamilyRevoker : IRefreshTokenFamilyRevoker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RefreshTokenFamilyRevoker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task RevokeForReuseAsync(Guid familyId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var revokedAt = clock.UtcNow;

        var tokens = await db.RefreshTokens
            .Where(r => r.FamilyId == familyId)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.Revoke(revokedAt, RefreshTokenRevokeReason.REUSE_DETECTED);
        }

        await db.SaveChangesAsync(ct);
    }
}
