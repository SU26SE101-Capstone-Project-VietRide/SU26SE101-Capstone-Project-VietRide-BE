using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.GetOperatorSummary;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class OperatorRepository : IOperatorRepository
{
    private readonly IdentityDbContext _dbContext;

    public OperatorRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Operator?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Operators.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Operator?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Operators.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Operators.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken);

    public Task<Operator> AddAsync(Operator entity, CancellationToken cancellationToken = default)
    {
        _dbContext.Operators.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Operator entity)
        => _dbContext.Operators.Update(entity);

    public void Remove(Operator entity)
        => _dbContext.Operators.Remove(entity);

    public IQueryable<Operator> Query()
        => _dbContext.Operators;

    public IQueryable<Operator> QueryNoTracking()
        => _dbContext.Operators.AsNoTracking();

    public Task<Operator?> GetByBusinessRegistrationNumberAsync(
        string businessRegistrationNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = businessRegistrationNumber.Trim();
        return _dbContext.Operators.FirstOrDefaultAsync(
            x => x.BusinessRegistrationNumber == normalized,
            cancellationToken);
    }

    public Task<Operator?> GetByTaxCodeAsync(
        string taxCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = taxCode.Trim();
        return _dbContext.Operators.FirstOrDefaultAsync(
            x => x.TaxCode == normalized,
            cancellationToken);
    }

    public async Task<PagedResult<Operator>> ListAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        CancellationToken cancellationToken = default)
        => await ListFilteredAsync(options, status, cancellationToken: cancellationToken);

    public async Task<PagedResult<Operator>> ListFilteredAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        bool? isActive = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtcExclusive = null,
        string dateField = "createdAt",
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilters(
            _dbContext.Operators.AsNoTracking(), options.Search, status, isActive,
            fromUtc, toUtcExclusive, dateField);

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, options.SortBy, options.SortDir)
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Operator>.Create(items, options.Page, options.PageSize, totalItems);
    }

    public async Task<IReadOnlyList<Operator>> ListForExportAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        bool? isActive,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string dateField,
        CancellationToken cancellationToken = default)
        => await ApplySort(
                ApplyFilters(_dbContext.Operators.AsNoTracking(), options.Search, status,
                    isActive, fromUtc, toUtcExclusive, dateField),
                options.SortBy,
                options.SortDir)
            .ToListAsync(cancellationToken);

    public async Task<AdminOperatorSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.Operators.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new AdminOperatorSummaryDto(
                group.Count(),
                group.Count(x => x.RegistrationStatus == OperatorRegistrationStatus.PENDING),
                group.Count(x => x.RegistrationStatus == OperatorRegistrationStatus.APPROVED),
                group.Count(x => x.RegistrationStatus == OperatorRegistrationStatus.SUSPENDED),
                group.Count(x => x.RegistrationStatus == OperatorRegistrationStatus.REJECTED),
                group.Count(x => x.IsActive)))
            .SingleOrDefaultAsync(cancellationToken);
        return result ?? new AdminOperatorSummaryDto(0, 0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<Operator>> ListSummariesByIdsAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken = default)
    {
        if (operatorIds.Count == 0)
            return [];

        return await _dbContext.Operators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(operatorTenant => operatorIds.Contains(operatorTenant.Id))
            .OrderBy(operatorTenant => operatorTenant.Id)
            .ToListAsync(cancellationToken);
    }

    private static IOrderedQueryable<Operator> ApplySort(
        IQueryable<Operator> query,
        string? sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            "contactemail" => descending ? query.OrderByDescending(x => x.ContactEmail) : query.OrderBy(x => x.ContactEmail),
            "contactphone" => descending ? query.OrderByDescending(x => x.ContactPhone) : query.OrderBy(x => x.ContactPhone),
            "businessregistrationnumber" => descending ? query.OrderByDescending(x => x.BusinessRegistrationNumber) : query.OrderBy(x => x.BusinessRegistrationNumber),
            "taxcode" => descending ? query.OrderByDescending(x => x.TaxCode) : query.OrderBy(x => x.TaxCode),
            "registrationstatus" => descending ? query.OrderByDescending(x => x.RegistrationStatus) : query.OrderBy(x => x.RegistrationStatus),
            "isactive" => descending ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            "approvedat" => descending ? query.OrderByDescending(x => x.ApprovedAt) : query.OrderBy(x => x.ApprovedAt),
            "suspendedat" => descending ? query.OrderByDescending(x => x.SuspendedAt) : query.OrderBy(x => x.SuspendedAt),
            _ => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
        };

        return descending ? ordered.ThenByDescending(x => x.Id) : ordered.ThenBy(x => x.Id);
    }

    private static IQueryable<Operator> ApplyFilters(
        IQueryable<Operator> query,
        string? search,
        OperatorRegistrationStatus? status,
        bool? isActive,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string dateField)
    {
        if (status.HasValue)
            query = query.Where(x => x.RegistrationStatus == status.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\")
                || EF.Functions.ILike(x.ContactEmail, pattern, "\\")
                || EF.Functions.ILike(x.ContactPhone, pattern, "\\")
                || EF.Functions.ILike(x.BusinessRegistrationNumber, pattern, "\\")
                || EF.Functions.ILike(x.TaxCode, pattern, "\\"));
        }

        var approvedAt = dateField.Equals("approvedAt", StringComparison.OrdinalIgnoreCase);
        if (fromUtc.HasValue)
            query = approvedAt
                ? query.Where(x => x.ApprovedAt >= fromUtc.Value)
                : query.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            query = approvedAt
                ? query.Where(x => x.ApprovedAt < toUtcExclusive.Value)
                : query.Where(x => x.CreatedAt < toUtcExclusive.Value);
        return query;
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
