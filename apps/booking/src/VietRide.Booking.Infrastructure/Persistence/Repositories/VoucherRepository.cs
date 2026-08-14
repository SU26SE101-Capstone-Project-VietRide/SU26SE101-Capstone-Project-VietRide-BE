using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Vouchers.GetVoucherSummary;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
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
    public async Task<Voucher?> FindPlatformByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Vouchers
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerOperatorId == null, ct);

    /// <inheritdoc/>
    public async Task<Voucher?> FindPlatformByIdIgnoringSoftDeleteAsync(Guid id, CancellationToken ct = default)
        => await _db.Vouchers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerOperatorId == null, ct);

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Voucher> Items, long Total)> ListAsync(
        Guid? ownerOperatorId,
        bool platformOnly,
        VoucherFundingType? fundingType,
        bool? isActive,
        int page,
        int pageSize,
        string? sortBy,
        string sortDir,
        CancellationToken ct = default,
        string? search = null,
        string? service = null,
        VoucherType? type = null,
        DateTimeOffset? validFromInclusive = null,
        DateTimeOffset? validUntilExclusive = null)
    {
        var query = _db.Vouchers.AsNoTracking();

        if (platformOnly)
            query = query.Where(v => v.OwnerOperatorId == null);
        else if (ownerOperatorId.HasValue)
            query = query.Where(v => v.OwnerOperatorId == ownerOperatorId.Value);

        if (fundingType.HasValue)
            query = query.Where(v => v.FundingType == fundingType.Value);

        if (isActive.HasValue)
            query = query.Where(v => v.IsActive == isActive.Value);

        if (type.HasValue)
            query = query.Where(v => v.Type == type.Value);

        if (validFromInclusive.HasValue && validUntilExclusive.HasValue)
            query = query.Where(v => v.ValidFrom < validUntilExclusive.Value
                && v.ValidUntil >= validFromInclusive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(v => EF.Functions.ILike(v.Code, pattern, "\\")
                || EF.Functions.ILike(v.Name, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(service))
            query = query.Where(v => v.ApplicableServices.Contains(service));

        var total = await query.LongCountAsync(ct);

        query = ApplySort(query, sortBy, sortDir);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetUsageCountsAsync(
        IReadOnlyCollection<Guid> voucherIds,
        CancellationToken ct = default)
        => await _db.VoucherUsages.AsNoTracking()
            .Where(usage => voucherIds.Contains(usage.VoucherId))
            .GroupBy(usage => usage.VoucherId)
            .Select(group => new { VoucherId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.VoucherId, row => row.Count, ct);

    public async Task<VoucherSummaryResult> GetSummaryAsync(
        Guid? ownerOperatorId,
        bool platformOnly,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.Vouchers.AsNoTracking();
        query = platformOnly
            ? query.Where(v => v.OwnerOperatorId == null)
            : query.Where(v => v.OwnerOperatorId == ownerOperatorId);

        var expiresAt = now.AddDays(7);
        var result = await query.GroupBy(_ => 1)
            .Select(group => new VoucherSummaryResult(
                group.Count(),
                group.Count(v => v.IsActive),
                group.Count(v => v.ApplicableServices.Contains("BOOKING")),
                group.Count(v => v.ApplicableServices.Contains("PARCEL")),
                group.Count(v => v.IsActive
                    && v.ValidFrom <= now
                    && v.ValidUntil >= now
                    && v.ValidUntil <= expiresAt)))
            .SingleOrDefaultAsync(ct);

        return result ?? new VoucherSummaryResult(0, 0, 0, 0, 0);
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
            .FirstOrDefaultAsync(u => u.ReferenceType == "BOOKING" && u.ReferenceId == bookingId, ct);
        if (usage is not null)
            _db.VoucherUsages.Remove(usage);
    }

    /// <inheritdoc/>
    public async Task DeleteUsageByReferenceAsync(string referenceType, Guid referenceId, CancellationToken ct = default)
    {
        var normalized = referenceType.Trim().ToUpperInvariant();
        var usage = await _db.VoucherUsages
            .FirstOrDefaultAsync(u => u.ReferenceType == normalized && u.ReferenceId == referenceId, ct);
        if (usage is not null)
            _db.VoucherUsages.Remove(usage);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private IQueryable<Voucher> ApplySort(
        IQueryable<Voucher> query,
        string? sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "validFrom" => descending
                ? query.OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.ValidFrom).ThenBy(v => v.Id),
            "validUntil" => descending
                ? query.OrderByDescending(v => v.ValidUntil).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.ValidUntil).ThenBy(v => v.Id),
            "code" => descending
                ? query.OrderByDescending(v => v.Code).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.Code).ThenBy(v => v.Id),
            "name" => descending
                ? query.OrderByDescending(v => v.Name).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.Name).ThenBy(v => v.Id),
            "isActive" => descending
                ? query.OrderByDescending(v => v.IsActive).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.IsActive).ThenBy(v => v.Id),
            "usedCount" => descending
                ? query.OrderByDescending(v => _db.VoucherUsages.Count(usage => usage.VoucherId == v.Id)).ThenByDescending(v => v.Id)
                : query.OrderBy(v => _db.VoucherUsages.Count(usage => usage.VoucherId == v.Id)).ThenBy(v => v.Id),
            // default: createdAt desc
            _ => descending
                ? query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
                : query.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id),
        };
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
