using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class RouteChangeProposalRepository : IRouteChangeProposalRepository
{
    private readonly TripDbContext dbContext;
    public RouteChangeProposalRepository(TripDbContext dbContext) => this.dbContext = dbContext;
    public Task<RouteChangeProposal?> GetByIdAsync(Guid id, CancellationToken ct = default) => dbContext.RouteChangeProposals.FindAsync([id], ct).AsTask();
    public Task<RouteChangeProposal> AddAsync(RouteChangeProposal entity, CancellationToken ct = default) { dbContext.RouteChangeProposals.Add(entity); return Task.FromResult(entity); }
    public void Update(RouteChangeProposal entity) => dbContext.RouteChangeProposals.Update(entity);
    public void Remove(RouteChangeProposal entity) => dbContext.RouteChangeProposals.Remove(entity);
    public IQueryable<RouteChangeProposal> Query() => dbContext.RouteChangeProposals;
    public IQueryable<RouteChangeProposal> QueryNoTracking() => dbContext.RouteChangeProposals.AsNoTracking();
    public async Task AcquireSourceCoordinationLockAsync(Guid sourceAlternativeRouteId, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        var lockKey = BitConverter.ToInt64(sourceAlternativeRouteId.ToByteArray(), 0);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
    }
    public Task<RouteChangeProposal?> GetOwnedByIdAsync(Guid operatorId, Guid proposalId, CancellationToken ct)
        => QueryWithStopsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId && x.OperatorId == operatorId, ct);
    public IQueryable<RouteChangeProposal> QueryWithStopsNoTracking()
        => dbContext.RouteChangeProposals.AsNoTracking().Include(x => x.Stops);
    public async Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingByTripAsync(Guid tripId, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        return await dbContext.RouteChangeProposals.FromSqlInterpolated($"SELECT * FROM vietride_trip.route_change_proposals WHERE trip_id = {tripId} AND status = 'PENDING' ORDER BY id FOR UPDATE").ToListAsync(ct);
    }
    public async Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingBySourceAsync(Guid sourceId, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id IN (SELECT trip_id FROM vietride_trip.route_change_proposals WHERE source_alternative_route_id = {sourceId} AND status = 'PENDING') ORDER BY id FOR UPDATE",
            ct);
        return await dbContext.RouteChangeProposals.FromSqlInterpolated(
            $"SELECT * FROM vietride_trip.route_change_proposals WHERE source_alternative_route_id = {sourceId} AND status = 'PENDING' ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
    }
    public Task LoadStopsAsync(RouteChangeProposal proposal, CancellationToken ct)
        => dbContext.Entry(proposal).Collection(x => x.Stops).LoadAsync(ct);
}
