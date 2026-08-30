using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelIncidentConfiguration : IEntityTypeConfiguration<ParcelIncident>
{
    public void Configure(EntityTypeBuilder<ParcelIncident> builder)
    {
        builder.ToTable("parcel_incidents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid");
        builder.Property(x => x.LegId).HasColumnName("leg_id").HasColumnType("uuid");
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ExpectedLocation).HasColumnName("expected_location").HasMaxLength(500);
        builder.Property(x => x.LastKnownLocation).HasColumnName("last_known_location").HasMaxLength(500);
        builder.Property(x => x.ReporterId).HasColumnName("reporter_id").HasColumnType("uuid");
        builder.Property(x => x.ReporterSource).HasColumnName("reporter_source").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("jsonb");
        builder.Property(x => x.SearchDeadline).HasColumnName("search_deadline");
        builder.Property(x => x.EscalatedAt).HasColumnName("escalated_at");
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.ResolutionCode).HasColumnName("resolution_code").HasMaxLength(64);
        builder.Property(x => x.ResolutionNote).HasColumnName("resolution_note").HasColumnType("text");
        builder.Property(x => x.OperatorProcessBreach).HasColumnName("operator_process_breach").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.ParcelId, x.Status });
        builder.HasIndex(x => new { x.SearchDeadline, x.Status });
        builder.HasIndex(x => new { x.ParcelId, x.Type }).IsUnique().HasFilter("status NOT IN ('CLOSED', 'RESOLVED')");
    }
}
