using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripDbContext).Assembly);
    }
}
