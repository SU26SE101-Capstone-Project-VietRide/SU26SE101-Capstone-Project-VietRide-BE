using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorFareSurchargeSettingRepository : IOperatorFareSurchargeSettingRepository
{
    private readonly TripDbContext _dbContext;

    public OperatorFareSurchargeSettingRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorFareSurchargeSetting?> GetByIdAsync(Guid id, CancellationToken ct)
        => GetByOperatorIdAsync(id, ct);

    public Task<OperatorFareSurchargeSetting?> GetByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => _dbContext.OperatorFareSurchargeSettings.FirstOrDefaultAsync(x => x.Id == operatorId, cancellationToken);

    public Task<OperatorFareSurchargeSetting> AddAsync(OperatorFareSurchargeSetting entity, CancellationToken ct)
    {
        _dbContext.OperatorFareSurchargeSettings.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorFareSurchargeSetting entity) => _dbContext.OperatorFareSurchargeSettings.Update(entity);

    public void Remove(OperatorFareSurchargeSetting entity) => _dbContext.OperatorFareSurchargeSettings.Remove(entity);

    public IQueryable<OperatorFareSurchargeSetting> Query() => _dbContext.OperatorFareSurchargeSettings;

    public IQueryable<OperatorFareSurchargeSetting> QueryNoTracking() => _dbContext.OperatorFareSurchargeSettings.AsNoTracking();
}
