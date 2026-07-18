using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

internal sealed class PasswordResetSessionExecutor : IPasswordResetSessionExecutor
{
    private const int MaxFailedAttempts = 5;
    private readonly IServiceScopeFactory _scopeFactory;

    public PasswordResetSessionExecutor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<PasswordResetSessionResult> ExecuteAsync(
        Guid userId,
        string code,
        string passwordHash,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE id = {userId} AND deleted_at IS NULL FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);

        if (user is null || user.Status != UserStatus.ACTIVE)
        {
            await transaction.CommitAsync(ct);
            return new PasswordResetSessionResult(PasswordResetSessionStatus.INVALID_OTP);
        }

        var pendingTokens = await db.EmailVerificationTokens
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.email_verification_tokens WHERE user_id = {userId} AND purpose = 'PASSWORD_RESET' AND used_at IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

        var token = pendingTokens.FirstOrDefault(candidate => candidate.Code == code);
        if (token is null)
        {
            var latest = pendingTokens
                .OrderByDescending(candidate => candidate.CreatedAt)
                .ThenByDescending(candidate => candidate.Id)
                .FirstOrDefault();

            if (latest is not null)
            {
                latest.IncrementFailedAttempts();
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new PasswordResetSessionResult(PasswordResetSessionStatus.INVALID_OTP);
        }

        var now = clock.UtcNow;
        if (token.ExpiresAt <= now)
        {
            await transaction.CommitAsync(ct);
            return new PasswordResetSessionResult(PasswordResetSessionStatus.EXPIRED_OTP);
        }

        if (token.FailedAttempts >= MaxFailedAttempts)
        {
            await transaction.CommitAsync(ct);
            return new PasswordResetSessionResult(PasswordResetSessionStatus.INVALID_OTP);
        }

        user.ResetPassword(passwordHash);
        token.MarkUsed(now);

        var activeRefreshTokens = await db.RefreshTokens
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.refresh_tokens WHERE user_id = {userId} AND revoked_at IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

        foreach (var refreshToken in activeRefreshTokens)
            refreshToken.Revoke(now, RefreshTokenRevokeReason.PASSWORD_RESET);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new PasswordResetSessionResult(
            PasswordResetSessionStatus.SUCCEEDED,
            user.Id,
            user.Status.ToString());
    }
}
