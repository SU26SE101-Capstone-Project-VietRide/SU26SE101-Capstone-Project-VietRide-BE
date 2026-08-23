using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Identifiers;
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

    /// Terminal outbox publish failures retained for operational review.
    public DbSet<OutboxDlq> OutboxDlq => Set<OutboxDlq>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schemaName = GetType().GetField("SchemaName")?.GetRawConstantValue() as string;
        var outboxEventStatusTypeName = string.IsNullOrWhiteSpace(schemaName)
            ? "outbox_event_status"
            : $"{schemaName}.outbox_event_status";

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            modelBuilder.HasPostgresEnum("outbox_event_status", Enum.GetNames<OutboxEventStatus>());
        }
        else
        {
            modelBuilder.HasPostgresEnum(schemaName, "outbox_event_status", Enum.GetNames<OutboxEventStatus>());
        }

        modelBuilder.Entity<OutboxEvent>(b =>
        {
            b.ToTable("outbox_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.EventType).IsRequired().HasMaxLength(100);
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Status)
                .HasColumnType(outboxEventStatusTypeName)
                .HasDefaultValueSql("'PENDING'")
                .HasSentinel((OutboxEventStatus)(-1))
                .IsRequired();
            b.Property(x => x.RetryCount).HasDefaultValue(0);
            b.Property(x => x.LastError);
            b.Property(x => x.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            b.Property(x => x.PublishedAt);
            b.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("idx_outbox_events_status_created")
                .HasFilter($"status IN ('PENDING'::{outboxEventStatusTypeName}, 'PUBLISHING'::{outboxEventStatusTypeName}, 'FAILED'::{outboxEventStatusTypeName})");
        });

        modelBuilder.Entity<OutboxDlq>(b =>
        {
            b.ToTable("outbox_dlq");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.EventId).IsRequired();
            b.Property(x => x.EventType).IsRequired().HasMaxLength(100);
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.RetryCount).IsRequired();
            b.Property(x => x.LastError).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.TerminalAt).HasDefaultValueSql("now()").IsRequired();
            b.HasIndex(x => x.EventId)
                .HasDatabaseName("uq_outbox_dlq_event_id")
                .IsUnique();
            b.HasIndex(x => new { x.TerminalAt, x.EventId })
                .HasDatabaseName("idx_outbox_dlq_terminal_event_id")
                .IsDescending(true, true);
        });

        modelBuilder.ApplySnakeCaseNaming();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (attempt < 3 && TryRegenerateBusinessCodes(exception))
            {
            }
        }

        throw new InvalidOperationException("Business code collision retry exhausted unexpectedly.");
    }

    public override int SaveChanges()
    {
        ApplyAuditing();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return base.SaveChanges();
            }
            catch (DbUpdateException exception) when (attempt < 3 && TryRegenerateBusinessCodes(exception))
            {
            }
        }

        throw new InvalidOperationException("Business code collision retry exhausted unexpectedly.");
    }

    private static bool TryRegenerateBusinessCodes(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraintName,
            })
        {
            return false;
        }

        var entities = exception.Entries
            .Select(entry => entry.Entity)
            .OfType<IBusinessCodeEntity>()
            .Where(candidate => candidate.BusinessCodeConstraintName == constraintName)
            .ToArray();
        if (entities.Length == 0)
        {
            return false;
        }

        foreach (var entity in entities)
        {
            entity.RegenerateBusinessCode();
        }

        return true;
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
