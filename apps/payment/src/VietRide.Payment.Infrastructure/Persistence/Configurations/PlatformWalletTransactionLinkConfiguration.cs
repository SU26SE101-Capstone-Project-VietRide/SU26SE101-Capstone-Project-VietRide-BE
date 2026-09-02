using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class PlatformWalletTransactionLinkConfiguration
    : IEntityTypeConfiguration<PlatformWalletTransactionLink>
{
    public void Configure(EntityTypeBuilder<PlatformWalletTransactionLink> builder)
    {
        builder.ToTable("platform_wallet_transaction_links", table =>
        {
            table.HasCheckConstraint(
                "chk_platform_wallet_tx_links_amount_non_negative",
                "allocated_amount >= 0");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.PlatformWalletTransactionId)
            .HasColumnName("platform_wallet_transaction_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(item => item.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid");
        builder.Property(item => item.TripId)
            .HasColumnName("trip_id")
            .HasColumnType("uuid");
        builder.Property(item => item.LinkType)
            .HasColumnName("link_type")
            .HasColumnType($"{PaymentDbContext.SchemaName}.platform_wallet_transaction_link_type")
            .IsRequired();
        builder.Property(item => item.ReferenceId)
            .HasColumnName("reference_id")
            .HasColumnType("uuid");
        builder.Property(item => item.ReferenceCode)
            .HasColumnName("reference_code")
            .HasMaxLength(64);
        builder.Property(item => item.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasColumnType("bigint")
            .IsRequired();
        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Ignore(item => item.UpdatedAt);
        builder.Ignore(item => item.RowVersion);

        builder.HasOne<PlatformWalletTransaction>()
            .WithMany()
            .HasForeignKey(item => item.PlatformWalletTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_platform_wallet_transaction_links_transaction");

        builder.HasIndex(item => item.PlatformWalletTransactionId)
            .HasDatabaseName("idx_platform_wallet_transaction_links_transaction");
        builder.HasIndex(item => item.OperatorId)
            .HasDatabaseName("idx_platform_wallet_transaction_links_operator")
            .HasFilter("operator_id IS NOT NULL");
        builder.HasIndex(item => item.TripId)
            .HasDatabaseName("idx_platform_wallet_transaction_links_trip")
            .HasFilter("trip_id IS NOT NULL");
        builder.HasIndex(item => new { item.ReferenceId, item.ReferenceCode })
            .HasDatabaseName("idx_platform_wallet_transaction_links_reference")
            .HasFilter("reference_id IS NOT NULL OR reference_code IS NOT NULL");
        builder.HasIndex(item => new
        {
            item.PlatformWalletTransactionId,
            item.LinkType,
            item.ReferenceId,
        })
            .HasDatabaseName("uq_platform_wallet_transaction_links_identity")
            .IsUnique()
            .AreNullsDistinct(false);
    }
}
