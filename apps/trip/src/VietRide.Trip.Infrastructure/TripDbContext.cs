using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Inbox;
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
        builder.MapEnum<TripStatus>($"{SchemaName}.trip_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripSource>($"{SchemaName}.trip_source", PostgresEnumNameTranslator);
        builder.MapEnum<PlannedEtaSource>($"{SchemaName}.planned_eta_source", PostgresEnumNameTranslator);
        builder.MapEnum<TripSeatStatus>($"{SchemaName}.trip_seat_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripSeatType>($"{SchemaName}.trip_seat_type", PostgresEnumNameTranslator);
        builder.MapEnum<TripStopStatus>($"{SchemaName}.trip_stop_status", PostgresEnumNameTranslator);
        builder.MapEnum<TripStopFareSource>($"{SchemaName}.trip_stop_fare_source", PostgresEnumNameTranslator);
        builder.MapEnum<TripGenerationSkipReason>($"{SchemaName}.trip_generation_skip_reason", PostgresEnumNameTranslator);
        builder.MapEnum<VehicleStatus>("public.vehicle_status", PostgresEnumNameTranslator);
        builder.MapEnum<IncidentCategory>($"{SchemaName}.incident_category", PostgresEnumNameTranslator);
        builder.MapEnum<RouteChangeProposalType>($"{SchemaName}.route_change_proposal_type", PostgresEnumNameTranslator);
        builder.MapEnum<RouteChangeProposalStatus>($"{SchemaName}.route_change_proposal_status", PostgresEnumNameTranslator);
    }

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<OperatorStation> OperatorStations => Set<OperatorStation>();

    public DbSet<Stop> Stops => Set<Stop>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<RouteStopFareTemplate> RouteStopFareTemplates => Set<RouteStopFareTemplate>();

    public DbSet<OperatorFareSurchargeSetting> OperatorFareSurchargeSettings => Set<OperatorFareSurchargeSetting>();

    public DbSet<OperatorFareSurchargePeriod> OperatorFareSurchargePeriods => Set<OperatorFareSurchargePeriod>();

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

    public DbSet<ResourceReservation> ResourceReservations => Set<ResourceReservation>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<RouteChangeProposal> RouteChangeProposals => Set<RouteChangeProposal>();

    public DbSet<RouteChangeProposalStop> RouteChangeProposalStops => Set<RouteChangeProposalStop>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsRouteCodeUniqueViolation(exception))
        {
            throw RouteCodeDuplicated();
        }
    }

    public override int SaveChanges()
    {
        try
        {
            return base.SaveChanges();
        }
        catch (DbUpdateException exception) when (IsRouteCodeUniqueViolation(exception))
        {
            throw RouteCodeDuplicated();
        }
    }

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
        modelBuilder.HasPostgresEnum(SchemaName, "planned_eta_source", new[] { "GOOGLE_ROUTES", "ROUTE_BASELINE" });
        modelBuilder.HasPostgresEnum("trip_seat_status", new[] { "AVAILABLE", "HELD", "BOOKED", "UNAVAILABLE" });
        modelBuilder.HasPostgresEnum("trip_seat_type", new[] { "STANDARD", "SLEEPER_LOWER", "SLEEPER_UPPER", "VIP", "DRIVER_AREA" });
        modelBuilder.HasPostgresEnum("trip_stop_status", new[] { "PENDING", "ARRIVED", "SKIPPED" });
        modelBuilder.HasPostgresEnum("trip_stop_fare_source", new[] { "TEMPLATE_SNAPSHOT", "MANUAL_OVERRIDE" });
        modelBuilder.HasPostgresEnum("trip_generation_skip_reason", new[] { "SUBSCRIPTION_LIMIT_EXCEEDED", "VEHICLE_CONFLICT", "DRIVER_CONFLICT", "OTHER" });
        modelBuilder.HasPostgresEnum("vehicle_status", new[] { "ACTIVE", "MAINTENANCE", "OFF_DUTY", "RETIRED" });
        modelBuilder.HasPostgresEnum(SchemaName, "incident_category", new[] { "TRAFFIC_JAM", "VEHICLE_BREAKDOWN", "ACCIDENT", "WEATHER", "OTHER" });
        modelBuilder.HasPostgresEnum(SchemaName, "route_change_proposal_type", new[] { "EXISTING", "CUSTOM" });
        modelBuilder.HasPostgresEnum(SchemaName, "route_change_proposal_status", new[] { "PENDING", "APPROVED", "REJECTED", "SUPERSEDED", "EXPIRED" });
        modelBuilder.AddVietRideIntegrationInbox();
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

    private static bool IsRouteCodeUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_routes_operator_code",
        };

    private static CodedConflictException RouteCodeDuplicated()
        => new(
            "ROUTE_CODE_DUPLICATED",
            "A Route with this code already exists for the operator.",
            [new ValidationError("code", "Route code is already in use.")]);
}
