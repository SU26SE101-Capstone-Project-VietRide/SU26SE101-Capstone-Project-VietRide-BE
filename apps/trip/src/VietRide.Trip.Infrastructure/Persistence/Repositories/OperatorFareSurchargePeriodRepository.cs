using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorFareSurchargePeriodRepository : IOperatorFareSurchargePeriodRepository
{
    private readonly TripDbContext _dbContext;

    public OperatorFareSurchargePeriodRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorFareSurchargePeriod?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.OperatorFareSurchargePeriods.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<OperatorFareSurchargePeriod?> GetOwnedByIdAsync(
        Guid operatorId,
        Guid periodId,
        CancellationToken cancellationToken = default)
        => _dbContext.OperatorFareSurchargePeriods.FirstOrDefaultAsync(
            x => x.Id == periodId && x.OperatorId == operatorId,
            cancellationToken);

    public Task<bool> ExistsActiveOverlapAsync(
        Guid operatorId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludedPeriodId,
        CancellationToken cancellationToken = default)
        => _dbContext.OperatorFareSurchargePeriods.AnyAsync(
            x => x.OperatorId == operatorId
                && x.IsActive
                && (!excludedPeriodId.HasValue || x.Id != excludedPeriodId.Value)
                && x.StartDate <= endDate
                && x.EndDate >= startDate,
            cancellationToken);

    public Task<OperatorFareSurchargePeriod?> GetActiveForDateAsync(
        Guid operatorId,
        DateOnly departureDate,
        CancellationToken cancellationToken = default)
        => _dbContext.OperatorFareSurchargePeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OperatorId == operatorId
                    && x.IsActive
                    && x.StartDate <= departureDate
                    && x.EndDate >= departureDate,
                cancellationToken);

    public Task<OperatorFareSurchargePeriod> AddAsync(OperatorFareSurchargePeriod entity, CancellationToken ct)
    {
        _dbContext.OperatorFareSurchargePeriods.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorFareSurchargePeriod entity) => _dbContext.OperatorFareSurchargePeriods.Update(entity);

    public void Remove(OperatorFareSurchargePeriod entity) => _dbContext.OperatorFareSurchargePeriods.Remove(entity);

    public IQueryable<OperatorFareSurchargePeriod> Query() => _dbContext.OperatorFareSurchargePeriods;

    public IQueryable<OperatorFareSurchargePeriod> QueryNoTracking() => _dbContext.OperatorFareSurchargePeriods.AsNoTracking();
}
