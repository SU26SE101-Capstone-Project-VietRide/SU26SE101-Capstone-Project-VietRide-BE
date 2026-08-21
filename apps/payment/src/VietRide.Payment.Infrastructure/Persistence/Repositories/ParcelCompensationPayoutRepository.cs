using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class ParcelCompensationPayoutRepository : IParcelCompensationPayoutRepository
{
    private readonly PaymentDbContext _db;

    public ParcelCompensationPayoutRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public Task<ParcelCompensationPayout?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.ParcelCompensationPayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ParcelCompensationPayout?> FindByClaimIdAsync(Guid claimId, CancellationToken cancellationToken)
        => _db.ParcelCompensationPayouts.FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);

    public async Task<ParcelCompensationPayout> AddAsync(ParcelCompensationPayout entity, CancellationToken ct)
    {
        await _db.ParcelCompensationPayouts.AddAsync(entity, ct);
        return entity;
    }

    public void Update(ParcelCompensationPayout entity) => _db.ParcelCompensationPayouts.Update(entity);
    public void Remove(ParcelCompensationPayout entity) => throw new NotSupportedException();
    public IQueryable<ParcelCompensationPayout> Query() => _db.ParcelCompensationPayouts;
    public IQueryable<ParcelCompensationPayout> QueryNoTracking() => _db.ParcelCompensationPayouts.AsNoTracking();
}
