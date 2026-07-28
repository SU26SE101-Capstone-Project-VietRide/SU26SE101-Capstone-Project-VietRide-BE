using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionUsageWarningMarkerConfiguration
    : IEntityTypeConfiguration<SubscriptionUsageWarningMarker>
{
    public void Configure(EntityTypeBuilder<SubscriptionUsageWarningMarker> builder)
    {
        builder.ToTable("subscription_usage_warning_markers");
        builder.HasKey(marker => marker.Id);
        builder.Property(marker => marker.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(marker => marker.SubscriptionId)
            .HasColumnName("subscription_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(marker => marker.Resource)
            .HasColumnName("resource")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(marker => marker.PeriodKey)
            .HasColumnName("period_key")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(marker => marker.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(marker => marker.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(marker => marker.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();
        builder.HasIndex(marker => new
        {
            marker.SubscriptionId,
            marker.Resource,
            marker.PeriodKey,
        })
            .HasDatabaseName("uq_subscription_usage_warning_markers_period")
            .IsUnique();
        builder.HasOne<OperatorSubscription>()
            .WithMany()
            .HasForeignKey(marker => marker.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_subscription_usage_warning_markers_subscription_id");
    }
}
