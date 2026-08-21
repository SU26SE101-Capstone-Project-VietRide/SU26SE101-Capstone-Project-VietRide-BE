using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelTransitLegConfiguration : IEntityTypeConfiguration<ParcelTransitLeg>
{
    public void Configure(EntityTypeBuilder<ParcelTransitLeg> builder)
    {
        builder.ToTable("parcel_transit_legs", table =>
        {
            table.HasCheckConstraint("chk_parcel_transit_legs_sequence_positive", "sequence > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.ExpectedOriginId).HasColumnName("expected_origin_id").HasColumnType("uuid");
        builder.Property(x => x.ExpectedDestinationId).HasColumnName("expected_destination_id").HasColumnType("uuid");
        builder.Property(x => x.ExpectedOriginName).HasColumnName("expected_origin_name").HasMaxLength(255);
        builder.Property(x => x.ExpectedDestinationName).HasColumnName("expected_destination_name").HasMaxLength(255);
        builder.Property(x => x.ActualOriginId).HasColumnName("actual_origin_id").HasColumnType("uuid");
        builder.Property(x => x.ActualDestinationId).HasColumnName("actual_destination_id").HasColumnType("uuid");
        builder.Property(x => x.VehicleId).HasColumnName("vehicle_id").HasColumnType("uuid");
        builder.Property(x => x.VehicleLicensePlate).HasColumnName("vehicle_license_plate").HasMaxLength(20);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ParcelId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.TripId, x.Status });
        builder.HasIndex(x => new { x.OperatorId, x.Status });
    }
}
