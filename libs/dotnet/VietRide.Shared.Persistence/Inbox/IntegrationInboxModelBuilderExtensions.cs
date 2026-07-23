using Microsoft.EntityFrameworkCore;

namespace VietRide.Shared.Persistence.Inbox;

public static class IntegrationInboxModelBuilderExtensions
{
    public static ModelBuilder AddVietRideIntegrationInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationInboxRecord>(builder =>
        {
            builder.ToTable("integration_inbox");
            builder.HasKey(entry => entry.Id);
            builder.Property(entry => entry.Id)
                .HasColumnName("id")
                .HasColumnType("uuid")
                .HasDefaultValueSql("gen_random_uuid()");
            builder.Property(entry => entry.ConsumerName)
                .HasColumnName("consumer_name")
                .HasMaxLength(200)
                .IsRequired();
            builder.Property(entry => entry.MessageId)
                .HasColumnName("message_id")
                .HasColumnType("uuid")
                .IsRequired();
            builder.Property(entry => entry.PayloadHash)
                .HasColumnName("payload_hash")
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            builder.Property(entry => entry.ProcessedAt)
                .HasColumnName("processed_at")
                .HasDefaultValueSql("now()")
                .IsRequired();
            builder.HasIndex(entry => new { entry.ConsumerName, entry.MessageId })
                .HasDatabaseName("uq_integration_inbox_consumer_message")
                .IsUnique();
        });

        return modelBuilder;
    }
}
