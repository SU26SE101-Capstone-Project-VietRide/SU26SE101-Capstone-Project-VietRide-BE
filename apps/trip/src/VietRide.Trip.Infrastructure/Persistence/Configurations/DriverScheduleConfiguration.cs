using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class DriverScheduleConfiguration : IEntityTypeConfiguration<DriverSchedule>
{
    public void Configure(EntityTypeBuilder<DriverSchedule> builder)
    {
        builder.ToTable("driver_schedules", TripDbContext.SchemaName, tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "chk_driver_schedules_valid_until_after_from",
                "valid_until IS NULL OR valid_until >= valid_from");
            tableBuilder.HasCheckConstraint(
                "chk_driver_schedules_base_fare_non_negative",
                "base_fare IS NULL OR base_fare >= 0");
        });

        builder.HasKey(schedule => schedule.Id)
            .HasName("pk_driver_schedules");

        builder.Property(schedule => schedule.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(schedule => schedule.OperatorId)
            .HasColumnName("operator_id");

        builder.Property(schedule => schedule.RouteId)
            .HasColumnName("route_id");

        builder.Property(schedule => schedule.VehicleId)
            .HasColumnName("vehicle_id");

        builder.Property(schedule => schedule.DriverUserId)
            .HasColumnName("driver_user_id");

        builder.Property(schedule => schedule.AssistantUserId)
            .HasColumnName("assistant_user_id");

        builder.Property(schedule => schedule.DayOfWeek)
            .HasColumnName("day_of_week")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(schedule => schedule.DepartureTime)
            .HasColumnName("departure_time")
            .HasColumnType("time without time zone");

        builder.Property(schedule => schedule.ValidFrom)
            .HasColumnName("valid_from")
            .HasColumnType("date");

        builder.Property(schedule => schedule.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("date");

        builder.Property(schedule => schedule.BaseFare)
            .HasColumnName("base_fare")
            .HasColumnType("bigint")
            .HasConversion(
                fare => fare.HasValue ? fare.Value.Amount : (long?)null,
                amount => amount.HasValue ? Money.FromRaw(amount.Value) : (Money?)null)
            .IsRequired(false);

        builder.Property(schedule => schedule.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(schedule => schedule.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(schedule => schedule.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(schedule => schedule.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(schedule => schedule.RowVersion);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(schedule => schedule.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(schedule => schedule.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(schedule => new { schedule.OperatorId, schedule.IsActive })
            .HasDatabaseName("idx_driver_schedules_operator_active");

        builder.HasIndex(schedule => new { schedule.DriverUserId, schedule.IsActive })
            .HasDatabaseName("idx_driver_schedules_driver_active");

        builder.HasIndex(schedule => new { schedule.VehicleId, schedule.IsActive })
            .HasDatabaseName("idx_driver_schedules_vehicle_active")
            .HasFilter("vehicle_id IS NOT NULL");

        builder.HasIndex(schedule => new { schedule.RouteId, schedule.IsActive })
            .HasDatabaseName("idx_driver_schedules_route_active");

        builder.HasQueryFilter(schedule => schedule.DeletedAt == null);
    }
}
