using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class CampaignVoucherConfiguration : IEntityTypeConfiguration<CampaignVoucher>
{
    public void Configure(EntityTypeBuilder<CampaignVoucher> builder)
    {
        builder.ToTable("campaign_vouchers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.VoucherId).HasColumnName("voucher_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);

        builder.HasOne(x => x.Campaign).WithMany(x => x.CampaignVouchers).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.CampaignId, x.VoucherId }).HasDatabaseName("uq_campaign_vouchers_campaign_voucher").IsUnique();
        builder.HasIndex(x => x.VoucherId).HasDatabaseName("idx_campaign_vouchers_voucher_id");
    }
}
