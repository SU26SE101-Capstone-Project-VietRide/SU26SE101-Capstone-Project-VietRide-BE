using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure;

/// Trip service EF Core context — owns schema `vietride_trip`.
public sealed class TripDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_trip";

    public TripDbContext(DbContextOptions<TripDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

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

    public DbSet<TripEntity> Trips => Set<TripEntity>();

    public DbSet<TripSeat> TripSeats => Set<TripSeat>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.HasPostgresEnum<TripStatus>("trip_status");
        modelBuilder.HasPostgresEnum<TripSource>("trip_source");
        modelBuilder.HasPostgresEnum<TripSeatStatus>("trip_seat_status");
        modelBuilder.HasPostgresEnum<TripSeatType>("trip_seat_type");
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripDbContext).Assembly);
        RemoveConventionIndex<Route>(modelBuilder, nameof(Route.DestinationStationId));
        RemoveConventionIndex<RouteStopFareTemplate>(modelBuilder, nameof(RouteStopFareTemplate.StopId));
        RemoveConventionIndex<AlternativeRoute>(modelBuilder, nameof(AlternativeRoute.DestinationStationId));
        RemoveConventionIndex<AlternativeRouteStop>(modelBuilder, nameof(AlternativeRouteStop.StopId));
        RemoveConventionIndex<TripEntity>(modelBuilder, nameof(TripEntity.RouteId));
        RemoveConventionIndex<TripEntity>(modelBuilder, nameof(TripEntity.VehicleId));
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
