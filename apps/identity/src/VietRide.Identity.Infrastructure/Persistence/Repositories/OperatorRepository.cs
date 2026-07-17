using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
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
    {
        var query = _dbContext.Operators.AsNoTracking();

        if (status.HasValue)
            query = query.Where(x => x.RegistrationStatus == status.Value);

        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var searchPattern = $"%{options.Search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, searchPattern)
                || EF.Functions.ILike(x.ContactEmail, searchPattern)
                || EF.Functions.ILike(x.ContactPhone, searchPattern)
                || EF.Functions.ILike(x.BusinessRegistrationNumber, searchPattern)
                || EF.Functions.ILike(x.TaxCode, searchPattern));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, options.SortBy, options.SortDir)
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Operator>.Create(items, options.Page, options.PageSize, totalItems);
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
        return sortBy?.Trim().ToLowerInvariant() switch
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
    }
}
