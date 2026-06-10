using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class AlternativeRouteStopConfiguration : IEntityTypeConfiguration<AlternativeRouteStop>
{
    public void Configure(EntityTypeBuilder<AlternativeRouteStop> builder)
    {
        builder.ToTable("alternative_route_stops", table =>
        {
            table.HasCheckConstraint(
                "chk_alternative_route_stops_order_positive",
                "order_index > 0");
        });

        builder.HasKey(x => new { x.AlternativeRouteId, x.StopId })
            .HasName("pk_alternative_route_stops");

        builder.Property(x => x.AlternativeRouteId)
            .HasColumnName("alternative_route_id")
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

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<AlternativeRoute>()
            .WithMany()
            .HasForeignKey(x => x.AlternativeRouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(x => x.StopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AlternativeRouteId, x.OrderIndex })
            .HasDatabaseName("uq_alternative_route_stops_route_order")
            .IsUnique();

        RemoveConventionIndex(builder, nameof(AlternativeRouteStop.StopId));
    }

    private static void RemoveConventionIndex(EntityTypeBuilder<AlternativeRouteStop> builder, string propertyName)
    {
        var property = builder.Metadata.FindProperty(propertyName);
        var index = property is null ? null : builder.Metadata.FindIndex(new[] { property });
        if (index is not null)
        {
            builder.Metadata.RemoveIndex(index);
        }
    }
}
