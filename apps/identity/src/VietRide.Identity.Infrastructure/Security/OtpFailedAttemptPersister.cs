using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Persists an OTP <c>failed_attempts</c> increment on a FRESH <see cref="IdentityDbContext"/>
/// scope so the write commits independently of the ambient request transaction.
///
/// Background: the verify-email handler runs inside <c>TransactionBehavior</c>, which rolls
/// back the entire transaction on any exception. Without this persister the increment would
/// be lost every time a wrong code is submitted, neutralising the anti-brute-force control.
///
/// Design: a fresh scope starts a User-first transaction, then locks pending OTP rows in UUID
/// order. This keeps OTP failure persistence linear with admin lock and password reset.
/// </summary>
internal sealed class OtpFailedAttemptPersister : IOtpFailedAttemptPersister
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OtpFailedAttemptPersister(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task PersistAsync(
        Guid userId,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE id = {userId} AND deleted_at IS NULL FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);

        var statusEligible = purpose switch
        {
            EmailVerificationPurpose.PASSWORD_RESET => user?.Status == UserStatus.ACTIVE,
            EmailVerificationPurpose.REGISTRATION => user?.Status == UserStatus.PENDING_EMAIL_VERIFICATION,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD => user?.Status == UserStatus.PENDING_INITIAL_PASSWORD,
            _ => false,
        };

        if (!statusEligible)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var tokens = await db.EmailVerificationTokens
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.email_verification_tokens WHERE user_id = {userId} AND purpose = {purpose} AND used_at IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
        var token = tokens
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();

        if (token is null)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        token.IncrementFailedAttempts();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
