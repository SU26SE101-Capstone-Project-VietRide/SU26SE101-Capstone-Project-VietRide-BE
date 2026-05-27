using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Trip.Infrastructure;

/// Trip service EF Core context — owns schema `vietride_trip`.
public sealed class TripDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_trip";

    public TripDbContext(DbContextOptions<TripDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
