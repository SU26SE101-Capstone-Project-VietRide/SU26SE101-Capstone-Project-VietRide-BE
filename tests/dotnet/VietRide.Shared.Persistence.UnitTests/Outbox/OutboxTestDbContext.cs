using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

/// <summary>
/// Minimal concrete <see cref="VietRideDbContextBase"/> for exercising the
/// shared outbox mapping (outbox_events table + outbox_event_status enum)
/// against a throwaway Postgres database.
/// </summary>
public sealed class OutboxTestDbContext : VietRideDbContextBase
{
    public OutboxTestDbContext(DbContextOptions options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        base.OnModelCreating(modelBuilder);
    }
}
