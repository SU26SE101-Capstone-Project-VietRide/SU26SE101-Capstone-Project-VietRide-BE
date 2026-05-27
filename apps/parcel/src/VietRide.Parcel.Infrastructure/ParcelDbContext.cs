using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Parcel.Infrastructure;

/// Parcel service EF Core context — owns schema `vietride_parcel`.
public sealed class ParcelDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_parcel";

    public ParcelDbContext(DbContextOptions<ParcelDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
