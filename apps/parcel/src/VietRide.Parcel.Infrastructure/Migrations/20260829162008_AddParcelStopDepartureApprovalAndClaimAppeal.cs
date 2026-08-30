using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelStopDepartureApprovalAndClaimAppeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_claim_appeals",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_claim_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    original_total_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revised_proven_direct_loss_vnd = table.Column<long>(type: "bigint", nullable: true),
                    revised_cargo_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    revised_freight_refund_vnd = table.Column<long>(type: "bigint", nullable: false),
                    revised_total_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    supplementary_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payout_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_claim_appeals", x => x.id);
                    table.CheckConstraint("chk_parcel_claim_appeal_awards", "original_total_award_vnd >= 0 AND revised_cargo_award_vnd >= 0 AND revised_freight_refund_vnd >= 0 AND revised_total_award_vnd >= 0 AND supplementary_award_vnd >= 0");
                    table.CheckConstraint("chk_parcel_claim_appeal_status", "status IN ('SUBMITTED', 'UNDER_REVIEW', 'UPHELD', 'ADJUSTMENT_APPROVED', 'FUNDING_PENDING', 'PAID')");
                    table.ForeignKey(
                        name: "fk_parcel_claim_appeals_parcel_claims_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_claim_appeals_parcel_incidents_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_stop_departure_approval_requests",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unresolved_parcel_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    departure_override_reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_stop_departure_approval_requests", x => x.id);
                    table.CheckConstraint("chk_parcel_stop_departure_approval_status", "status IN ('PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'CANCELLED')");
                    table.CheckConstraint("chk_parcel_stop_departure_review_audit", "(status = 'PENDING_APPROVAL' AND reviewed_by_user_id IS NULL AND reviewed_by_role IS NULL AND reviewed_at IS NULL) OR (status IN ('APPROVED', 'REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_by_role IS NOT NULL AND reviewed_at IS NOT NULL) OR (status = 'CANCELLED' AND reviewed_by_user_id IS NULL AND reviewed_by_role = 'SYSTEM' AND reviewed_at IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_claim_appeals_operator_status",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claim_appeals_incident_id",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "uq_parcel_claim_appeals_claim",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                column: "claim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_claim_appeals_idempotency",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_parcel_stop_departure_approval_operator_status",
                schema: "vietride_parcel",
                table: "parcel_stop_departure_approval_requests",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_stop_departure_approval_trip_stop",
                schema: "vietride_parcel",
                table: "parcel_stop_departure_approval_requests",
                columns: new[] { "trip_id", "stop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_parcel_stop_departure_approval_idempotency",
                schema: "vietride_parcel",
                table: "parcel_stop_departure_approval_requests",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_stop_departure_approval_pending",
                schema: "vietride_parcel",
                table: "parcel_stop_departure_approval_requests",
                columns: new[] { "trip_id", "stop_id", "status" },
                unique: true,
                filter: "status = 'PENDING_APPROVAL'");

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_parcel.parcel_claim_appeals (
                    id, claim_id, parcel_id, incident_id, operator_id, beneficiary_user_id,
                    original_claim_status, original_total_award_vnd, status, reason,
                    submitted_by_user_id, submitted_at, revised_cargo_award_vnd,
                    revised_freight_refund_vnd, revised_total_award_vnd,
                    supplementary_award_vnd, idempotency_key, created_at, updated_at, row_version)
                SELECT
                    gen_random_uuid(), id, parcel_id, incident_id, operator_id, beneficiary_user_id,
                    CASE WHEN paid_at IS NOT NULL OR payout_reference_id IS NOT NULL THEN 'PAID' ELSE 'REJECTED' END,
                    CASE WHEN paid_at IS NOT NULL OR payout_reference_id IS NOT NULL THEN total_award_vnd ELSE 0 END,
                    'SUBMITTED', appeal_reason, appealed_by_user_id, appealed_at,
                    0, 0, 0, 0, id, appealed_at, appealed_at, 0
                FROM vietride_parcel.parcel_claims
                WHERE status = 'APPEALED'
                  AND appeal_reason IS NOT NULL
                  AND appealed_by_user_id IS NOT NULL
                  AND appealed_at IS NOT NULL
                ON CONFLICT (claim_id) DO NOTHING;

                UPDATE vietride_parcel.parcel_claims
                SET status = CASE
                        WHEN paid_at IS NOT NULL OR payout_reference_id IS NOT NULL THEN 'PAID'
                        ELSE 'REJECTED'
                    END,
                    updated_at = now()
                WHERE status = 'APPEALED';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcel_claims AS claims
                SET status = 'APPEALED',
                    updated_at = now()
                FROM vietride_parcel.parcel_claim_appeals AS appeals
                WHERE appeals.claim_id = claims.id
                  AND appeals.idempotency_key = appeals.claim_id;
                """);

            migrationBuilder.DropTable(
                name: "parcel_claim_appeals",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_stop_departure_approval_requests",
                schema: "vietride_parcel");
        }
    }
}
