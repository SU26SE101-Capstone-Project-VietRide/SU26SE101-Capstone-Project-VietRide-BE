using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Commits failed-login tracking on a fresh DbContext scope so the write is not
/// rolled back by the login command's 401 exception.
/// </summary>
internal sealed class FailedLoginPersister : IFailedLoginPersister
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FailedLoginPersister(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task PersistAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var lockoutCounter = scope.ServiceProvider.GetRequiredService<ILoginLockoutCounter>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE id = {userId} AND deleted_at IS NULL FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);
        if (user is null)
            return;

        if (user.Status != UserStatus.ACTIVE
            && !(user.Status == UserStatus.PENDING_EMAIL_VERIFICATION && user.Role == UserRole.PASSENGER))
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var failedAttemptsInWindow = await lockoutCounter.IncrementAsync(userId, ct);
        user.RecordFailedLogin(clock, failedAttemptsInWindow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
