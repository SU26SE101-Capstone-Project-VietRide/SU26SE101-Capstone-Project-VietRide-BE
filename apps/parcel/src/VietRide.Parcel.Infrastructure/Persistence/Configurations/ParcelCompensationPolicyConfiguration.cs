using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCompensationPolicyConfiguration
    : IEntityTypeConfiguration<ParcelCompensationPolicy>
{
    public void Configure(EntityTypeBuilder<ParcelCompensationPolicy> builder)
    {
        builder.ToTable("parcel_compensation_policies", table =>
        {
            table.HasCheckConstraint("chk_parcel_compensation_policy_rate", "compensation_rate_percent BETWEEN 1 AND 100");
            table.HasCheckConstraint("chk_parcel_compensation_policy_cap", "max_compensation_vnd > 0");
            table.HasCheckConstraint("chk_parcel_compensation_policy_sla", "claim_window_days > 0 AND search_sla_hours > 0 AND decision_sla_business_days > 0 AND payout_sla_business_days > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CompensationRatePercent).HasColumnName("compensation_rate_percent").IsRequired();
        builder.Property(x => x.MaxCompensationVnd).HasColumnName("max_compensation_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.NoProofFallbackMultiplier).HasColumnName("no_proof_fallback_multiplier").IsRequired();
        builder.Property(x => x.ClaimWindowDays).HasColumnName("claim_window_days").IsRequired();
        builder.Property(x => x.SearchSlaHours).HasColumnName("search_sla_hours").IsRequired();
        builder.Property(x => x.DecisionSlaBusinessDays).HasColumnName("decision_sla_business_days").IsRequired();
        builder.Property(x => x.PayoutSlaBusinessDays).HasColumnName("payout_sla_business_days").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.BelowDefaultAcknowledged).HasColumnName("below_default_acknowledged").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => x.OperatorId).IsUnique();
    }
}
