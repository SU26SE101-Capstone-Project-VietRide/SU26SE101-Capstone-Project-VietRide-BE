using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripGenerationSkipLogConfiguration : IEntityTypeConfiguration<TripGenerationSkipLog>
{
    public void Configure(EntityTypeBuilder<TripGenerationSkipLog> builder)
    {
        builder.ToTable("trip_generation_skip_logs");

        builder.HasKey(log => log.Id).HasName("pk_trip_generation_skip_logs");
        builder.Ignore(log => log.RowVersion);

        builder.Property(log => log.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(log => log.OperatorId).HasColumnName("operator_id");
        builder.Property(log => log.DriverScheduleId).HasColumnName("driver_schedule_id");
        builder.Property(log => log.SkippedDate).HasColumnName("skipped_date");
        builder.Property(log => log.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasColumnType("vietride_trip.trip_generation_skip_reason");
        builder.Property(log => log.Message).HasColumnName("message");
        builder.Property(log => log.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Ignore(log => log.UpdatedAt);

        builder.HasIndex(log => new { log.OperatorId, log.SkippedDate })
            .IsDescending(false, true)
            .HasDatabaseName("idx_trip_gen_skip_logs_operator_date");
        builder.HasIndex(log => new { log.DriverScheduleId, log.SkippedDate })
            .IsDescending(false, true)
            .HasDatabaseName("idx_trip_gen_skip_logs_schedule");

        builder.HasOne<DriverSchedule>()
            .WithMany()
            .HasForeignKey(log => log.DriverScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
