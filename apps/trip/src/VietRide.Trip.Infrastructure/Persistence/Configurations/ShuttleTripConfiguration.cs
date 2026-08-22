using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class ShuttleTripConfiguration : IEntityTypeConfiguration<ShuttleTrip>
{
    public void Configure(EntityTypeBuilder<ShuttleTrip> builder)
    {
        builder.ToTable("shuttle_trips", table =>
        {
            table.HasCheckConstraint("chk_shuttle_trips_schedule", "scheduled_end_time > scheduled_departure_time");
            table.HasCheckConstraint("chk_shuttle_trips_direction", "direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')");
            table.HasCheckConstraint("chk_shuttle_trips_status", "status IN ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')");
        });
        builder.HasKey(x => x.Id).HasName("pk_shuttle_trips");
        builder.Ignore(x => x.RowVersion);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id");
        builder.Property(x => x.MainTripId).HasColumnName("main_trip_id");
        builder.Property(x => x.StationId).HasColumnName("station_id");
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(30);
        builder.Property(x => x.DriverUserId).HasColumnName("driver_user_id");
        builder.Property(x => x.VehicleId).HasColumnName("vehicle_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
            .HasDefaultValue(ShuttleTrip.ScheduledStatus);
        builder.Property(x => x.ScheduledDepartureTime).HasColumnName("scheduled_departure_time");
        builder.Property(x => x.ScheduledEndTime).HasColumnName("scheduled_end_time");
        builder.Property(x => x.ActualDepartureTime).HasColumnName("actual_departure_time");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CancelReason).HasColumnName("cancel_reason");
        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.HasIndex(x => x.MainTripId).HasDatabaseName("idx_shuttle_trips_main_trip");
        builder.HasIndex(x => new { x.OperatorId, x.Status }).HasDatabaseName("idx_shuttle_trips_operator_status");
        builder.HasIndex(x => new { x.StationId, x.Direction }).HasDatabaseName("idx_shuttle_trips_station_direction");
        builder.HasIndex(x => new { x.DriverUserId, x.ScheduledDepartureTime, x.ScheduledEndTime })
            .HasFilter("status IN ('SCHEDULED', 'IN_PROGRESS')")
            .HasDatabaseName("idx_shuttle_trips_driver_schedule");
        builder.HasIndex(x => new { x.VehicleId, x.ScheduledDepartureTime, x.ScheduledEndTime })
            .HasFilter("status IN ('SCHEDULED', 'IN_PROGRESS')")
            .HasDatabaseName("idx_shuttle_trips_vehicle_schedule");
        builder.HasOne<Domain.Entities.Trip>().WithMany().HasForeignKey(x => x.MainTripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
    }
}
