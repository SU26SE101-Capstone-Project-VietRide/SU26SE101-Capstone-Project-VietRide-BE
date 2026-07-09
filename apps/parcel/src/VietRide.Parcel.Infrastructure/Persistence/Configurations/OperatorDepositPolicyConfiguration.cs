using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class OperatorDepositPolicyConfiguration : IEntityTypeConfiguration<OperatorDepositPolicy>
{
    public void Configure(EntityTypeBuilder<OperatorDepositPolicy> builder)
    {
        builder.ToTable("operator_deposit_policies", table =>
        {
            table.HasCheckConstraint("chk_operator_deposit_policies_percent", "deposit_percent > 0 AND deposit_percent <= 100");
        });

        builder.HasKey(policy => policy.Id);
        builder.Ignore(policy => policy.RowVersion);
        builder.Property(policy => policy.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(policy => policy.OperatorId).HasColumnName("operator_id");
        builder.Property(policy => policy.RouteId).HasColumnName("route_id");
        builder.Property(policy => policy.DepositPercent).HasColumnName("deposit_percent").HasColumnType("decimal(5,2)");
        builder.Property(policy => policy.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(policy => policy.EffectiveTo).HasColumnName("effective_to");
        builder.Property(policy => policy.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(policy => policy.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(policy => policy.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(policy => new { policy.OperatorId, policy.RouteId, policy.IsActive, policy.EffectiveFrom })
            .HasDatabaseName("idx_operator_deposit_policies_lookup");
    }
}
