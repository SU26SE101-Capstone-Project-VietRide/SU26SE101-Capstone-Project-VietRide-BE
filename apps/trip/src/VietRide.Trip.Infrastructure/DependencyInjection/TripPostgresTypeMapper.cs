using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.DependencyInjection;

public static class TripPostgresTypeMapper
{
    public static void MapTripEnums(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        // Schema-qualify the enum names so writes resolve unambiguously even if a stray
        // duplicate of the same enum exists in another schema (e.g. `public`) — the trip
        // tables' columns all use the `vietride_trip` schema. Without the qualifier, Npgsql
        // throws "More than one PostgreSQL type was found with the name ..." on write.
        dataSourceBuilder.MapEnum<TripStatus>("vietride_trip.trip_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSource>("vietride_trip.trip_source", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSeatStatus>("vietride_trip.trip_seat_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSeatType>("vietride_trip.trip_seat_type", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripStopFareSource>("vietride_trip.trip_stop_fare_source", new NpgsqlNullNameTranslator());
    }
}
