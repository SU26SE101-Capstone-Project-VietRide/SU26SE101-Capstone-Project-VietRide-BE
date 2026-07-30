using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class OperatorParcelStatsRepository : IOperatorParcelStatsRepository
{
    private readonly ParcelDbContext _dbContext;

    public OperatorParcelStatsRepository(ParcelDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OperatorParcelStatsReadResult> GetAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string groupBy,
        int routeLimit,
        CancellationToken cancellationToken = default)
    {
        var rows = string.Equals(groupBy, "status", StringComparison.Ordinal)
            ? await GetStatusRowsAsync(operatorId, fromUtc, toUtcExclusive, cancellationToken)
            : await GetRouteRowsAsync(operatorId, fromUtc, toUtcExclusive, routeLimit, cancellationToken);

        var totalParcels = rows.Count == 0 ? 0 : rows[0].TotalParcels;
        var buckets = rows
            .Where(row => row.Key is not null || (row.RouteId.HasValue && row.RouteName is not null))
            .Select(row => new OperatorParcelStatsBucketReadModel(
                row.Key,
                row.RouteId,
                row.RouteName,
                row.Count))
            .ToList();
        return new OperatorParcelStatsReadResult(totalParcels, buckets);
    }

    private async Task<List<OperatorParcelStatsSqlRow>> GetStatusRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken)
        => await _dbContext.Database.SqlQuery<OperatorParcelStatsSqlRow>($"""
            WITH scoped AS (
                SELECT status
                FROM vietride_parcel.parcels
                WHERE operator_id = {operatorId}
                  AND created_at >= {fromUtc}
                  AND created_at < {toUtcExclusive}
            ),
            total AS (
                SELECT COUNT(*)::bigint AS total_parcels
                FROM scoped
            ),
            grouped AS (
                SELECT status::text AS key, COUNT(*)::bigint AS count
                FROM scoped
                GROUP BY status
            )
            SELECT
                grouped.key AS "Key",
                NULL::uuid AS "RouteId",
                NULL::text AS "RouteName",
                COALESCE(grouped.count, 0)::bigint AS "Count",
                total.total_parcels AS "TotalParcels"
            FROM total
            LEFT JOIN grouped ON TRUE
            ORDER BY grouped.key NULLS LAST
            """).ToListAsync(cancellationToken);

    private async Task<List<OperatorParcelStatsSqlRow>> GetRouteRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        int routeLimit,
        CancellationToken cancellationToken)
        => await _dbContext.Database.SqlQuery<OperatorParcelStatsSqlRow>($"""
            WITH scoped AS (
                SELECT id, created_at, trip_snapshot_route_id, trip_snapshot_route_name
                FROM vietride_parcel.parcels
                WHERE operator_id = {operatorId}
                  AND created_at >= {fromUtc}
                  AND created_at < {toUtcExclusive}
            ),
            total AS (
                SELECT COUNT(*)::bigint AS total_parcels
                FROM scoped
            ),
            grouped AS (
                SELECT
                    trip_snapshot_route_id AS route_id,
                    COUNT(*)::bigint AS parcel_count
                FROM scoped
                WHERE trip_snapshot_route_id IS NOT NULL
                GROUP BY trip_snapshot_route_id
            ),
            latest_names AS (
                SELECT DISTINCT ON (trip_snapshot_route_id)
                    trip_snapshot_route_id AS route_id,
                    trip_snapshot_route_name AS route_name
                FROM scoped
                WHERE trip_snapshot_route_id IS NOT NULL
                  AND trip_snapshot_route_name IS NOT NULL
                  AND btrim(trip_snapshot_route_name) <> ''
                ORDER BY trip_snapshot_route_id, created_at DESC, id DESC
            ),
            ranked AS (
                SELECT
                    grouped.route_id,
                    latest_names.route_name,
                    grouped.parcel_count,
                    ROW_NUMBER() OVER (
                        ORDER BY grouped.parcel_count DESC, latest_names.route_name, grouped.route_id) AS rank
                FROM grouped
                INNER JOIN latest_names ON latest_names.route_id = grouped.route_id
            )
            SELECT
                NULL::text AS "Key",
                ranked.route_id AS "RouteId",
                ranked.route_name AS "RouteName",
                COALESCE(ranked.parcel_count, 0)::bigint AS "Count",
                total.total_parcels AS "TotalParcels"
            FROM total
            LEFT JOIN ranked ON ranked.rank <= {routeLimit}
            ORDER BY ranked.rank NULLS LAST
            """).ToListAsync(cancellationToken);

    private sealed class OperatorParcelStatsSqlRow
    {
        public string? Key { get; set; }
        public Guid? RouteId { get; set; }
        public string? RouteName { get; set; }
        public long Count { get; set; }
        public long TotalParcels { get; set; }
    }
}
