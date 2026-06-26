using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelStatsConfiguration : IEntityTypeConfiguration<ParcelStats>
{
    public void Configure(EntityTypeBuilder<ParcelStats> builder)
    {
        builder.ToTable("parcel_stats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.StatDate)
            .HasColumnName("stat_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.TotalParcels)
            .HasColumnName("total_parcels")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalLoaded)
            .HasColumnName("total_loaded")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalDelivered)
            .HasColumnName("total_delivered")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalRejected)
            .HasColumnName("total_rejected")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalReturned)
            .HasColumnName("total_returned")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalRevenue)
            .HasColumnName("total_revenue")
            .HasColumnType("bigint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalRefunded)
            .HasColumnName("total_refunded")
            .HasColumnType("bigint")
            .HasDefaultValue(0)
            .IsRequired();

        // parcel_stats is an upsert counter table — no created_at column per schema.sql.
        builder.Ignore(x => x.CreatedAt);

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => new { x.OperatorId, x.StatDate })
            .HasDatabaseName("uq_parcel_stats_operator_date")
            .IsUnique();

        builder.HasIndex(x => x.StatDate)
            .HasDatabaseName("idx_parcel_stats_stat_date")
            .IsDescending();
    }
}
