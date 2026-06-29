using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Persistence.Naming;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Shared.Persistence;

/// Base DbContext for every VietRide service.
/// - Applies snake_case naming convention to all tables/columns.
/// - Auto-populates CreatedAt/UpdatedAt for IAuditable entities on SaveChanges.
/// - Registers OutboxEvent DbSet so every service inherits the outbox table.
public abstract class VietRideDbContextBase : DbContext
{
    private readonly IClock _clock;

    protected VietRideDbContextBase(DbContextOptions options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    /// Outbox table — every service that publishes events writes here in same transaction.
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresEnum("outbox_event_status", Enum.GetNames<OutboxEventStatus>());

        modelBuilder.Entity<OutboxEvent>(b =>
        {
            b.ToTable("outbox_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.EventType).IsRequired().HasMaxLength(100);
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Status)
                .HasColumnType("outbox_event_status")
                .HasDefaultValueSql("'PENDING'")
                .IsRequired();
            b.Property(x => x.RetryCount).HasDefaultValue(0);
            b.Property(x => x.LastError);
            b.Property(x => x.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            b.Property(x => x.PublishedAt);
            b.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("idx_outbox_events_status_created")
                .HasFilter("status IN ('PENDING', 'PUBLISHING', 'FAILED')");
        });

        modelBuilder.ApplySnakeCaseNaming();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditing();
        return base.SaveChanges();
    }

    private void ApplyAuditing()
    {
        var now = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
