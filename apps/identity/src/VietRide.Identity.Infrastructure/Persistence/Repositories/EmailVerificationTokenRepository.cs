using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class EmailVerificationTokenRepository : IEmailVerificationTokenRepository
{
    private readonly IdentityDbContext _db;

    public EmailVerificationTokenRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<EmailVerificationToken?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.EmailVerificationTokens.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<EmailVerificationToken?> FindActiveAsync(
        Guid userId,
        string code,
        EmailVerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken ct = default)
        => await _db.EmailVerificationTokens
            .FirstOrDefaultAsync(
                e => e.UserId == userId
                     && e.Code == code
                     && e.Purpose == purpose
                     && e.UsedAt == null
                     && e.ExpiresAt > now
                     && e.FailedAttempts < 5,
                ct);

    /// <inheritdoc />
    public async Task<EmailVerificationToken?> FindByCodeAsync(
        Guid userId,
        string code,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default)
        => await _db.EmailVerificationTokens
            .FirstOrDefaultAsync(
                e => e.UserId == userId
                     && e.Code == code
                     && e.Purpose == purpose
                     && e.UsedAt == null,
                ct);

    /// <inheritdoc />
    public async Task<EmailVerificationToken?> FindByCodeAndPurposeAsync(
        string code,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default)
        => await QueryByCodeAndPurpose(_db.EmailVerificationTokens, code, purpose)
            .FirstOrDefaultAsync(ct);

    internal static IQueryable<EmailVerificationToken> QueryByCodeAndPurpose(
        IQueryable<EmailVerificationToken> tokens,
        string code,
        EmailVerificationPurpose purpose)
        => tokens.Where(e => e.Code == code && e.Purpose == purpose && e.UsedAt == null);

    /// <inheritdoc />
    public async Task<EmailVerificationToken?> FindLatestPendingAsync(
        Guid userId,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default)
        => await _db.EmailVerificationTokens
            .Where(e => e.UserId == userId && e.Purpose == purpose && e.UsedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task RevokeActiveByUserAndPurposeAsync(
        Guid userId,
        EmailVerificationPurpose purpose,
        DateTimeOffset revokedAt,
        CancellationToken ct = default)
    {
        await _db.EmailVerificationTokens
            .Where(e => e.UserId == userId && e.Purpose == purpose && e.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.UsedAt, revokedAt), ct);
    }

    public async Task<EmailVerificationToken> AddAsync(EmailVerificationToken entity, CancellationToken ct)
    {
        await _db.EmailVerificationTokens.AddAsync(entity, ct);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(EmailVerificationToken entity, CancellationToken ct)
    {
        await _db.EmailVerificationTokens.AddAsync(entity, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Detach the failed entity so EF does not try to re-insert it on
            // the next SaveChanges call (e.g. the outer transaction commit).
            _db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("23505") == true
           || ex.InnerException?.Message.Contains("unique") == true
           || ex.InnerException?.Message.Contains("duplicate key") == true;

    public void Update(EmailVerificationToken entity)
        => _db.EmailVerificationTokens.Update(entity);

    public void Remove(EmailVerificationToken entity)
        => _db.EmailVerificationTokens.Remove(entity);

    public IQueryable<EmailVerificationToken> Query()
        => _db.EmailVerificationTokens;

    public IQueryable<EmailVerificationToken> QueryNoTracking()
        => _db.EmailVerificationTokens.AsNoTracking();
}
