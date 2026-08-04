using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Trip.Domain.Entities;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteChangeProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .Annotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .Annotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_trip.route_change_proposal_status", "PENDING,APPROVED,REJECTED,SUPERSEDED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_trip.route_change_proposal_type", "EXISTING,CUSTOM")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "route_change_proposals",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<RouteChangeProposalType>(type: "vietride_trip.route_change_proposal_type", nullable: false),
                    status = table.Column<RouteChangeProposalStatus>(type: "vietride_trip.route_change_proposal_status", nullable: false, defaultValueSql: "'PENDING'::vietride_trip.route_change_proposal_status"),
                    source_alternative_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    snapshot_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    snapshot_description = table.Column<string>(type: "text", nullable: true),
                    snapshot_destination_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_total_distance_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    snapshot_estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    snapshot_path_polyline = table.Column<string>(type: "text", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    superseded_by_proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_alternative_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_change_proposals", x => x.id);
                    table.CheckConstraint("chk_route_change_proposals_reason", "char_length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("chk_route_change_proposals_rejection_reason", "rejection_reason IS NULL OR char_length(rejection_reason) <= 500");
                    table.CheckConstraint("chk_route_change_proposals_source", "(type = 'EXISTING' AND source_alternative_route_id IS NOT NULL AND source_updated_at IS NOT NULL) OR (type = 'CUSTOM' AND source_alternative_route_id IS NULL AND source_updated_at IS NULL)");
                    table.CheckConstraint("chk_route_change_proposals_custom_geometry", "type <> 'CUSTOM' OR (snapshot_path_polyline IS NOT NULL AND char_length(btrim(snapshot_path_polyline)) > 0)");
                    table.ForeignKey(
                        name: "fk_route_change_proposals_approved_alternative_route",
                        column: x => x.approved_alternative_route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "alternative_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_change_proposals_incident",
                        column: x => x.incident_id,
                        principalSchema: "vietride_trip",
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_change_proposals_source_alternative_route",
                        column: x => x.source_alternative_route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "alternative_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_change_proposals_superseded_by",
                        column: x => x.superseded_by_proposal_id,
                        principalSchema: "vietride_trip",
                        principalTable: "route_change_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_route_change_proposals_trip",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_change_proposal_stops",
                schema: "vietride_trip",
                columns: table => new
                {
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    estimated_duration_from_origin_minutes = table.Column<int>(type: "integer", nullable: false),
                    distance_from_origin_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_change_proposal_stops", x => new { x.proposal_id, x.stop_id });
                    table.CheckConstraint("chk_route_change_proposal_stops_distance_non_negative", "distance_from_origin_km IS NULL OR distance_from_origin_km >= 0");
                    table.CheckConstraint("chk_route_change_proposal_stops_duration_non_negative", "estimated_duration_from_origin_minutes >= 0");
                    table.CheckConstraint("chk_route_change_proposal_stops_order_positive", "order_index > 0");
                    table.ForeignKey(
                        name: "fk_route_change_proposal_stops_proposal",
                        column: x => x.proposal_id,
                        principalSchema: "vietride_trip",
                        principalTable: "route_change_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_route_change_proposal_stops_stop",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_route_change_proposal_stops_order",
                schema: "vietride_trip",
                table: "route_change_proposal_stops",
                columns: new[] { "proposal_id", "order_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_approved_route",
                schema: "vietride_trip",
                table: "route_change_proposals",
                column: "approved_alternative_route_id",
                filter: "approved_alternative_route_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_operator_status_created",
                schema: "vietride_trip",
                table: "route_change_proposals",
                columns: new[] { "operator_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_proposer_created",
                schema: "vietride_trip",
                table: "route_change_proposals",
                columns: new[] { "proposed_by_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_source",
                schema: "vietride_trip",
                table: "route_change_proposals",
                column: "source_alternative_route_id",
                filter: "source_alternative_route_id IS NOT NULL AND status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_superseded_by",
                schema: "vietride_trip",
                table: "route_change_proposals",
                column: "superseded_by_proposal_id",
                filter: "superseded_by_proposal_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_route_change_proposals_trip_status",
                schema: "vietride_trip",
                table: "route_change_proposals",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION vietride_trip.trg_set_route_change_proposal_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_route_change_proposals_updated_at
                BEFORE UPDATE ON vietride_trip.route_change_proposals
                FOR EACH ROW EXECUTE FUNCTION vietride_trip.trg_set_route_change_proposal_updated_at();

                CREATE TRIGGER trg_route_change_proposal_stops_updated_at
                BEFORE UPDATE ON vietride_trip.route_change_proposal_stops
                FOR EACH ROW EXECUTE FUNCTION vietride_trip.trg_set_route_change_proposal_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_route_change_proposal_stops_updated_at
                    ON vietride_trip.route_change_proposal_stops;
                DROP TRIGGER IF EXISTS trg_route_change_proposals_updated_at
                    ON vietride_trip.route_change_proposals;
                DROP FUNCTION IF EXISTS vietride_trip.trg_set_route_change_proposal_updated_at();
                """);

            migrationBuilder.DropTable(
                name: "route_change_proposal_stops",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "route_change_proposals",
                schema: "vietride_trip");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .Annotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .Annotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_status", "PENDING,APPROVED,REJECTED,SUPERSEDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_type", "EXISTING,CUSTOM")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}
