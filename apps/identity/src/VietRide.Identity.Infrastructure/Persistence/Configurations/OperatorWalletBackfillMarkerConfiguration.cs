using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

public sealed class OperatorWalletBackfillMarkerConfiguration
    : IEntityTypeConfiguration<OperatorWalletBackfillMarker>
{
    public void Configure(EntityTypeBuilder<OperatorWalletBackfillMarker> builder)
    {
        builder.ToTable("operator_wallet_backfill_markers");
        builder.HasKey(marker => marker.OperatorId);
        builder.Property(marker => marker.OperatorId).ValueGeneratedNever();
        builder.Ignore(marker => marker.Id);
        builder.Property(marker => marker.EventId).IsRequired();
        builder.HasIndex(marker => marker.EventId).IsUnique();
        builder.Property(marker => marker.CreatedAt).HasDefaultValueSql("now()").IsRequired();
        builder.Property(marker => marker.UpdatedAt).HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(marker => marker.RowVersion);
    }
}
