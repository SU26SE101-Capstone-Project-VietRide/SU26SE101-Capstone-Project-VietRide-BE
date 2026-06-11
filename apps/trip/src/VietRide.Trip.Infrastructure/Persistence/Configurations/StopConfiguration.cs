using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class StopConfiguration : IEntityTypeConfiguration<Stop>
{
    public void Configure(EntityTypeBuilder<Stop> builder)
    {
        builder.ToTable("stops", table =>
        {
            table.HasCheckConstraint(
                "chk_stops_no_self_replacement",
                "replaced_by_stop_id IS NULL OR replaced_by_stop_id <> id");
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

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(x => x.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(x => x.Address)
            .HasColumnName("address")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(x => x.GooglePlaceId)
            .HasColumnName("google_place_id")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.SharedSuggestion)
            .HasColumnName("shared_suggestion")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.ReplacedByStopId)
            .HasColumnName("replaced_by_stop_id")
            .HasColumnType("uuid")
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

        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(x => x.ReplacedByStopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OperatorId)
            .HasDatabaseName("idx_stops_operator_id")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => x.ReplacedByStopId)
            .HasDatabaseName("idx_stops_replaced_by")
            .HasFilter("replaced_by_stop_id IS NOT NULL");

        builder.HasIndex(x => x.SharedSuggestion)
            .HasDatabaseName("idx_stops_shared_suggestion")
            .HasFilter("shared_suggestion = TRUE AND is_active = TRUE");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
