using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleType>
{
    public void Configure(EntityTypeBuilder<VehicleType> builder)
    {
        builder.ToTable("vehicle_types", TripDbContext.SchemaName);

        builder.HasKey(vehicleType => vehicleType.Id)
            .HasName("pk_vehicle_types");

        builder.Property(vehicleType => vehicleType.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(vehicleType => vehicleType.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(vehicleType => vehicleType.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(vehicleType => vehicleType.EstimatedPassengerLuggageKgPerSeat)
            .HasColumnName("estimated_passenger_luggage_kg_per_seat");

        builder.Property(vehicleType => vehicleType.DefaultSeatCount)
            .HasColumnName("default_seat_count");

        builder.Property(vehicleType => vehicleType.IsSystemDefined)
            .HasColumnName("is_system_defined")
            .HasDefaultValue(false);

        builder.Property(vehicleType => vehicleType.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(vehicleType => vehicleType.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(vehicleType => vehicleType.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(vehicleType => vehicleType.RowVersion);

        builder.HasIndex(vehicleType => vehicleType.Code)
            .IsUnique()
            .HasDatabaseName("uq_vehicle_types_code");

        builder.HasIndex(vehicleType => vehicleType.IsActive)
            .HasDatabaseName("idx_vehicle_types_is_active");
    }
}
