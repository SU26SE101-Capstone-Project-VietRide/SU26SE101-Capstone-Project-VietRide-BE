using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for the Voucher aggregate.
/// Implements <see cref="IVoucherRepository"/> — extends the generic repository contract
/// (<see cref="IRepository{TEntity,TId}"/>) with voucher-specific queries.
/// </summary>
internal sealed class VoucherRepository : IVoucherRepository
{
    private readonly BookingDbContext _db;

    public VoucherRepository(BookingDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // IRepository<Voucher, Guid>
    // -----------------------------------------------------------------------

    public async Task<Voucher?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<Voucher> AddAsync(Voucher entity, CancellationToken ct)
    {
        await _db.Vouchers.AddAsync(entity, ct);
        return entity;
    }

    public void Update(Voucher entity)
        => _db.Vouchers.Update(entity);

    public void Remove(Voucher entity)
        => _db.Vouchers.Remove(entity);

    public IQueryable<Voucher> Query()
        => _db.Vouchers;

    public IQueryable<Voucher> QueryNoTracking()
        => _db.Vouchers.AsNoTracking();

    // -----------------------------------------------------------------------
    // IVoucherRepository — aggregate-specific queries
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => await _db.Vouchers.AnyAsync(v => v.Code == code, ct);

    /// <inheritdoc/>
    public async Task<Voucher?> FindByCodeAsync(string code, CancellationToken ct = default)
        => await _db.Vouchers.FirstOrDefaultAsync(v => v.Code == code, ct);

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Voucher> Items, long Total)> ListAsync(
        Guid? ownerOperatorId,
        VoucherFundingType? fundingType,
        bool? isActive,
        int page,
        int pageSize,
        string? sortBy,
        string sortDir,
        CancellationToken ct = default)
    {
        var query = _db.Vouchers.AsNoTracking();

        if (ownerOperatorId.HasValue)
            query = query.Where(v => v.OwnerOperatorId == ownerOperatorId.Value);

        if (fundingType.HasValue)
            query = query.Where(v => v.FundingType == fundingType.Value);

        if (isActive.HasValue)
            query = query.Where(v => v.IsActive == isActive.Value);

        // Apply sort
        query = ApplySort(query, sortBy, sortDir);

        var total = await query.LongCountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc/>
    public async Task AddConsentAsync(OperatorVoucherConsent consent, CancellationToken ct = default)
    {
        await _db.OperatorVoucherConsents.AddAsync(consent, ct);
    }

    // -----------------------------------------------------------------------
    // Operator-scoped queries (Task 14.1b — appended; do not modify above)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<Voucher?> FindByIdAndOwnerAsync(Guid id, Guid ownerOperatorId, CancellationToken ct = default)
        => await _db.Vouchers
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerOperatorId == ownerOperatorId, ct);

    /// <inheritdoc/>
    public async Task<Voucher?> FindByIdAndOwnerIgnoringSoftDeleteAsync(Guid id, Guid ownerOperatorId, CancellationToken ct = default)
        => await _db.Vouchers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerOperatorId == ownerOperatorId, ct);

    /// <inheritdoc/>
    public async Task<int> CountUsagesAsync(Guid voucherId, CancellationToken ct = default)
        => await _db.VoucherUsages.CountAsync(u => u.VoucherId == voucherId, ct);

    // -----------------------------------------------------------------------
    // VoucherUsage methods (Task 14.3 — checkout record + compensation)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task AddUsageAsync(VoucherUsage usage, CancellationToken ct = default)
        => await _db.VoucherUsages.AddAsync(usage, ct);

    /// <inheritdoc/>
    public async Task<int> CountUsagesByUserAsync(Guid voucherId, Guid userId, CancellationToken ct = default)
        => await _db.VoucherUsages.CountAsync(u => u.VoucherId == voucherId && u.UserId == userId, ct);

    /// <inheritdoc/>
    public async Task DeleteUsageByBookingAsync(Guid bookingId, CancellationToken ct = default)
    {
        var usage = await _db.VoucherUsages
            .FirstOrDefaultAsync(u => u.BookingId == bookingId, ct);
        if (usage is not null)
            _db.VoucherUsages.Remove(usage);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static IQueryable<Voucher> ApplySort(
        IQueryable<Voucher> query,
        string? sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "validFrom" => descending
                ? query.OrderByDescending(v => v.ValidFrom)
                : query.OrderBy(v => v.ValidFrom),
            "validUntil" => descending
                ? query.OrderByDescending(v => v.ValidUntil)
                : query.OrderBy(v => v.ValidUntil),
            "code" => descending
                ? query.OrderByDescending(v => v.Code)
                : query.OrderBy(v => v.Code),
            "name" => descending
                ? query.OrderByDescending(v => v.Name)
                : query.OrderBy(v => v.Name),
            "isActive" => descending
                ? query.OrderByDescending(v => v.IsActive)
                : query.OrderBy(v => v.IsActive),
            // default: createdAt desc
            _ => descending
                ? query.OrderByDescending(v => v.CreatedAt)
                : query.OrderBy(v => v.CreatedAt),
        };
    }
}
