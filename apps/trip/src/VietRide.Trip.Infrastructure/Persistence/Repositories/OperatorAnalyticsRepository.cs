using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class OperatorAnalyticsRepository : IOperatorAnalyticsRepository
{
    private readonly TripDbContext dbContext;

    public OperatorAnalyticsRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OperatorVehicleCountReadModel>> GetVehicleCountsAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Vehicles
            .AsNoTracking()
            .Where(vehicle => operatorIds.Contains(vehicle.OperatorId))
            .GroupBy(vehicle => vehicle.OperatorId)
            .Select(group => new { OperatorId = group.Key, VehicleCount = group.Count() })
            .OrderBy(item => item.OperatorId)
            .ToListAsync(cancellationToken);

        return counts
            .Select(item => new OperatorVehicleCountReadModel(item.OperatorId, item.VehicleCount))
            .ToArray();
    }

    public async Task<IReadOnlyList<OperatorRoutePerformanceReadModel>> GetRoutePerformanceAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
        => await dbContext.Database.SqlQueryRaw<OperatorRoutePerformanceReadModel>(
            """
            SELECT route.id AS "RouteId",
                   route.name AS "RouteName",
                   origin.name AS "OriginName",
                   destination.name AS "DestinationName",
                   COUNT(*)::integer AS "TripCount",
                   COUNT(*) FILTER (WHERE trip.status = 'COMPLETED')::integer AS "CompletedTripCount"
            FROM vietride_trip.trips AS trip
            INNER JOIN vietride_trip.routes AS route ON route.id = trip.route_id
            INNER JOIN vietride_trip.stations AS origin ON origin.id = route.origin_station_id
            INNER JOIN vietride_trip.stations AS destination ON destination.id = route.destination_station_id
            WHERE trip.operator_id = @operator_id
              AND trip.departure_date_time >= @from_utc
              AND trip.departure_date_time < @to_utc
            GROUP BY route.id, route.name, origin.name, destination.name
            ORDER BY route.name, route.id;
            """,
            new NpgsqlParameter("operator_id", operatorId),
            new NpgsqlParameter("from_utc", fromUtc),
            new NpgsqlParameter("to_utc", toUtc))
            .ToListAsync(cancellationToken);
}
