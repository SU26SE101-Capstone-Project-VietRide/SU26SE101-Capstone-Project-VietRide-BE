using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class AlternativeRouteConfiguration : IEntityTypeConfiguration<AlternativeRoute>
{
    public void Configure(EntityTypeBuilder<AlternativeRoute> builder)
    {
        builder.ToTable("alternative_routes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RouteId)
            .HasColumnName("route_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.DestinationStationId)
            .HasColumnName("destination_station_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.TotalDistanceKm)
            .HasColumnName("total_distance_km")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);

        builder.Property(x => x.EstimatedDurationMinutes)
            .HasColumnName("estimated_duration_minutes")
            .IsRequired(false);

        builder.Property(x => x.PathPolyline)
            .HasColumnName("path_polyline")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
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

        builder.Ignore(x => x.RowVersion);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.DestinationStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.RouteId)
            .HasDatabaseName("idx_alternative_routes_route_id")
            .HasFilter("is_active = TRUE");

        RemoveConventionIndex(builder, nameof(AlternativeRoute.DestinationStationId));
    }

    private static void RemoveConventionIndex(EntityTypeBuilder<AlternativeRoute> builder, string propertyName)
    {
        var property = builder.Metadata.FindProperty(propertyName);
        var index = property is null ? null : builder.Metadata.FindIndex(new[] { property });
        if (index is not null)
        {
            builder.Metadata.RemoveIndex(index);
        }
    }
}
