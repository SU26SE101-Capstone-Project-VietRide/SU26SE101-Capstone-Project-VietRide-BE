using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("trip_stops");

        builder.Ignore(stop => stop.Id);
        builder.Ignore(stop => stop.RowVersion);

        builder.HasKey(stop => new { stop.TripId, stop.StopId }).HasName("pk_trip_stops");

        builder.Property(stop => stop.TripId).HasColumnName("trip_id");
        builder.Property(stop => stop.StopId).HasColumnName("stop_id");
        builder.Property(stop => stop.OrderIndex).HasColumnName("order_index");
        builder.Property(stop => stop.EstimatedArrivalTime)
            .HasColumnName("estimated_arrival_time")
            .HasComment("Static baseline. NEVER updated after Trip generate. Dynamic ETA lives in Redis only.");
        builder.Property(stop => stop.ActualArrivalTime).HasColumnName("actual_arrival_time");
        builder.Property(stop => stop.ActualDepartureTime).HasColumnName("actual_departure_time");
        builder.Property(stop => stop.Status)
            .HasColumnName("status")
            .HasColumnType("vietride_trip.trip_stop_status")
            .HasDefaultValue(TripStopStatus.PENDING);
        builder.Property(stop => stop.AllowPickup).HasColumnName("allow_pickup");
        builder.Property(stop => stop.AllowDropoff).HasColumnName("allow_dropoff");
        builder.Property(stop => stop.DistanceFromOriginKm)
            .HasColumnName("distance_from_origin_km")
            .HasColumnType("decimal(8,2)");
        builder.Property(stop => stop.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Property(stop => stop.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(stop => new { stop.TripId, stop.OrderIndex })
            .IsUnique()
            .HasDatabaseName("uq_trip_stops_trip_order");
        builder.HasIndex(stop => new { stop.TripId, stop.Status }).HasDatabaseName("idx_trip_stops_trip_status");
        builder.HasIndex(stop => stop.EstimatedArrivalTime)
            .HasDatabaseName("idx_trip_stops_estimated_arrival")
            .HasFilter("status = 'PENDING'");

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(stop => stop.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(stop => stop.StopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
