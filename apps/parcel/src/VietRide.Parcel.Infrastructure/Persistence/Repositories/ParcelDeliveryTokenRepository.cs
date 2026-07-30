using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelDeliveryTokenRepository : IParcelDeliveryTokenRepository
{
    private readonly ParcelDbContext _db;

    public ParcelDeliveryTokenRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public async Task<ParcelDeliveryToken?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
        => await _db.ParcelDeliveryTokens
            .FirstOrDefaultAsync(token => token.Id == id, cancellationToken);

    public async Task<ParcelDeliveryToken> AddAsync(
        ParcelDeliveryToken entity,
        CancellationToken cancellationToken)
    {
        await _db.ParcelDeliveryTokens.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(ParcelDeliveryToken entity)
        => _db.ParcelDeliveryTokens.Update(entity);

    public void Remove(ParcelDeliveryToken entity)
        => _db.ParcelDeliveryTokens.Remove(entity);

    public IQueryable<ParcelDeliveryToken> Query()
        => _db.ParcelDeliveryTokens;

    public IQueryable<ParcelDeliveryToken> QueryNoTracking()
        => _db.ParcelDeliveryTokens.AsNoTracking();

    public async Task<ParcelDeliveryToken?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
        => await _db.ParcelDeliveryTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task<ParcelDeliveryToken?> FindActiveByParcelIdAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
        => await _db.ParcelDeliveryTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                token => token.ParcelId == parcelId && token.RevokedAt == null,
                cancellationToken);

    public async Task<bool> RevokeActiveAsync(
        Guid parcelId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var affected = await _db.ParcelDeliveryTokens
            .Where(token => token.ParcelId == parcelId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.UpdatedAt, revokedAt), cancellationToken);

        return affected > 0;
    }

    public async Task<bool> RevokeAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var affected = await _db.ParcelDeliveryTokens
            .Where(token => token.Id == tokenId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.UpdatedAt, revokedAt), cancellationToken);

        return affected > 0;
    }
}
