using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", TripDbContext.SchemaName, tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("chk_vehicles_total_seats_positive", "total_seats > 0");
            tableBuilder.HasCheckConstraint(
                "chk_vehicles_cargo_weight_non_negative",
                "max_cargo_weight_kg IS NULL OR max_cargo_weight_kg >= 0");
        });

        builder.HasKey(vehicle => vehicle.Id)
            .HasName("pk_vehicles");

        builder.Property(vehicle => vehicle.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(vehicle => vehicle.OperatorId)
            .HasColumnName("operator_id");

        builder.Property(vehicle => vehicle.VehicleTypeId)
            .HasColumnName("vehicle_type_id");

        builder.Property(vehicle => vehicle.LicensePlate)
            .HasColumnName("license_plate")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(vehicle => vehicle.SeatLayoutJson)
            .HasColumnName("seat_layout_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(vehicle => vehicle.TotalSeats)
            .HasColumnName("total_seats");

        builder.Property(vehicle => vehicle.MaxCargoWeightKg)
            .HasColumnName("max_cargo_weight_kg")
            .HasColumnType("decimal(8,2)");

        builder.Property(vehicle => vehicle.MaxCargoVolumeM3)
            .HasColumnName("max_cargo_volume_m3")
            .HasColumnType("decimal(8,2)");

        builder.Property(vehicle => vehicle.Status)
            .HasColumnName("status")
            .HasColumnType("vehicle_status")
            .HasDefaultValueSql("'ACTIVE'::vehicle_status");

        builder.Property(vehicle => vehicle.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(vehicle => vehicle.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(vehicle => vehicle.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(vehicle => vehicle.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(vehicle => vehicle.RowVersion);

        builder.HasQueryFilter(vehicle => vehicle.DeletedAt == null);

        builder.HasOne<VehicleType>()
            .WithMany()
            .HasForeignKey(vehicle => vehicle.VehicleTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(vehicle => vehicle.LicensePlate)
            .IsUnique()
            .HasDatabaseName("uq_vehicles_license_plate")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(vehicle => new { vehicle.OperatorId, vehicle.Status })
            .HasDatabaseName("idx_vehicles_operator_status")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(vehicle => vehicle.VehicleTypeId)
            .HasDatabaseName("idx_vehicles_vehicle_type_id");
    }
}
