using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for the OperatorVoucherConsent aggregate.
/// Implements <see cref="IOperatorVoucherConsentRepository"/> — extends the generic repository
/// contract (<see cref="IRepository{TEntity,TId}"/>) with consent-specific queries.
/// </summary>
internal sealed class OperatorVoucherConsentRepository : IOperatorVoucherConsentRepository
{
    private readonly BookingDbContext _db;

    public OperatorVoucherConsentRepository(BookingDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // IRepository<OperatorVoucherConsent, Guid>
    // -----------------------------------------------------------------------

    public async Task<OperatorVoucherConsent?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.OperatorVoucherConsents.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<OperatorVoucherConsent> AddAsync(OperatorVoucherConsent entity, CancellationToken ct)
    {
        await _db.OperatorVoucherConsents.AddAsync(entity, ct);
        return entity;
    }

    public void Update(OperatorVoucherConsent entity)
        => _db.OperatorVoucherConsents.Update(entity);

    public void Remove(OperatorVoucherConsent entity)
        => _db.OperatorVoucherConsents.Remove(entity);

    public IQueryable<OperatorVoucherConsent> Query()
        => _db.OperatorVoucherConsents;

    public IQueryable<OperatorVoucherConsent> QueryNoTracking()
        => _db.OperatorVoucherConsents.AsNoTracking();

    // -----------------------------------------------------------------------
    // IOperatorVoucherConsentRepository — aggregate-specific queries
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<OperatorVoucherConsent?> FindByIdAndOperatorAsync(
        Guid id,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.OperatorVoucherConsents
            .FirstOrDefaultAsync(c => c.Id == id && c.OperatorId == operatorId, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OperatorVoucherConsent>> ListByOperatorAsync(
        Guid operatorId,
        OperatorVoucherConsentStatus? status,
        CancellationToken ct = default)
    {
        var query = _db.OperatorVoucherConsents
            .AsNoTracking()
            .Include(c => c.Voucher)
            .Where(c => c.OperatorId == operatorId);

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        return await query
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OperatorVoucherConsent>> ListByVoucherAsync(
        Guid voucherId,
        CancellationToken ct = default)
        => await _db.OperatorVoucherConsents
            .AsNoTracking()
            .Where(c => c.VoucherId == voucherId)
            .OrderBy(c => c.RequestedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<OperatorVoucherConsent?> FindAcceptedByVoucherAndOperatorAsync(
        Guid voucherId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.OperatorVoucherConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.VoucherId == voucherId
                && c.OperatorId == operatorId
                && c.Status == OperatorVoucherConsentStatus.ACCEPTED,
                ct);
}
