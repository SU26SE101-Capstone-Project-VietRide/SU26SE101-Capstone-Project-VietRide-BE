using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionQuotaAllocationConfiguration : IEntityTypeConfiguration<SubscriptionQuotaAllocation>
{
    public void Configure(EntityTypeBuilder<SubscriptionQuotaAllocation> builder)
    {
        builder.ToTable("subscription_quota_allocations");
        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(allocation => allocation.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(allocation => allocation.SubscriptionId).HasColumnName("subscription_id").HasColumnType("uuid").IsRequired();
        builder.Property(allocation => allocation.Resource).HasColumnName("resource").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(allocation => allocation.ResourceId).HasColumnName("resource_id").HasColumnType("uuid").IsRequired();
        builder.Property(allocation => allocation.PeriodKey).HasColumnName("period_key").HasMaxLength(7);
        builder.Property(allocation => allocation.ReleasedAt).HasColumnName("released_at");
        builder.Property(allocation => allocation.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(allocation => allocation.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(allocation => allocation.RowVersion);
        builder.HasIndex(allocation => new { allocation.OperatorId, allocation.Resource, allocation.ResourceId })
            .HasDatabaseName("uq_subscription_quota_allocations_resource")
            .IsUnique();
        builder.HasIndex(allocation => new { allocation.SubscriptionId, allocation.Resource, allocation.ReleasedAt })
            .HasDatabaseName("idx_subscription_quota_allocations_subscription_resource");
        builder.HasOne<OperatorSubscription>().WithMany().HasForeignKey(allocation => allocation.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_quota_allocations_subscription_id");
    }
}
