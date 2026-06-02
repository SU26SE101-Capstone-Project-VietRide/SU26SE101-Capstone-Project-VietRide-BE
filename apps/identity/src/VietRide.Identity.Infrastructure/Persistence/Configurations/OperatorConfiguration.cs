using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Day-3 stub mapping — <c>operators</c> table with <c>id UUID PK DEFAULT gen_random_uuid()</c> ONLY.
/// Day 6 ALTER TABLE adds the remaining ~30 columns + creates the <c>operator_registration_status</c>
/// enum + adds NOT NULL/CHECK/UNIQUE constraints.
/// </summary>
internal sealed class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operators");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        // Day-3 stub: only the PK column is mapped here.
        // Task 3.2 note: EF will also emit CreatedAt/UpdatedAt columns from BaseEntity<Guid>
        // via IAuditable. These are deliberately ignored here (stub migration) and will be
        // reconciled in the Day-6 migration when the full schema lands.
        //
        // *** DAY-6 CARRY-OVER WARNING ***
        // The three Ignore(...) calls below MUST be removed in the Day-6 task when the full
        // `operators` schema + columns (created_at, updated_at, row_version, ~30 additional
        // columns + operator_registration_status enum) are added via ALTER TABLE migration.
        // Leaving them in place after Day 6 will cause EF to silently skip those columns.
        builder.Ignore(o => o.CreatedAt);
        builder.Ignore(o => o.UpdatedAt);
        builder.Ignore(o => o.RowVersion);
    }
}
