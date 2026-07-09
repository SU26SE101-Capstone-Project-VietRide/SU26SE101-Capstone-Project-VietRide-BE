using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripCargoParcelConfiguration : IEntityTypeConfiguration<TripCargoParcel>
{
    public void Configure(EntityTypeBuilder<TripCargoParcel> builder)
    {
        builder.ToTable("trip_cargo_parcels", table =>
        {
            table.HasCheckConstraint("chk_trip_cargo_parcels_weight_positive", "weight_kg > 0");
            table.HasCheckConstraint("chk_trip_cargo_parcels_volume_positive", "volume_m3 > 0");
            table.HasCheckConstraint("chk_trip_cargo_parcels_actual_weight_positive", "actual_weight_kg IS NULL OR actual_weight_kg > 0");
            table.HasCheckConstraint("chk_trip_cargo_parcels_actual_volume_positive", "actual_volume_m3 IS NULL OR actual_volume_m3 > 0");
            table.HasCheckConstraint("chk_trip_cargo_parcels_state", "state IN ('RESERVED', 'LOADED', 'RELEASED')");
        });

        builder.HasKey(cargo => cargo.Id).HasName("pk_trip_cargo_parcels");
        builder.Ignore(cargo => cargo.RowVersion);

        builder.Property(cargo => cargo.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(cargo => cargo.TripId).HasColumnName("trip_id");
        builder.Property(cargo => cargo.ParcelId).HasColumnName("parcel_id");
        builder.Property(cargo => cargo.WeightKg)
            .HasColumnName("weight_kg")
            .HasColumnType("decimal(8,2)");
        builder.Property(cargo => cargo.VolumeM3)
            .HasColumnName("volume_m3")
            .HasColumnType("decimal(10,4)");
        builder.Property(cargo => cargo.ActualWeightKg)
            .HasColumnName("actual_weight_kg")
            .HasColumnType("decimal(8,2)");
        builder.Property(cargo => cargo.ActualVolumeM3)
            .HasColumnName("actual_volume_m3")
            .HasColumnType("decimal(10,4)");
        builder.Property(cargo => cargo.State)
            .HasColumnName("state")
            .HasMaxLength(20);
        builder.Property(cargo => cargo.LoadedAt).HasColumnName("loaded_at");
        builder.Property(cargo => cargo.ReleasedAt).HasColumnName("released_at");
        builder.Property(cargo => cargo.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Property(cargo => cargo.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(cargo => new { cargo.TripId, cargo.ParcelId })
            .IsUnique()
            .HasDatabaseName("uq_trip_cargo_parcels_trip_parcel");
        builder.HasIndex(cargo => new { cargo.TripId, cargo.State })
            .HasDatabaseName("idx_trip_cargo_parcels_trip_state");

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(cargo => cargo.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
