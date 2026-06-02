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
/// Design: a fresh <see cref="IServiceScope"/> is created per call, giving a fresh
/// <see cref="IdentityDbContext"/> with no ambient transaction. <c>SaveChangesAsync</c>
/// on that context issues a standalone UPDATE + auto-commit (no explicit transaction needed
/// for a single-row write). The scope is disposed before the method returns.
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

        var token = await db.EmailVerificationTokens
            .Where(e => e.UserId == userId && e.Purpose == purpose && e.UsedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (token is null)
            return;

        token.IncrementFailedAttempts();
        db.EmailVerificationTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }
}
