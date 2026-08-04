using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class RouteChangeProposalConfiguration : IEntityTypeConfiguration<RouteChangeProposal>
{
    public void Configure(EntityTypeBuilder<RouteChangeProposal> builder)
    {
        builder.ToTable("route_change_proposals", table =>
        {
            table.HasCheckConstraint("chk_route_change_proposals_reason", "char_length(btrim(reason)) BETWEEN 1 AND 500");
            table.HasCheckConstraint("chk_route_change_proposals_rejection_reason", "rejection_reason IS NULL OR char_length(rejection_reason) <= 500");
            table.HasCheckConstraint("chk_route_change_proposals_source", "(type = 'EXISTING' AND source_alternative_route_id IS NOT NULL AND source_updated_at IS NOT NULL) OR (type = 'CUSTOM' AND source_alternative_route_id IS NULL AND source_updated_at IS NULL)");
            table.HasCheckConstraint("chk_route_change_proposals_custom_geometry", "type <> 'CUSTOM' OR (snapshot_path_polyline IS NOT NULL AND char_length(btrim(snapshot_path_polyline)) > 0)");
        });
        builder.HasKey(x => x.Id).HasName("pk_route_change_proposals");
        builder.Ignore(x => x.RowVersion);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id");
        builder.Property(x => x.ProposedByUserId).HasColumnName("proposed_by_user_id");
        builder.Property(x => x.Type).HasColumnName("type").HasColumnType("vietride_trip.route_change_proposal_type");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("vietride_trip.route_change_proposal_status").HasDefaultValueSql("'PENDING'::vietride_trip.route_change_proposal_status");
        builder.Property(x => x.SourceAlternativeRouteId).HasColumnName("source_alternative_route_id");
        builder.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at");
        builder.Property(x => x.IncidentId).HasColumnName("incident_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(x => x.Name).HasColumnName("snapshot_name").HasMaxLength(255);
        builder.Property(x => x.Description).HasColumnName("snapshot_description");
        builder.Property(x => x.DestinationStationId).HasColumnName("snapshot_destination_station_id");
        builder.Property(x => x.TotalDistanceKm).HasColumnName("snapshot_total_distance_km").HasColumnType("decimal(8,2)");
        builder.Property(x => x.EstimatedDurationMinutes).HasColumnName("snapshot_estimated_duration_minutes");
        builder.Property(x => x.PathPolyline).HasColumnName("snapshot_path_polyline");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(x => x.SupersededByProposalId).HasColumnName("superseded_by_proposal_id");
        builder.Property(x => x.ApprovedAlternativeRouteId).HasColumnName("approved_alternative_route_id");
        builder.Property(x => x.ResolutionCode).HasColumnName("resolution_code").HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.HasOne<Domain.Entities.Trip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposals_trip");
        builder.HasOne<AlternativeRoute>().WithMany().HasForeignKey(x => x.SourceAlternativeRouteId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposals_source_alternative_route");
        builder.HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposals_incident");
        builder.HasOne<RouteChangeProposal>().WithMany().HasForeignKey(x => x.SupersededByProposalId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposals_superseded_by");
        builder.HasOne<AlternativeRoute>().WithMany().HasForeignKey(x => x.ApprovedAlternativeRouteId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_change_proposals_approved_alternative_route");
        builder.HasIndex(x => new { x.TripId, x.Status }).HasDatabaseName("idx_route_change_proposals_trip_status");
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("idx_route_change_proposals_operator_status_created");
        builder.HasIndex(x => new { x.ProposedByUserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_route_change_proposals_proposer_created");
        builder.HasIndex(x => x.SourceAlternativeRouteId).HasDatabaseName("idx_route_change_proposals_source").HasFilter("source_alternative_route_id IS NOT NULL AND status = 'PENDING'");
        builder.HasIndex(x => x.SupersededByProposalId).HasDatabaseName("idx_route_change_proposals_superseded_by").HasFilter("superseded_by_proposal_id IS NOT NULL");
        builder.HasIndex(x => x.ApprovedAlternativeRouteId).HasDatabaseName("idx_route_change_proposals_approved_route").HasFilter("approved_alternative_route_id IS NOT NULL");
        builder.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_route_change_proposal_stops_proposal");
        builder.Navigation(x => x.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
