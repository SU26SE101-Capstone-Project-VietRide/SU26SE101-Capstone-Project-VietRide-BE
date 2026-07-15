using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure;

/// Trip service EF Core context — owns schema `vietride_trip`.
public sealed class TripDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_trip";

    private static readonly INpgsqlNameTranslator PostgresEnumNameTranslator = new NpgsqlNullNameTranslator();

    public TripDbContext(DbContextOptions<TripDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    public static void ConfigurePostgresEnums(NpgsqlDataSourceBuilder builder)
    {
        builder.MapEnum<TripStatus>("trip_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripSource>("trip_source", PostgresEnumNameTranslator);
        builder.MapEnum<TripSeatStatus>("trip_seat_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripSeatType>("trip_seat_type", PostgresEnumNameTranslator);
        builder.MapEnum<TripStopStatus>("trip_stop_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripStopFareSource>("trip_stop_fare_source", PostgresEnumNameTranslator);
        builder.MapEnum<TripGenerationSkipReason>("trip_generation_skip_reason", PostgresEnumNameTranslator);
        builder.MapEnum<VehicleStatus>("vehicle_status", PostgresEnumNameTranslator);
    }

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<OperatorStation> OperatorStations => Set<OperatorStation>();

    public DbSet<Stop> Stops => Set<Stop>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<RouteStopFareTemplate> RouteStopFareTemplates => Set<RouteStopFareTemplate>();

    public DbSet<AlternativeRoute> AlternativeRoutes => Set<AlternativeRoute>();

    public DbSet<AlternativeRouteStop> AlternativeRouteStops => Set<AlternativeRouteStop>();

    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<DriverSchedule> DriverSchedules => Set<DriverSchedule>();

    public DbSet<DriverScheduleAuditLog> DriverScheduleAuditLogs => Set<DriverScheduleAuditLog>();

    public DbSet<Domain.Entities.Trip> Trips => Set<Domain.Entities.Trip>();

    public DbSet<TripAuditLog> TripAuditLogs => Set<TripAuditLog>();

    public DbSet<TripSeat> TripSeats => Set<TripSeat>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<TripStopFare> TripStopFares => Set<TripStopFare>();

    public DbSet<TripCargoParcel> TripCargoParcels => Set<TripCargoParcel>();

    public DbSet<TripGenerationSkipLog> TripGenerationSkipLogs => Set<TripGenerationSkipLog>();

    public DbSet<ShuttleTrip> ShuttleTrips => Set<ShuttleTrip>();

    public DbSet<ShuttlePassenger> ShuttlePassengers => Set<ShuttlePassenger>();

    public DbSet<ShuttleDispatchAlert> ShuttleDispatchAlerts => Set<ShuttleDispatchAlert>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresEnum("trip_status", new[] { "SCHEDULED", "BOARDING", "IN_PROGRESS", "COMPLETED", "CANCELLED", "DISRUPTED" });
        modelBuilder.HasPostgresEnum("trip_source", new[] { "MANUAL", "AUTO_FROM_SCHEDULE", "VEHICLE_SUBSTITUTION" });
        modelBuilder.HasPostgresEnum("trip_seat_status", new[] { "AVAILABLE", "HELD", "BOOKED", "UNAVAILABLE" });
        modelBuilder.HasPostgresEnum("trip_seat_type", new[] { "STANDARD", "SLEEPER_LOWER", "SLEEPER_UPPER", "VIP", "DRIVER_AREA" });
        modelBuilder.HasPostgresEnum("trip_stop_status", new[] { "PENDING", "ARRIVED", "SKIPPED" });
        modelBuilder.HasPostgresEnum("trip_stop_fare_source", new[] { "TEMPLATE_SNAPSHOT", "MANUAL_OVERRIDE" });
        modelBuilder.HasPostgresEnum("trip_generation_skip_reason", new[] { "SUBSCRIPTION_LIMIT_EXCEEDED", "VEHICLE_CONFLICT", "DRIVER_CONFLICT", "OTHER" });
        modelBuilder.HasPostgresEnum("vehicle_status", new[] { "ACTIVE", "MAINTENANCE", "OFF_DUTY", "RETIRED" });
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripDbContext).Assembly);
        RemoveConventionIndex<Route>(modelBuilder, nameof(Route.DestinationStationId));
        RemoveConventionIndex<RouteStopFareTemplate>(modelBuilder, nameof(RouteStopFareTemplate.StopId));
        RemoveConventionIndex<AlternativeRoute>(modelBuilder, nameof(AlternativeRoute.DestinationStationId));
        RemoveConventionIndex<AlternativeRouteStop>(modelBuilder, nameof(AlternativeRouteStop.StopId));
        RemoveConventionIndex<Domain.Entities.Trip>(modelBuilder, nameof(Domain.Entities.Trip.RouteId));
        RemoveConventionIndex<Domain.Entities.Trip>(modelBuilder, nameof(Domain.Entities.Trip.VehicleId));
    }

    private static void RemoveConventionIndex<TEntity>(ModelBuilder modelBuilder, string propertyName)
        where TEntity : class
    {
        var entityType = modelBuilder.Entity<TEntity>().Metadata;
        var property = entityType.FindProperty(propertyName);
        var index = property is null ? null : entityType.FindIndex(new[] { property });
        if (index is not null)
        {
            entityType.RemoveIndex(index);
        }
    }
}
