using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCustodyEventConfiguration : IEntityTypeConfiguration<ParcelCustodyEvent>
{
    public void Configure(EntityTypeBuilder<ParcelCustodyEvent> builder)
    {
        builder.ToTable("parcel_custody_events", table =>
        {
            table.HasCheckConstraint("chk_parcel_custody_events_sequence_positive", "sequence > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.LegId).HasColumnName("leg_id").HasColumnType("uuid");
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ExpectedLocationType).HasColumnName("expected_location_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ExpectedLocationId).HasColumnName("expected_location_id").HasColumnType("uuid");
        builder.Property(x => x.ActualLocationType).HasColumnName("actual_location_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ActualLocationId).HasColumnName("actual_location_id").HasColumnType("uuid");
        builder.Property(x => x.LocationSnapshot).HasColumnName("location_snapshot").HasMaxLength(500);
        builder.Property(x => x.VehicleId).HasColumnName("vehicle_id").HasColumnType("uuid");
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasColumnType("uuid");
        builder.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParcelTransitLeg>().WithMany().HasForeignKey(x => x.LegId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ParcelId, x.OccurredAt, x.Id });
        builder.HasIndex(x => new { x.TripId, x.ActualLocationId, x.OccurredAt });
        builder.HasIndex(x => new { x.ParcelId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
    }
}
