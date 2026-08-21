using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCurrentCustodyConfiguration : IEntityTypeConfiguration<ParcelCurrentCustody>
{
    public void Configure(EntityTypeBuilder<ParcelCurrentCustody> builder)
    {
        builder.ToTable("parcel_current_custody");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.LastEventType).HasColumnName("last_event_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.LastLocationType).HasColumnName("last_location_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LastLocationId).HasColumnName("last_location_id").HasColumnType("uuid");
        builder.Property(x => x.LastLocationSnapshot).HasColumnName("last_location_snapshot").HasMaxLength(500);
        builder.Property(x => x.LastConfirmedAt).HasColumnName("last_confirmed_at").IsRequired();
        builder.Property(x => x.CurrentTripId).HasColumnName("current_trip_id").HasColumnType("uuid");
        builder.Property(x => x.CurrentVehicleId).HasColumnName("current_vehicle_id").HasColumnType("uuid");
        builder.Property(x => x.TrackingConfidence).HasColumnName("tracking_confidence").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastSequence).HasColumnName("last_sequence").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ParcelId).IsUnique();
        builder.HasIndex(x => new { x.LastLocationId, x.LastConfirmedAt });
    }
}
