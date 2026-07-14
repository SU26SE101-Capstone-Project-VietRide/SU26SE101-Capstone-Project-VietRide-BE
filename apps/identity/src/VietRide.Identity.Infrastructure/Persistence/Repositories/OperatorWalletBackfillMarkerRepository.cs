using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class OperatorWalletBackfillMarkerRepository
    : IOperatorWalletBackfillMarkerRepository
{
    private readonly IdentityDbContext _db;

    public OperatorWalletBackfillMarkerRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public Task<OperatorWalletBackfillMarker?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => FindByOperatorIdAsync(id, cancellationToken);

    public async Task<OperatorWalletBackfillMarker> AddAsync(
        OperatorWalletBackfillMarker entity,
        CancellationToken cancellationToken = default)
    {
        await _db.OperatorWalletBackfillMarkers.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(OperatorWalletBackfillMarker entity) => _db.Update(entity);
    public void Remove(OperatorWalletBackfillMarker entity) => _db.Remove(entity);
    public IQueryable<OperatorWalletBackfillMarker> Query() => _db.OperatorWalletBackfillMarkers;
    public IQueryable<OperatorWalletBackfillMarker> QueryNoTracking() => Query().AsNoTracking();

    public Task<OperatorWalletBackfillMarker?> FindByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
        => _db.OperatorWalletBackfillMarkers.SingleOrDefaultAsync(
            marker => marker.OperatorId == operatorId,
            cancellationToken);
}
