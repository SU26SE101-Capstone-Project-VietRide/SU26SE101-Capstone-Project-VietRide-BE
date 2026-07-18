using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

internal sealed class RefreshSessionExecutor : IRefreshSessionExecutor
{
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;

    public RefreshSessionExecutor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<RefreshSessionResult> ExecuteAsync(
        string rawRefreshToken,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tokenFactory = scope.ServiceProvider.GetRequiredService<IRefreshTokenFactory>();
        var familyRevoker = scope.ServiceProvider.GetRequiredService<IRefreshTokenFamilyRevoker>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tokenHash = tokenFactory.ComputeHash(rawRefreshToken);

        var userIdHint = await db.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.UserId)
            .SingleOrDefaultAsync(ct);

        if (!userIdHint.HasValue)
            return RefreshSessionResult.Invalid("Refresh token is invalid.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE id = {userIdHint.Value} AND deleted_at IS NULL FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);

        if (user is null || user.Status != UserStatus.ACTIVE)
        {
            await transaction.CommitAsync(ct);
            return RefreshSessionResult.Invalid("Refresh token is invalid for the current account status.");
        }

        var existing = await db.RefreshTokens
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.refresh_tokens WHERE token_hash = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        if (existing is null || existing.UserId != user.Id)
        {
            await transaction.CommitAsync(ct);
            return RefreshSessionResult.Invalid("Refresh token is invalid.");
        }

        var now = clock.UtcNow;
        if (existing.RevokedAt is not null)
        {
            if (existing.RevokedReason == RefreshTokenRevokeReason.NORMAL_ROTATION
                && now - existing.RevokedAt.Value <= RotationGracePeriod)
            {
                await transaction.CommitAsync(ct);
                return RefreshSessionResult.Invalid("Refresh token was already rotated. Retry with the latest refresh token.");
            }

            var familyTokens = await db.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM vietride_identity.refresh_tokens WHERE family_id = {existing.FamilyId} ORDER BY id FOR UPDATE")
                .ToListAsync(ct);

            await familyRevoker.RevokeForReuseAsync(familyTokens, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return RefreshSessionResult.Invalid("Refresh token has already been used.");
        }

        if (existing.ExpiresAt <= now)
        {
            await transaction.CommitAsync(ct);
            return RefreshSessionResult.Invalid("Refresh token has expired.");
        }

        existing.Revoke(now, RefreshTokenRevokeReason.NORMAL_ROTATION);
        var (rawRefresh, newRefreshEntity) = tokenFactory.Create(
            user.Id,
            existing.Id,
            existing.FamilyId);

        await db.RefreshTokens.AddAsync(newRefreshEntity, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return RefreshSessionResult.Success(user, rawRefresh);
    }
}
