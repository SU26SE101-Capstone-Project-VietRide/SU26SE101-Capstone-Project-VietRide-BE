using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("routes", table =>
        {
            table.HasCheckConstraint(
                "chk_routes_origin_dest_different",
                "origin_station_id <> destination_station_id");

            table.HasCheckConstraint(
                "chk_routes_base_fare_non_negative",
                "base_fare >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(x => x.OriginStationId)
            .HasColumnName("origin_station_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.DestinationStationId)
            .HasColumnName("destination_station_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ReturnRouteId)
            .HasColumnName("return_route_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.BaseFare)
            .HasColumnName("base_fare")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
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

        builder.Property(x => x.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.OriginStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.DestinationStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(x => x.ReturnRouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OperatorId)
            .HasDatabaseName("idx_routes_operator_id")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => new { x.OriginStationId, x.DestinationStationId })
            .HasDatabaseName("idx_routes_origin_destination")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => x.ReturnRouteId)
            .HasDatabaseName("idx_routes_return_route_id")
            .HasFilter("return_route_id IS NOT NULL");

        builder.HasIndex(x => new { x.OperatorId, x.Code })
            .HasDatabaseName("uq_routes_operator_code")
            .HasFilter("deleted_at IS NULL AND code IS NOT NULL")
            .IsUnique();

        RemoveConventionIndex(builder, nameof(Route.DestinationStationId));

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }

    private static void RemoveConventionIndex(EntityTypeBuilder<Route> builder, string propertyName)
    {
        var property = builder.Metadata.FindProperty(propertyName);
        var index = property is null ? null : builder.Metadata.FindIndex(new[] { property });
        if (index is not null)
        {
            builder.Metadata.RemoveIndex(index);
        }
    }
}
