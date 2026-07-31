using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelStatusHistoryConfiguration : IEntityTypeConfiguration<ParcelStatusHistory>
{
    private const string ParcelStatusType = $"{ParcelDbContext.SchemaName}.parcel_status";

    public void Configure(EntityTypeBuilder<ParcelStatusHistory> builder)
    {
        builder.ToTable("parcel_status_history");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(history => history.ParcelId)
            .HasColumnName("parcel_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(history => history.Status)
            .HasColumnName("status")
            .HasColumnType(ParcelStatusType)
            .IsRequired();

        builder.Property(history => history.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(history => history.ActorType)
            .HasColumnName("actor_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(history => history.ActorId)
            .HasColumnName("actor_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(history => history.Source)
            .HasColumnName("source")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasColumnName("reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.HasOne<ParcelEntity>()
            .WithMany()
            .HasForeignKey(history => history.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new { history.ParcelId, history.OccurredAt, history.Id })
            .HasDatabaseName("idx_parcel_status_history_parcel_occurred_id");

        builder.HasIndex(history => history.ParcelId)
            .HasDatabaseName("uq_parcel_status_history_migration_baseline")
            .HasFilter("source = 'MIGRATION_BASELINE'")
            .IsUnique();
    }
}
