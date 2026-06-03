using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions;
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
        long failedAttemptsInWindow,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return;

        user.RecordFailedLogin(clock, failedAttemptsInWindow);
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }
}
