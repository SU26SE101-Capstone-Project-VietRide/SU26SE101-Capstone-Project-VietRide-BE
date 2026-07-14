using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedIntegrationEventConfiguration : IEntityTypeConfiguration<ProcessedIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedIntegrationEvent> builder)
    {
        builder.ToTable("processed_integration_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Consumer).HasColumnName("consumer").HasMaxLength(150).IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => new { x.Consumer, x.EventId }).HasDatabaseName("uq_processed_integration_events_consumer_event").IsUnique();
    }
}
