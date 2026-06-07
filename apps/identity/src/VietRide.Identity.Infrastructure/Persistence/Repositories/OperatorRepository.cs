using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

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
}
