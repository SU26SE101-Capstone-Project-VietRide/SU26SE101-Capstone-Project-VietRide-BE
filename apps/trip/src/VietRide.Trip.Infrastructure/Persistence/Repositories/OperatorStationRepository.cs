using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorStationRepository : IOperatorStationRepository
{
    private readonly TripDbContext _dbContext;

    public OperatorStationRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorStation?> GetByIdAsync(Guid id, CancellationToken ct)
        => _dbContext.OperatorStations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<OperatorStation> AddAsync(OperatorStation entity, CancellationToken ct)
    {
        _dbContext.OperatorStations.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorStation entity)
        => _dbContext.OperatorStations.Update(entity);

    public void Remove(OperatorStation entity)
        => _dbContext.OperatorStations.Remove(entity);

    public IQueryable<OperatorStation> Query()
        => _dbContext.OperatorStations;

    public IQueryable<OperatorStation> QueryNoTracking()
        => _dbContext.OperatorStations.AsNoTracking();

    public Task<bool> ExistsActiveAsync(
        Guid operatorId,
        Guid stationId,
        CancellationToken cancellationToken)
        => _dbContext.OperatorStations.AsNoTracking().AnyAsync(
            item => item.OperatorId == operatorId
                && item.StationId == stationId
                && item.IsActive,
            cancellationToken);

    public Task<OperatorStation?> AcquireActiveForRouteProposalApprovalAsync(
        Guid operatorId,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required.");
        foreach (var local in _dbContext.OperatorStations.Local
                     .Where(item => item.OperatorId == operatorId && item.StationId == stationId)
                     .ToArray())
        {
            _dbContext.Entry(local).State = EntityState.Detached;
        }

        return _dbContext.OperatorStations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.operator_stations WHERE operator_id = {operatorId} AND station_id = {stationId} AND is_active = TRUE ORDER BY id FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<(int RelinkedCount, int CollapsedCount)> RelinkForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
    {
        var mappings = await _dbContext.OperatorStations
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.operator_stations WHERE station_id IN ({duplicateStationId}, {primaryStationId}) ORDER BY operator_id::text, id::text FOR UPDATE")
            .ToListAsync(cancellationToken);
        var primaryByOperator = mappings
            .Where(mapping => mapping.StationId == primaryStationId)
            .ToDictionary(mapping => mapping.OperatorId);
        var duplicateMappings = mappings
            .Where(mapping => mapping.StationId == duplicateStationId)
            .ToArray();
        var relinkedCount = 0;
        var collapsedCount = 0;
        foreach (var duplicateMapping in duplicateMappings)
        {
            if (primaryByOperator.TryGetValue(duplicateMapping.OperatorId, out var primaryMapping))
            {
                primaryMapping.MergeConfigurationFrom(duplicateMapping);
                _dbContext.OperatorStations.Remove(duplicateMapping);
                collapsedCount++;
            }
            else
            {
                duplicateMapping.RelinkToStation(primaryStationId);
                relinkedCount++;
            }
        }

        return (relinkedCount, collapsedCount);
    }
}
