using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class ShuttlePassengerConfiguration : IEntityTypeConfiguration<ShuttlePassenger>
{
    public void Configure(EntityTypeBuilder<ShuttlePassenger> builder)
    {
        builder.ToTable("shuttle_passengers", table =>
        {
            table.HasCheckConstraint("chk_shuttle_passengers_direction", "direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')");
            table.HasCheckConstraint("chk_shuttle_passengers_status", "status IN ('PENDING_ASSIGNMENT', 'PENDING', 'PICKED_UP', 'DELIVERED', 'NO_SHOW', 'CANCELLED')");
            table.HasCheckConstraint(
                "chk_shuttle_passengers_road_distance",
                "road_distance_meters IS NULL OR road_distance_meters >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_shuttle_passengers");
        builder.Ignore(x => x.RowVersion);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ShuttleTripId).HasColumnName("shuttle_trip_id");
        builder.Property(x => x.MainTripId).HasColumnName("main_trip_id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.TicketId).HasColumnName("ticket_id");
        builder.Property(x => x.PassengerUserId).HasColumnName("passenger_user_id");
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(30);
        builder.Property(x => x.PickupAddress).HasColumnName("pickup_address");
        builder.Property(x => x.PickupLat).HasColumnName("pickup_lat").HasColumnType("decimal(10,7)");
        builder.Property(x => x.PickupLng).HasColumnName("pickup_lng").HasColumnType("decimal(10,7)");
        builder.Property(x => x.RoadDistanceMeters).HasColumnName("road_distance_meters");
        builder.Property(x => x.ScheduledPickupTime).HasColumnName("scheduled_pickup_time");
        builder.Property(x => x.PickupOrder).HasColumnName("pickup_order");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30)
            .HasDefaultValue(ShuttlePassenger.PendingAssignmentStatus);
        builder.Property(x => x.PickedUpAt).HasColumnName("picked_up_at");
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(x => x.CancelReason).HasColumnName("cancel_reason");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.HasIndex(x => x.ShuttleTripId).HasFilter("shuttle_trip_id IS NOT NULL").HasDatabaseName("idx_shuttle_passengers_shuttle_trip");
        builder.HasIndex(x => new { x.MainTripId, x.Status }).HasDatabaseName("idx_shuttle_passengers_main_trip_status");
        builder.HasIndex(x => x.BookingId).HasFilter("booking_id IS NOT NULL").HasDatabaseName("idx_shuttle_passengers_booking");
        builder.HasIndex(x => new { x.BookingId, x.TicketId, x.Direction }).IsUnique()
            .HasFilter("booking_id IS NOT NULL AND ticket_id IS NOT NULL")
            .HasDatabaseName("uq_shuttle_passengers_booking_ticket_direction");
        builder.HasOne<ShuttleTrip>().WithMany().HasForeignKey(x => x.ShuttleTripId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Domain.Entities.Trip>().WithMany().HasForeignKey(x => x.MainTripId).OnDelete(DeleteBehavior.Restrict);
    }
}
