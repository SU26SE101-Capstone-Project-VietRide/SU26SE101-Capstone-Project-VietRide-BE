using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class IncidentRepository : IIncidentRepository
{
    private readonly TripDbContext _dbContext;

    public IncidentRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Incidents.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<Incident> AddAsync(Incident entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.Incidents.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(Incident entity) => _dbContext.Incidents.Update(entity);

    public void Remove(Incident entity) => _dbContext.Incidents.Remove(entity);

    public IQueryable<Incident> Query() => _dbContext.Incidents;

    public IQueryable<Incident> QueryNoTracking() => _dbContext.Incidents.AsNoTracking();

    public async Task<PagedResult<OperatorIncidentReadRow>> ListOperatorIncidentsAsync(
        Guid operatorId,
        Guid? tripId,
        IncidentCategory? category,
        bool? resolved,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildOperatorProjectionQuery(operatorId);
        if (tripId.HasValue)
        {
            query = query.Where(row => row.TripId == tripId.Value);
        }

        if (category.HasValue)
        {
            query = query.Where(row => row.Category == category.Value);
        }

        if (resolved.HasValue)
        {
            query = resolved.Value
                ? query.Where(row => row.ResolvedAt != null)
                : query.Where(row => row.ResolvedAt == null);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(row => row.ReportedAt >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            query = query.Where(row => row.ReportedAt < toUtcExclusive.Value);
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var projections = await query
            .OrderByDescending(row => row.ReportedAt)
            .ThenBy(row => row.IncidentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var items = projections.Select(MapReadRow).ToArray();
        return PagedResult<OperatorIncidentReadRow>.Create(items, page, pageSize, totalItems);
    }

    public async Task<OperatorIncidentReadRow?> GetOperatorIncidentAsync(
        Guid operatorId,
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var projection = await BuildOperatorProjectionQuery(operatorId)
            .SingleOrDefaultAsync(row => row.IncidentId == incidentId, cancellationToken);
        return projection is null ? null : MapReadRow(projection);
    }

    public Task<Incident?> AcquireOperatorIncidentAsync(
        Guid operatorId,
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A transaction is required to resolve an Incident.");
        }

        var tracked = _dbContext.Incidents.Local.FirstOrDefault(incident => incident.Id == incidentId);
        if (tracked is not null)
        {
            _dbContext.Entry(tracked).State = EntityState.Detached;
        }

        return _dbContext.Incidents
            .FromSqlInterpolated($"""
                SELECT incident.*
                FROM vietride_trip.incidents AS incident
                INNER JOIN vietride_trip.trips AS trip ON trip.id = incident.trip_id
                WHERE incident.id = {incidentId}
                  AND trip.operator_id = {operatorId}
                FOR UPDATE OF incident
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<OperatorIncidentProjection> BuildOperatorProjectionQuery(Guid operatorId)
        => from incident in _dbContext.Incidents.AsNoTracking()
           join trip in _dbContext.Trips.AsNoTracking() on incident.TripId equals trip.Id
           join route in _dbContext.Routes.IgnoreQueryFilters().AsNoTracking() on trip.RouteId equals route.Id
           join origin in _dbContext.Stations.IgnoreQueryFilters().AsNoTracking() on route.OriginStationId equals origin.Id
           join destination in _dbContext.Stations.IgnoreQueryFilters().AsNoTracking() on route.DestinationStationId equals destination.Id
           where trip.OperatorId == operatorId
           select new OperatorIncidentProjection
           {
               IncidentId = incident.Id,
               TripId = incident.TripId,
               Category = incident.Category,
               Description = incident.Description,
               PhotoUrls = incident.PhotoUrls,
               Latitude = incident.Latitude,
               Longitude = incident.Longitude,
               ReportedAt = incident.ReportedAt,
               ResolvedAt = incident.ResolvedAt,
               ResolvedByUserId = incident.ResolvedByUserId,
               ResolutionNote = incident.ResolutionNote,
               ReportedByUserId = incident.ReportedByUserId,
               TripStatus = trip.Status,
               DepartureDateTime = trip.DepartureDateTime,
               RouteId = route.Id,
               RouteName = route.Name,
               OriginStationId = origin.Id,
               OriginStationName = origin.Name,
               DestinationStationId = destination.Id,
               DestinationStationName = destination.Name,
           };

    private static OperatorIncidentReadRow MapReadRow(OperatorIncidentProjection row)
        => new(
            row.IncidentId,
            row.TripId,
            row.Category,
            row.Description,
            row.PhotoUrls,
            row.Latitude,
            row.Longitude,
            row.ReportedAt,
            row.ResolvedAt,
            row.ResolvedByUserId,
            row.ResolutionNote,
            row.ReportedByUserId,
            row.TripStatus,
            row.DepartureDateTime,
            row.RouteId,
            row.RouteName,
            row.OriginStationId,
            row.OriginStationName,
            row.DestinationStationId,
            row.DestinationStationName);

    private sealed class OperatorIncidentProjection
    {
        public Guid IncidentId { get; init; }
        public Guid TripId { get; init; }
        public IncidentCategory Category { get; init; }
        public string? Description { get; init; }
        public IReadOnlyCollection<string>? PhotoUrls { get; init; }
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }
        public DateTimeOffset ReportedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public Guid? ResolvedByUserId { get; init; }
        public string? ResolutionNote { get; init; }
        public Guid ReportedByUserId { get; init; }
        public TripStatus TripStatus { get; init; }
        public DateTimeOffset DepartureDateTime { get; init; }
        public Guid RouteId { get; init; }
        public string RouteName { get; init; } = string.Empty;
        public Guid OriginStationId { get; init; }
        public string OriginStationName { get; init; } = string.Empty;
        public Guid DestinationStationId { get; init; }
        public string DestinationStationName { get; init; } = string.Empty;
    }
}
