using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OperatorSubscriptionConfiguration : IEntityTypeConfiguration<OperatorSubscription>
{
    public void Configure(EntityTypeBuilder<OperatorSubscription> builder)
    {
        builder.ToTable("operator_subscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(s => s.PlanId)
            .HasColumnName("active_plan_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasColumnType("subscription_status")
            .HasDefaultValue(SubscriptionStatus.PENDING_APPROVAL)
            .IsRequired();

        builder.Property(s => s.StartedAt)
            .HasColumnName("started_at")
            .IsRequired(false);

        builder.Property(s => s.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired(false);

        builder.Property(s => s.PaymentMethod)
            .HasColumnName("payment_method")
            .HasColumnType("subscription_payment_method")
            .IsRequired(false);

        builder.Property(s => s.BillingPeriod)
            .HasColumnName("billing_period")
            .HasColumnType("subscription_billing_period")
            .IsRequired(false);

        builder.Property(s => s.CurrentVehicles)
            .HasColumnName("current_vehicles")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentDrivers)
            .HasColumnName("current_drivers")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentAssistants)
            .HasColumnName("current_assistants")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentOperatorUsers)
            .HasColumnName("current_operator_users")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentRoutes)
            .HasColumnName("current_routes")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentTripsThisMonth)
            .HasColumnName("current_trips_this_month")
            .HasDefaultValue(0)
            .IsRequired()
            .HasComment("Reset to 0 monthly by Hangfire (day 1, 00:01). Skipped for Trip.source = VEHICLE_SUBSTITUTION.");

        builder.Property(s => s.LastResetAt)
            .HasColumnName("last_reset_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.TrialExpiringWarnSentAt)
            .HasColumnName("trial_expiring_warn_sent_at")
            .IsRequired(false);

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Canonical identity schema has no operator_subscriptions.row_version column.
        builder.Ignore(s => s.RowVersion);

        builder.HasOne<Operator>()
            .WithMany()
            .HasForeignKey(s => s.OperatorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_operator_subscriptions_operator_id");

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_operator_subscriptions_plan_id");

        builder.HasIndex(s => s.OperatorId)
            .HasDatabaseName("uq_operator_subscriptions_operator_id")
            .IsUnique();

        builder.HasIndex(s => s.Status)
            .HasDatabaseName("idx_operator_subscriptions_status");

        builder.HasIndex(s => s.ExpiresAt)
            .HasDatabaseName("idx_operator_subscriptions_expires_at")
            .HasFilter("status = 'ACTIVE'");

        builder.HasIndex(s => s.PlanId)
            .HasDatabaseName("idx_operator_subscriptions_active_plan_id");
    }
}
