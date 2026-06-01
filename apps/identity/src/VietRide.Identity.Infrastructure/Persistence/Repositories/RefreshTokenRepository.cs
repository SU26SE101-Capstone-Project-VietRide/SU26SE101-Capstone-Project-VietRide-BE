using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _db;
    private readonly IClock _clock;

    public RefreshTokenRepository(IdentityDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<RefreshToken?> GetByTokenHashAsync(string sha256Hex, CancellationToken ct = default)
        => await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == sha256Hex, ct);

    public async Task RevokeFamilyAsync(
        Guid familyId,
        RefreshTokenRevokeReason reason,
        CancellationToken ct = default)
    {
        var revokedAt = _clock.UtcNow;

        var tokens = await _db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.Revoke(revokedAt, reason);
        }
    }

    public async Task<RefreshToken> AddAsync(RefreshToken entity, CancellationToken ct)
    {
        await _db.RefreshTokens.AddAsync(entity, ct);
        return entity;
    }

    public void Update(RefreshToken entity)
        => _db.RefreshTokens.Update(entity);

    public void Remove(RefreshToken entity)
        => _db.RefreshTokens.Remove(entity);

    public IQueryable<RefreshToken> Query()
        => _db.RefreshTokens;

    public IQueryable<RefreshToken> QueryNoTracking()
        => _db.RefreshTokens.AsNoTracking();
}
