using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCompensationPayoutConfiguration
    : IEntityTypeConfiguration<ParcelCompensationPayout>
{
    public void Configure(EntityTypeBuilder<ParcelCompensationPayout> builder)
    {
        builder.ToTable("parcel_compensation_payouts", table =>
            table.HasCheckConstraint("chk_parcel_compensation_payout_amount", "amount_vnd > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.BeneficiaryUserId).HasColumnName("beneficiary_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.AmountVnd).HasColumnName("amount_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.FundingSource).HasColumnName("funding_source").HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.WalletTransactionId).HasColumnName("wallet_transaction_id").HasColumnType("uuid");
        builder.Property(x => x.PaidAt).HasColumnName("paid_at");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasColumnType("uuid");
        builder.Property(x => x.PaidEventId).HasColumnName("paid_event_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => x.ClaimId).IsUnique();
        builder.HasIndex(x => x.SourceEventId).IsUnique().HasFilter("source_event_id IS NOT NULL");
        builder.HasIndex(x => x.PaidEventId).IsUnique().HasFilter("paid_event_id IS NOT NULL");
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt });
    }
}
