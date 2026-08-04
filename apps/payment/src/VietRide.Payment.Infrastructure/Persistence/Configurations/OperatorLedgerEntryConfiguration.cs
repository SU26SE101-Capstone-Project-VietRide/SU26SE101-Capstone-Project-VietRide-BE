using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class OperatorLedgerEntryConfiguration : IEntityTypeConfiguration<OperatorLedgerEntry>
{
    public void Configure(EntityTypeBuilder<OperatorLedgerEntry> builder)
    {
        builder.ToTable("operator_ledger_entries", table =>
        {
            table.HasCheckConstraint(
                "chk_operator_ledger_entries_amount_direction",
                "(entry_type IN ('BOOKING_REFUND','PARCEL_REFUND') AND amount < 0) OR " +
                "(entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND amount = 0) OR " +
                "(entry_type = 'ADJUSTMENT') OR " +
                "(entry_type NOT IN ('BOOKING_REFUND','PARCEL_REFUND','VOUCHER_OPERATOR_FUNDED_AUDIT','ADJUSTMENT') AND amount > 0)");
            table.HasCheckConstraint(
                "chk_operator_ledger_entries_trip_required",
                "entry_type = 'ADJUSTMENT' OR trip_id IS NOT NULL");
            table.HasCheckConstraint(
                "chk_operator_ledger_entries_actor_type",
                "actor_type IN ('USER','SYSTEM')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid");
        builder.Property(x => x.EntryType).HasColumnName("entry_type").HasColumnType($"{PaymentDbContext.SchemaName}.operator_ledger_entry_type").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.ReferenceType).HasColumnName("reference_type").HasColumnType($"{PaymentDbContext.SchemaName}.operator_ledger_reference_type").IsRequired();
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(x => x.ActorType).HasColumnName("actor_type").HasConversion<string>().HasMaxLength(16).HasDefaultValueSql("'SYSTEM'").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasColumnType("uuid");
        builder.Property(x => x.ActorDisplayName).HasColumnName("actor_display_name").HasMaxLength(200);
        builder.Property(x => x.ActorEmail).HasColumnName("actor_email").HasMaxLength(320);
        builder.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(50);
        builder.Property(x => x.ActorSnapshotResolved).HasColumnName("actor_snapshot_resolved").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => new { x.OperatorId, x.CreatedAt }).HasDatabaseName("idx_operator_ledger_entries_operator_id_created_at").IsDescending(false, true);
        builder.HasIndex(x => new { x.OperatorId, x.TripId }).HasDatabaseName("idx_operator_ledger_entries_operator_trip").HasFilter("trip_id IS NOT NULL");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).HasDatabaseName("idx_operator_ledger_entries_reference");
        builder.HasIndex(x => new { x.OperatorId, x.EntryType }).HasDatabaseName("idx_operator_ledger_entries_entry_type");
        builder.HasIndex(x => new { x.SourceEventId, x.EntryType, x.ReferenceId }).HasDatabaseName("uq_operator_ledger_entries_source").IsUnique();
        builder.HasIndex(x => x.ActorUserId).HasDatabaseName("idx_operator_ledger_entries_actor_user_id").HasFilter("actor_user_id IS NOT NULL");
    }
}
