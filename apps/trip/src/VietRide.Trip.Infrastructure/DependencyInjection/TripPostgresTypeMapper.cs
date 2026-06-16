using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.DependencyInjection;

public static class TripPostgresTypeMapper
{
    public static void MapTripEnums(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        dataSourceBuilder.MapEnum<TripStatus>("trip_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSource>("trip_source", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSeatStatus>("trip_seat_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripSeatType>("trip_seat_type", new NpgsqlNullNameTranslator());
    }
}
