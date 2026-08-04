using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class RouteChangeProposalStopConfiguration : IEntityTypeConfiguration<RouteChangeProposalStop>
{
    public void Configure(EntityTypeBuilder<RouteChangeProposalStop> builder)
    {
        builder.ToTable("route_change_proposal_stops", table =>
        {
            table.HasCheckConstraint("chk_route_change_proposal_stops_order_positive", "order_index > 0");
            table.HasCheckConstraint("chk_route_change_proposal_stops_duration_non_negative", "estimated_duration_from_origin_minutes >= 0");
            table.HasCheckConstraint("chk_route_change_proposal_stops_distance_non_negative", "distance_from_origin_km IS NULL OR distance_from_origin_km >= 0");
        });
        builder.HasKey(x => new { x.ProposalId, x.StopId }).HasName("pk_route_change_proposal_stops");
        builder.Property(x => x.ProposalId).HasColumnName("proposal_id");
        builder.Property(x => x.StopId).HasColumnName("stop_id");
        builder.Property(x => x.OrderIndex).HasColumnName("order_index");
        builder.Property(x => x.EstimatedDurationFromOriginMinutes).HasColumnName("estimated_duration_from_origin_minutes");
        builder.Property(x => x.DistanceFromOriginKm).HasColumnName("distance_from_origin_km").HasColumnType("decimal(8,2)");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.HasOne<Stop>().WithMany().HasForeignKey(x => x.StopId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposal_stops_stop");
        builder.HasIndex(x => new { x.ProposalId, x.OrderIndex }).IsUnique().HasDatabaseName("uq_route_change_proposal_stops_order");
    }
}
