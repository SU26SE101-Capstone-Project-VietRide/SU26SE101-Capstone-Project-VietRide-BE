using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(p => p.PricePerMonth)
            .HasColumnName("price_per_month")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(p => p.PricePerYear)
            .HasColumnName("price_per_year")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(p => p.MaxVehicles)
            .HasColumnName("max_vehicles")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.MaxDrivers)
            .HasColumnName("max_drivers")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.MaxAssistants)
            .HasColumnName("max_assistants")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.MaxOperatorUsers)
            .HasColumnName("max_operator_users")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.MaxRoutes)
            .HasColumnName("max_routes")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.MaxTripsPerMonth)
            .HasColumnName("max_trips_per_month")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.EnableParcel)
            .HasColumnName("enable_parcel")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.EnableShuttle)
            .HasColumnName("enable_shuttle")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.EnableRag)
            .HasColumnName("enable_rag")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Canonical identity schema has no subscription_plans.row_version column.
        builder.Ignore(p => p.RowVersion);

        builder.HasIndex(p => p.IsActive)
            .HasDatabaseName("idx_subscription_plans_is_active");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "chk_subscription_plans_price_per_month_non_negative",
                "price_per_month >= 0");

            t.HasCheckConstraint(
                "chk_subscription_plans_price_per_year_non_negative",
                "price_per_year >= 0");
        });
    }
}
