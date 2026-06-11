using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> builder)
    {
        builder.ToTable("route_stops", table =>
        {
            table.HasCheckConstraint(
                "chk_route_stops_allow_at_least_one",
                "allow_pickup = TRUE OR allow_dropoff = TRUE");

            table.HasCheckConstraint(
                "chk_route_stops_order_positive",
                "order_index > 0");
        });

        builder.HasKey(x => new { x.RouteId, x.StopId })
            .HasName("pk_route_stops");

        builder.Property(x => x.RouteId)
            .HasColumnName("route_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.StopId)
            .HasColumnName("stop_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.OrderIndex)
            .HasColumnName("order_index")
            .IsRequired();

        builder.Property(x => x.EstimatedDurationFromOriginMinutes)
            .HasColumnName("estimated_duration_from_origin_minutes")
            .IsRequired();

        builder.Property(x => x.DistanceFromOriginKm)
            .HasColumnName("distance_from_origin_km")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);

        builder.Property(x => x.AllowPickup)
            .HasColumnName("allow_pickup")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.AllowDropoff)
            .HasColumnName("allow_dropoff")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(x => x.StopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RouteId, x.OrderIndex })
            .HasDatabaseName("uq_route_stops_route_order")
            .IsUnique();

        builder.HasIndex(x => x.StopId)
            .HasDatabaseName("idx_route_stops_stop_id");
    }
}
