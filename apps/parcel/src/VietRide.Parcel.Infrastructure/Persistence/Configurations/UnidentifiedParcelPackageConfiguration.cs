using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class UnidentifiedParcelPackageConfiguration
    : IEntityTypeConfiguration<UnidentifiedParcelPackage>
{
    public void Configure(EntityTypeBuilder<UnidentifiedParcelPackage> builder)
    {
        builder.ToTable("unidentified_parcel_packages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TemporaryExceptionTag).HasColumnName("temporary_exception_tag").HasMaxLength(100).IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid");
        builder.Property(x => x.LocationType).HasColumnName("location_type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LocationId).HasColumnName("location_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.LocationSnapshot).HasColumnName("location_snapshot").HasMaxLength(500);
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        builder.Property(x => x.ObservedWeightKg).HasColumnName("observed_weight_kg").HasColumnType("numeric(10,3)");
        builder.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.MatchedParcelId).HasColumnName("matched_parcel_id").HasColumnType("uuid");
        builder.Property(x => x.MatchedAt).HasColumnName("matched_at");
        builder.Property(x => x.MatchedByUserId).HasColumnName("matched_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => new { x.OperatorId, x.TemporaryExceptionTag }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt });
        builder.HasIndex(x => x.MatchedParcelId).HasFilter("matched_parcel_id IS NOT NULL");
    }
}
