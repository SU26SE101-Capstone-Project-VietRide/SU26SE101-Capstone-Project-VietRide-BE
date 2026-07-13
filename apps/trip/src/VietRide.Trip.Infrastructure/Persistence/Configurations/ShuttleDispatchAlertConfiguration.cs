using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class ShuttleDispatchAlertConfiguration : IEntityTypeConfiguration<ShuttleDispatchAlert>
{
    public void Configure(EntityTypeBuilder<ShuttleDispatchAlert> builder)
    {
        builder.ToTable("shuttle_dispatch_alerts", table => table.HasCheckConstraint(
            "chk_shuttle_dispatch_alerts_type",
            "alert_type IN ('WARNING_120', 'WARNING_60', 'AUTO_CUTOFF')"));
        builder.HasKey(x => x.Id).HasName("pk_shuttle_dispatch_alerts");
        builder.Ignore(x => x.RowVersion);
        builder.Ignore(x => x.UpdatedAt);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.MainTripId).HasColumnName("main_trip_id");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id");
        builder.Property(x => x.AlertType).HasColumnName("alert_type").HasMaxLength(20);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.MainTripId, x.AlertType }).IsUnique().HasDatabaseName("uq_shuttle_dispatch_alerts_trip_type");
        builder.HasIndex(x => new { x.OperatorId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_shuttle_dispatch_alerts_operator_created");
        builder.HasOne<Domain.Entities.Trip>().WithMany().HasForeignKey(x => x.MainTripId).OnDelete(DeleteBehavior.Restrict);
    }
}
