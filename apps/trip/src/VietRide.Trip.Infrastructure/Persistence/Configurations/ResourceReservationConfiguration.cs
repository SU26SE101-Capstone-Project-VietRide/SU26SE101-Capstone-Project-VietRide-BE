using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class ResourceReservationConfiguration : IEntityTypeConfiguration<ResourceReservation>
{
    public void Configure(EntityTypeBuilder<ResourceReservation> builder)
    {
        builder.HasAnnotation(
            "VietRide:ExclusionConstraint:ex_resource_reservations_no_overlap",
            "EXCLUDE USING gist (resource_type WITH =, resource_id WITH =, tstzrange(planned_start_at, planned_end_at, '[)') WITH &&) WHERE (status IN ('RESERVED', 'ACTIVE'))");

        builder.ToTable("resource_reservations", table =>
        {
            table.HasCheckConstraint(
                "chk_resource_reservations_source",
                "num_nonnulls(trip_id, shuttle_trip_id) = 1");
            table.HasCheckConstraint(
                "chk_resource_reservations_period",
                "planned_end_at > planned_start_at");
            table.HasCheckConstraint(
                "chk_resource_reservations_type",
                "resource_type IN ('CREW', 'VEHICLE')");
            table.HasCheckConstraint(
                "chk_resource_reservations_role",
                "resource_role IN ('DRIVER', 'ASSISTANT', 'VEHICLE')");
            table.HasCheckConstraint(
                "chk_resource_reservations_type_role",
                "(resource_type = 'VEHICLE' AND resource_role = 'VEHICLE') OR (resource_type = 'CREW' AND resource_role IN ('DRIVER', 'ASSISTANT'))");
            table.HasCheckConstraint(
                "chk_resource_reservations_status",
                "status IN ('RESERVED', 'ACTIVE', 'RELEASED', 'CANCELLED')");
            table.HasCheckConstraint(
                "chk_resource_reservations_start_coordinates",
                "(start_latitude IS NULL) = (start_longitude IS NULL)");
            table.HasCheckConstraint(
                "chk_resource_reservations_end_coordinates",
                "(end_latitude IS NULL) = (end_longitude IS NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_resource_reservations");
        builder.Ignore(x => x.RowVersion);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id");
        builder.Property(x => x.ResourceType).HasColumnName("resource_type").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ResourceRole).HasColumnName("resource_role").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ResourceId).HasColumnName("resource_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.ShuttleTripId).HasColumnName("shuttle_trip_id");
        builder.Property(x => x.PlannedStartAt).HasColumnName("planned_start_at");
        builder.Property(x => x.PlannedEndAt).HasColumnName("planned_end_at");
        builder.Property(x => x.StartStationId).HasColumnName("start_station_id");
        builder.Property(x => x.EndStationId).HasColumnName("end_station_id");
        builder.Property(x => x.StartLatitude).HasColumnName("start_latitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.StartLongitude).HasColumnName("start_longitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.EndLatitude).HasColumnName("end_latitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.EndLongitude).HasColumnName("end_longitude").HasColumnType("decimal(10,7)");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.ResourceType, x.ResourceId, x.PlannedStartAt })
            .HasDatabaseName("idx_resource_reservations_resource_start")
            .HasFilter("status IN ('RESERVED', 'ACTIVE')");
        builder.HasIndex(x => new { x.TripId, x.ResourceRole })
            .IsUnique()
            .HasDatabaseName("uq_resource_reservations_trip_role")
            .HasFilter("trip_id IS NOT NULL");
        builder.HasIndex(x => new { x.ShuttleTripId, x.ResourceRole })
            .IsUnique()
            .HasDatabaseName("uq_resource_reservations_shuttle_role")
            .HasFilter("shuttle_trip_id IS NOT NULL");
        builder.HasIndex(x => new { x.OperatorId, x.Status })
            .HasDatabaseName("idx_resource_reservations_operator_status");

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ShuttleTrip>()
            .WithMany()
            .HasForeignKey(x => x.ShuttleTripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.StartStationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.EndStationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
