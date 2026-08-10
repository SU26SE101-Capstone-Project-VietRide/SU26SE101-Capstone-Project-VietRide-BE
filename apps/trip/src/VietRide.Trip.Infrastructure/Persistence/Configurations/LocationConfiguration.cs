using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations", table =>
        {
            table.HasCheckConstraint(
                "chk_locations_type",
                "type IN ('PROVINCE', 'MUNICIPALITY', 'WARD', 'COMMUNE', 'SPECIAL_ZONE')");
            table.HasCheckConstraint(
                "chk_locations_parent_level",
                "((type IN ('PROVINCE', 'MUNICIPALITY') AND parent_location_id IS NULL) OR "
                + "(type IN ('WARD', 'COMMUNE', 'SPECIAL_ZONE') AND parent_location_id IS NOT NULL))");
            table.HasCheckConstraint(
                "chk_locations_sort_order_non_negative",
                "sort_order >= 0");
        });

        builder.HasKey(location => location.Id)
            .HasName("pk_locations");

        builder.Property(location => location.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(location => location.Code)
            .HasColumnName("code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(location => location.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(location => location.Type)
            .HasColumnName("type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(location => location.ParentLocationId)
            .HasColumnName("parent_location_id")
            .HasColumnType("uuid");

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(location => location.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_locations_parent_location_id");

        builder.Property(location => location.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(location => location.SortOrder)
            .HasColumnName("sort_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(location => location.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(location => location.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(location => location.RowVersion);

        builder.HasIndex(location => location.Code)
            .HasDatabaseName("uq_locations_code")
            .IsUnique();

        builder.HasIndex(location => new { location.IsActive, location.SortOrder, location.Name })
            .HasDatabaseName("idx_locations_active_sort");

        builder.HasIndex(location => new { location.ParentLocationId, location.SortOrder, location.Name })
            .HasDatabaseName("idx_locations_active_parent_sort")
            .HasFilter("parent_location_id IS NOT NULL AND is_active = TRUE");

    }
}
