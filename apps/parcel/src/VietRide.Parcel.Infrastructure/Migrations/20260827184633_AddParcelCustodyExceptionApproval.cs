using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelCustodyExceptionApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_custody_exception_requests",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actual_location_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actual_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    temporary_exception_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    observed_weight_kg = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    evidence_references_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    approved_custody_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_custody_exception_requests", x => x.id);
                    table.CheckConstraint("chk_parcel_custody_exception_request_status", "status IN ('PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'CANCELLED')");
                    table.CheckConstraint("chk_parcel_custody_exception_review_audit", "(status = 'PENDING_APPROVAL' AND reviewed_by_user_id IS NULL AND reviewed_by_role IS NULL AND reviewed_at IS NULL) OR (status <> 'PENDING_APPROVAL' AND reviewed_by_user_id IS NOT NULL AND reviewed_by_role IS NOT NULL AND reviewed_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_parcel_custody_exception_requests_parcel_custody_events_app~",
                        column: x => x.approved_custody_event_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_custody_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_custody_exception_requests_parcel_incidents_incident~",
                        column: x => x.incident_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_custody_exception_requests_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_custody_exception_requests_approved_event",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                column: "approved_custody_event_id");

            migrationBuilder.CreateIndex(
                name: "idx_parcel_custody_exception_requests_operator_status",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_custody_exception_requests_trip_status",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                columns: new[] { "trip_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_parcel_custody_exception_requests_idempotency",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_custody_exception_requests_incident",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                column: "incident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_custody_exception_requests_pending_parcel_type",
                schema: "vietride_parcel",
                table: "parcel_custody_exception_requests",
                columns: new[] { "parcel_id", "incident_type" },
                unique: true,
                filter: "status = 'PENDING_APPROVAL'");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_parcel_custody_exception_requests_updated_at
                BEFORE UPDATE ON vietride_parcel.parcel_custody_exception_requests
                FOR EACH ROW EXECUTE FUNCTION vietride_parcel.trg_set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_parcel_custody_exception_requests_updated_at
                    ON vietride_parcel.parcel_custody_exception_requests;
                """);

            migrationBuilder.DropTable(
                name: "parcel_custody_exception_requests",
                schema: "vietride_parcel");
        }
    }
}
