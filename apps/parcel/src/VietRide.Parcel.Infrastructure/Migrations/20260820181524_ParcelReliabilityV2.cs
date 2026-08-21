using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParcelReliabilityV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "claim_window_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<long>(
                name: "compensation_policy_cap_vnd_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValue: 30000000L);

            migrationBuilder.AddColumn<int>(
                name: "compensation_policy_version_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "compensation_rate_percent_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "decision_sla_business_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "declaration_accepted_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "declaration_policy_version",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "declared_value_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "no_proof_fallback_multiplier_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "payout_sla_business_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "search_sla_hours_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 72);

            migrationBuilder.CreateTable(
                name: "parcel_compensation_policies",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compensation_rate_percent = table.Column<int>(type: "integer", nullable: false),
                    max_compensation_vnd = table.Column<long>(type: "bigint", nullable: false),
                    no_proof_fallback_multiplier = table.Column<int>(type: "integer", nullable: false),
                    claim_window_days = table.Column<int>(type: "integer", nullable: false),
                    search_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    decision_sla_business_days = table.Column<int>(type: "integer", nullable: false),
                    payout_sla_business_days = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    below_default_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_compensation_policies", x => x.id);
                    table.CheckConstraint("chk_parcel_compensation_policy_cap", "max_compensation_vnd > 0");
                    table.CheckConstraint("chk_parcel_compensation_policy_rate", "compensation_rate_percent BETWEEN 1 AND 100");
                    table.CheckConstraint("chk_parcel_compensation_policy_sla", "claim_window_days > 0 AND search_sla_hours > 0 AND decision_sla_business_days > 0 AND payout_sla_business_days > 0");
                });

            migrationBuilder.CreateTable(
                name: "parcel_current_custody",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_location_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_location_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_confidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_current_custody", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_current_custody_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parcel_incidents",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leg_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    expected_location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_known_location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: true),
                    search_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    operator_process_breach = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_incidents_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_transit_legs",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    expected_origin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_origin_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    expected_destination_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    actual_origin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actual_destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_license_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_transit_legs", x => x.id);
                    table.CheckConstraint("chk_parcel_transit_legs_sequence_positive", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_parcel_transit_legs_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unidentified_parcel_packages",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    temporary_exception_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    observed_weight_kg = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    evidence_references_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matched_parcel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    matched_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidentified_parcel_packages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parcel_claims",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declared_value_vnd = table.Column<long>(type: "bigint", nullable: true),
                    proven_direct_loss_vnd = table.Column<long>(type: "bigint", nullable: true),
                    compensation_rate_percent = table.Column<int>(type: "integer", nullable: false),
                    policy_cap_vnd = table.Column<long>(type: "bigint", nullable: false),
                    cargo_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    freight_refund_vnd = table.Column<long>(type: "bigint", nullable: false),
                    total_award_vnd = table.Column<long>(type: "bigint", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    no_proof_fallback_multiplier = table.Column<int>(type: "integer", nullable: false),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payout_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_claims", x => x.id);
                    table.CheckConstraint("chk_parcel_claims_amounts", "policy_cap_vnd > 0 AND cargo_award_vnd >= 0 AND freight_refund_vnd >= 0 AND total_award_vnd >= 0");
                    table.CheckConstraint("chk_parcel_claims_rate", "compensation_rate_percent BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_parcel_claims_parcel_incidents_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_claims_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_search_tasks",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    result = table.Column<string>(type: "text", nullable: true),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_search_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_search_tasks_parcel_incidents_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_parcel_search_tasks_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_custody_events",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leg_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expected_location_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    expected_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actual_location_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    actual_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence_references_json = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_custody_events", x => x.id);
                    table.CheckConstraint("chk_parcel_custody_events_sequence_positive", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_parcel_custody_events_parcel_transit_legs_leg_id",
                        column: x => x.leg_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_transit_legs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_custody_events_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_claim_evidence",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_claim_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_claim_evidence_parcel_claims_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claim_evidence_claim_id_created_at",
                schema: "vietride_parcel",
                table: "parcel_claim_evidence",
                columns: new[] { "claim_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claims_beneficiary_user_id_created_at",
                schema: "vietride_parcel",
                table: "parcel_claims",
                columns: new[] { "beneficiary_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claims_incident_id",
                schema: "vietride_parcel",
                table: "parcel_claims",
                column: "incident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claims_operator_id_status_created_at",
                schema: "vietride_parcel",
                table: "parcel_claims",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_claims_parcel_id",
                schema: "vietride_parcel",
                table: "parcel_claims",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_compensation_policies_operator_id",
                schema: "vietride_parcel",
                table: "parcel_compensation_policies",
                column: "operator_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_current_custody_last_location_id_last_confirmed_at",
                schema: "vietride_parcel",
                table: "parcel_current_custody",
                columns: new[] { "last_location_id", "last_confirmed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_current_custody_parcel_id",
                schema: "vietride_parcel",
                table: "parcel_current_custody",
                column: "parcel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_custody_events_leg_id",
                schema: "vietride_parcel",
                table: "parcel_custody_events",
                column: "leg_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_custody_events_parcel_id_idempotency_key",
                schema: "vietride_parcel",
                table: "parcel_custody_events",
                columns: new[] { "parcel_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_custody_events_parcel_id_occurred_at_id",
                schema: "vietride_parcel",
                table: "parcel_custody_events",
                columns: new[] { "parcel_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_custody_events_trip_id_actual_location_id_occurred_at",
                schema: "vietride_parcel",
                table: "parcel_custody_events",
                columns: new[] { "trip_id", "actual_location_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_incidents_operator_id_status_created_at",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_incidents_parcel_id_status",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                columns: new[] { "parcel_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_incidents_parcel_id_type",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                columns: new[] { "parcel_id", "type" },
                unique: true,
                filter: "status NOT IN ('CLOSED', 'RESOLVED')");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_incidents_search_deadline_status",
                schema: "vietride_parcel",
                table: "parcel_incidents",
                columns: new[] { "search_deadline", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_search_tasks_assignee_id_status_deadline",
                schema: "vietride_parcel",
                table: "parcel_search_tasks",
                columns: new[] { "assignee_id", "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_search_tasks_incident_id_status",
                schema: "vietride_parcel",
                table: "parcel_search_tasks",
                columns: new[] { "incident_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_search_tasks_parcel_id",
                schema: "vietride_parcel",
                table: "parcel_search_tasks",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_transit_legs_operator_id_status",
                schema: "vietride_parcel",
                table: "parcel_transit_legs",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_transit_legs_parcel_id_sequence",
                schema: "vietride_parcel",
                table: "parcel_transit_legs",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_transit_legs_trip_id_status",
                schema: "vietride_parcel",
                table: "parcel_transit_legs",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_unidentified_parcel_packages_matched_parcel_id",
                schema: "vietride_parcel",
                table: "unidentified_parcel_packages",
                column: "matched_parcel_id",
                filter: "matched_parcel_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_unidentified_parcel_packages_operator_id_status_created_at",
                schema: "vietride_parcel",
                table: "unidentified_parcel_packages",
                columns: new[] { "operator_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_unidentified_parcel_packages_operator_id_temporary_exceptio~",
                schema: "vietride_parcel",
                table: "unidentified_parcel_packages",
                columns: new[] { "operator_id", "temporary_exception_tag" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION vietride_parcel.prevent_parcel_custody_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'parcel_custody_events is append-only';
                END;
                $function$;

                CREATE TRIGGER trg_parcel_custody_events_append_only
                BEFORE UPDATE OR DELETE ON vietride_parcel.parcel_custody_events
                FOR EACH ROW
                EXECUTE FUNCTION vietride_parcel.prevent_parcel_custody_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_parcel_custody_events_append_only
                    ON vietride_parcel.parcel_custody_events;
                DROP FUNCTION IF EXISTS vietride_parcel.prevent_parcel_custody_event_mutation();
                """);

            migrationBuilder.DropTable(
                name: "parcel_claim_evidence",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_compensation_policies",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_current_custody",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_custody_events",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_search_tasks",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "unidentified_parcel_packages",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_claims",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_transit_legs",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_incidents",
                schema: "vietride_parcel");

            migrationBuilder.DropColumn(
                name: "claim_window_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "compensation_policy_cap_vnd_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "compensation_policy_version_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "compensation_rate_percent_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "decision_sla_business_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "declaration_accepted_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "declaration_policy_version",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "declared_value_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "no_proof_fallback_multiplier_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "payout_sla_business_days_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "search_sla_hours_snapshot",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
