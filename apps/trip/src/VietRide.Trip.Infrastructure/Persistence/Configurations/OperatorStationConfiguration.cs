using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class OperatorStationConfiguration : IEntityTypeConfiguration<OperatorStation>
{
    public void Configure(EntityTypeBuilder<OperatorStation> builder)
    {
        builder.ToTable("operator_stations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.StationId)
            .HasColumnName("station_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.DisplayNameOverride)
            .HasColumnName("display_name_override")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.CounterLocation)
            .HasColumnName("counter_location")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(x => x.Instructions)
            .HasColumnName("instructions")
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

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OperatorId, x.StationId })
            .HasDatabaseName("uq_operator_stations_operator_station")
            .IsUnique();

        builder.HasIndex(x => x.OperatorId)
            .HasDatabaseName("idx_operator_stations_operator_id")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => x.StationId)
            .HasDatabaseName("idx_operator_stations_station_id")
            .HasFilter("is_active = TRUE");
    }
}
