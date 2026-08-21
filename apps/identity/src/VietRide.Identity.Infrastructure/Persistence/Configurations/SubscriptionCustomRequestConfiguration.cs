using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionCustomRequestConfiguration : IEntityTypeConfiguration<SubscriptionCustomRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionCustomRequest> builder)
    {
        builder.ToTable("subscription_custom_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.MaxVehicles).HasColumnName("max_vehicles").IsRequired();
        builder.Property(x => x.MaxDrivers).HasColumnName("max_drivers").IsRequired();
        builder.Property(x => x.MaxAssistants).HasColumnName("max_assistants").IsRequired();
        builder.Property(x => x.MaxOperatorUsers).HasColumnName("max_operator_users").IsRequired();
        builder.Property(x => x.MaxRoutes).HasColumnName("max_routes").IsRequired();
        builder.Property(x => x.MaxTripsPerMonth).HasColumnName("max_trips_per_month").IsRequired();
        builder.Property(x => x.EnableParcel).HasColumnName("enable_parcel").IsRequired();
        builder.Property(x => x.EnableShuttle).HasColumnName("enable_shuttle").IsRequired();
        builder.Property(x => x.EnableRag).HasColumnName("enable_rag").IsRequired();
        builder.Property(x => x.PreferredBillingPeriod).HasColumnName("preferred_billing_period").HasColumnType("subscription_billing_period").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by").HasColumnType("uuid");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Property(x => x.ApprovedPlanId).HasColumnName("approved_plan_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<Operator>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_custom_requests_operator_id");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_custom_requests_reviewed_by");
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.ApprovedPlanId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_custom_requests_approved_plan_id");

        builder.HasIndex(x => x.OperatorId)
            .HasDatabaseName("uq_subscription_custom_requests_pending_operator")
            .IsUnique()
            .HasFilter("status = 'PENDING_REVIEW'");
        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("idx_subscription_custom_requests_status_created_at");
        builder.HasIndex(x => x.ApprovedPlanId).HasDatabaseName("uq_subscription_custom_requests_approved_plan_id").IsUnique().HasFilter("approved_plan_id IS NOT NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("chk_subscription_custom_requests_limits_non_negative", "max_vehicles >= 0 AND max_drivers >= 0 AND max_assistants >= 0 AND max_operator_users >= 0 AND max_routes >= 0 AND max_trips_per_month >= 0");
            table.HasCheckConstraint("chk_subscription_custom_requests_review_state", "(status = 'PENDING_REVIEW' AND reviewed_by IS NULL AND reviewed_at IS NULL AND rejection_reason IS NULL AND approved_plan_id IS NULL) OR (status = 'APPROVED' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND rejection_reason IS NULL AND approved_plan_id IS NOT NULL) OR (status = 'REJECTED' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND rejection_reason IS NOT NULL AND approved_plan_id IS NULL)");
        });
    }
}
