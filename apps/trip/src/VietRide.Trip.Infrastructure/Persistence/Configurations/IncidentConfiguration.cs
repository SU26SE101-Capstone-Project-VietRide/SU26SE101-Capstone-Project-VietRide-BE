using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.HasKey(incident => incident.Id).HasName("pk_incidents");
        builder.Ignore(incident => incident.RowVersion);

        builder.Property(incident => incident.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(incident => incident.TripId).HasColumnName("trip_id");
        builder.Property(incident => incident.ReportedByUserId).HasColumnName("reported_by_user_id");
        builder.Property(incident => incident.Category)
            .HasColumnName("category")
            .HasColumnType("vietride_trip.incident_category");
        builder.Property(incident => incident.Description).HasColumnName("description");
        builder.Property(incident => incident.PhotoUrls)
            .HasColumnName("photo_urls")
            .HasColumnType("jsonb")
            .HasConversion(
                value => System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null),
                value => System.Text.Json.JsonSerializer.Deserialize<string[]>(value, (System.Text.Json.JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<string>?>(
                (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
                value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
                value => value == null ? null : value.ToArray()));
        builder.Property(incident => incident.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("decimal(10,7)");
        builder.Property(incident => incident.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("decimal(10,7)");
        builder.Property(incident => incident.ReportedAt)
            .HasColumnName("reported_at")
            .HasDefaultValueSql("now()");
        builder.Property(incident => incident.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(incident => incident.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(incident => incident.ResolutionNote).HasColumnName("resolution_note");
        builder.Property(incident => incident.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Property(incident => incident.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(incident => incident.TripId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_incidents_trips_trip_id");

        builder.HasIndex(incident => incident.TripId).HasDatabaseName("idx_incidents_trip_id");
        builder.HasIndex(incident => incident.ReportedByUserId).HasDatabaseName("idx_incidents_reported_by");
        builder.HasIndex(incident => incident.ReportedAt)
            .IsDescending()
            .HasDatabaseName("idx_incidents_reported_at");
    }
}
