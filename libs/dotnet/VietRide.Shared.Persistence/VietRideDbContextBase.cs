using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Persistence.Naming;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Shared.Persistence;

/// Base DbContext for every VietRide service.
/// - Applies snake_case naming convention to all tables/columns.
/// - Auto-populates CreatedAt/UpdatedAt for IAuditable entities on SaveChanges.
/// - Registers OutboxMessage DbSet so every service inherits the outbox table.
public abstract class VietRideDbContextBase : DbContext
{
    private readonly IClock _clock;

    protected VietRideDbContextBase(DbContextOptions options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    /// Outbox table — every service that publishes events writes here in same transaction.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.OccurredAt).IsRequired();
            b.Property(x => x.Type).IsRequired().HasMaxLength(200);
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.ProcessedAt);
            b.Property(x => x.RetryCount).HasDefaultValue(0);
            b.Property(x => x.LastError);
            b.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
                .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");
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
