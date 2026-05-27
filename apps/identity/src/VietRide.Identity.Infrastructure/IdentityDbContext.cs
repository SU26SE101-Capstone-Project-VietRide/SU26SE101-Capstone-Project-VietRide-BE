using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Identity.Infrastructure;

/// Identity service EF Core context — owns schema `vietride_identity`.
public sealed class IdentityDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
