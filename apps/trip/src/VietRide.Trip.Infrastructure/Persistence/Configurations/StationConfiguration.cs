using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.AddressStreet)
            .HasColumnName("address_street")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(x => x.LocationId)
            .HasColumnName("location_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.City)
            .HasColumnName("city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Province)
            .HasColumnName("province")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired(false);

        builder.Property(x => x.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired(false);

        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(x => x.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.OperatingHours)
            .HasColumnName("operating_hours")
            .HasColumnType("jsonb")
            .HasConversion(value => ToJsonElement(value), value => ToJsonString(value))
            .IsRequired(false);

        builder.Property(x => x.Facilities)
            .HasColumnName("facilities")
            .HasColumnType("jsonb")
            .HasConversion(value => ToJsonElement(value), value => ToJsonString(value))
            .IsRequired(false);

        builder.Property(x => x.SupportsShuttle)
            .HasColumnName("supports_shuttle")
            .HasDefaultValue(false)
            .IsRequired();

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

        builder.HasIndex(x => x.Slug)
            .HasDatabaseName("uq_stations_slug")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(x => new { x.City, x.Province })
            .HasDatabaseName("idx_stations_city_province")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => x.LocationId)
            .HasDatabaseName("idx_stations_location_id")
            .HasFilter("location_id IS NOT NULL AND is_active = TRUE");

        builder.HasIndex(x => x.SupportsShuttle)
            .HasDatabaseName("idx_stations_supports_shuttle")
            .HasFilter("is_active = TRUE");

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Name)
            .HasDatabaseName("idx_stations_name_trgm")
            .HasMethod("gin")
            .HasFilter("FALSE");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }

    private static JsonElement? ToJsonElement(string? value)
    {
        if (value is null)
            return null;

        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static string? ToJsonString(JsonElement? value)
        => value?.GetRawText();
}
