using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelRouteFareConfiguration : IEntityTypeConfiguration<ParcelRouteFare>
{
    public void Configure(EntityTypeBuilder<ParcelRouteFare> builder)
    {
        builder.ToTable("parcel_route_fares", table =>
        {
            table.HasCheckConstraint("chk_parcel_route_fares_price_non_negative", "price_vnd >= 0");
            table.HasCheckConstraint("chk_parcel_route_fares_effective_order", "effective_until IS NULL OR effective_until > effective_from");
        });

        builder.HasKey(x => new { x.RouteId, x.SizeCategory });

        builder.Property(x => x.RouteId)
            .HasColumnName("route_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.SizeCategory)
            .HasColumnName("size_category")
            .HasColumnType("parcel_size_category")
            .IsRequired();

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.PriceVnd)
            .HasColumnName("price_vnd")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.EffectiveUntil)
            .HasColumnName("effective_until")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);
        builder.Ignore(x => x.Id);

        builder.HasIndex(x => x.OperatorId)
            .HasDatabaseName("idx_parcel_route_fares_operator_id");
    }
}
