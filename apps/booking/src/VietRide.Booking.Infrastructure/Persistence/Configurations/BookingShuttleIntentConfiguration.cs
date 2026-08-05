using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingShuttleIntentConfiguration : IEntityTypeConfiguration<BookingShuttleIntent>
{
    public void Configure(EntityTypeBuilder<BookingShuttleIntent> builder)
    {
        builder.ToTable("booking_shuttle_intents", table =>
        {
            table.HasCheckConstraint("chk_booking_shuttle_intents_latitude", "pickup_latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("chk_booking_shuttle_intents_longitude", "pickup_longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint(
                "chk_booking_shuttle_intents_direction",
                "direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')");
            table.HasCheckConstraint(
                "chk_booking_shuttle_intents_road_distance",
                "road_distance_meters IS NULL OR road_distance_meters >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_booking_shuttle_intents");
        builder.Ignore(x => x.RowVersion);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(30)
            .HasDefaultValue(BookingShuttleIntent.InboundDirection);
        builder.Property(x => x.PickupAddress).HasColumnName("pickup_address");
        builder.Property(x => x.PickupLatitude).HasColumnName("pickup_latitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.PickupLongitude).HasColumnName("pickup_longitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.RoadDistanceMeters).HasColumnName("road_distance_meters");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.BookingId, x.Direction })
            .IsUnique()
            .HasFilter("is_active = TRUE")
            .HasDatabaseName("uq_booking_shuttle_intents_booking_direction");
        builder.HasOne(x => x.Booking).WithMany(x => x.ShuttleIntents)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
