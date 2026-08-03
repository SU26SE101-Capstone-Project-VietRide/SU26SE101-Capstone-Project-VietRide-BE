using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class OperatorFareSurchargePeriodConfiguration : IEntityTypeConfiguration<OperatorFareSurchargePeriod>
{
    public void Configure(EntityTypeBuilder<OperatorFareSurchargePeriod> builder)
    {
        builder.HasAnnotation(
            "VietRide:ExclusionConstraint:ex_operator_fare_surcharge_periods_no_active_overlap",
            "EXCLUDE USING gist (operator_id WITH =, daterange(start_date, end_date + 1, '[)') WITH &&) WHERE (is_active = TRUE AND deleted_at IS NULL)");

        builder.ToTable("operator_fare_surcharge_periods", table =>
        {
            table.HasCheckConstraint(
                "chk_operator_fare_surcharge_periods_name_not_blank",
                "length(btrim(name)) BETWEEN 1 AND 120");
            table.HasCheckConstraint(
                "chk_operator_fare_surcharge_periods_date_order",
                "start_date <= end_date");
            table.HasCheckConstraint(
                "chk_operator_fare_surcharge_periods_percent",
                "surcharge_percent BETWEEN 1 AND 100");
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
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(x => x.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(x => x.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(x => x.SurchargePercent)
            .HasColumnName("surcharge_percent")
            .HasColumnType("smallint")
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

        builder.HasIndex(x => new { x.OperatorId, x.StartDate, x.Id })
            .HasDatabaseName("idx_operator_fare_surcharge_periods_operator_start")
            .HasFilter("deleted_at IS NULL");
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
