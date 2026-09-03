using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelClaimProofAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "proof_status",
                schema: "vietride_parcel",
                table: "parcel_claims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_status",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_parcel_claim_evidence_claim_id_id",
                schema: "vietride_parcel",
                table: "parcel_claim_evidence",
                columns: new[] { "claim_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_parcel_claim_appeals_id_claim_id",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                columns: new[] { "id", "claim_id" });

            migrationBuilder.CreateTable(
                name: "parcel_claim_appeal_decision_evidence",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    appeal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_claim_appeal_decision_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_claim_appeal_decision_evidence_appeal",
                        columns: x => new { x.appeal_id, x.claim_id },
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claim_appeals",
                        principalColumns: new[] { "id", "claim_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_claim_appeal_decision_evidence_claim_evidence",
                        columns: x => new { x.claim_id, x.evidence_id },
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claim_evidence",
                        principalColumns: new[] { "claim_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_claim_decision_evidence",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_claim_decision_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_claim_decision_evidence_claim",
                        column: x => x.claim_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parcel_claim_decision_evidence_claim_evidence",
                        columns: x => new { x.claim_id, x.evidence_id },
                        principalSchema: "vietride_parcel",
                        principalTable: "parcel_claim_evidence",
                        principalColumns: new[] { "claim_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_claims_proof_status",
                schema: "vietride_parcel",
                table: "parcel_claims",
                sql: "proof_status IS NULL OR proof_status IN ('VERIFIED', 'UNVERIFIED', 'NO_PROOF')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_claim_appeal_proof_status",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals",
                sql: "proof_status IS NULL OR proof_status IN ('VERIFIED', 'UNVERIFIED', 'NO_PROOF')");

            migrationBuilder.CreateIndex(
                name: "idx_parcel_claim_appeal_decision_evidence_appeal_claim",
                schema: "vietride_parcel",
                table: "parcel_claim_appeal_decision_evidence",
                columns: new[] { "appeal_id", "claim_id" });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_claim_appeal_decision_evidence_claim_evidence",
                schema: "vietride_parcel",
                table: "parcel_claim_appeal_decision_evidence",
                columns: new[] { "claim_id", "evidence_id" });

            migrationBuilder.CreateIndex(
                name: "uq_parcel_claim_appeal_decision_evidence",
                schema: "vietride_parcel",
                table: "parcel_claim_appeal_decision_evidence",
                columns: new[] { "appeal_id", "evidence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_claim_decision_evidence",
                schema: "vietride_parcel",
                table: "parcel_claim_decision_evidence",
                columns: new[] { "claim_id", "evidence_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION vietride_parcel.reject_parcel_claim_decision_evidence_mutation()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'parcel claim decision evidence is append-only'
                        USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_parcel_claim_decision_evidence_immutable
                    BEFORE UPDATE OR DELETE ON vietride_parcel.parcel_claim_decision_evidence
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.reject_parcel_claim_decision_evidence_mutation();

                CREATE TRIGGER trg_parcel_claim_appeal_decision_evidence_immutable
                    BEFORE UPDATE OR DELETE ON vietride_parcel.parcel_claim_appeal_decision_evidence
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.reject_parcel_claim_decision_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_claim_appeal_decision_evidence",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_claim_decision_evidence",
                schema: "vietride_parcel");

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS vietride_parcel.reject_parcel_claim_decision_evidence_mutation();");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_claims_proof_status",
                schema: "vietride_parcel",
                table: "parcel_claims");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_parcel_claim_evidence_claim_id_id",
                schema: "vietride_parcel",
                table: "parcel_claim_evidence");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_parcel_claim_appeals_id_claim_id",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_claim_appeal_proof_status",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals");

            migrationBuilder.DropColumn(
                name: "proof_status",
                schema: "vietride_parcel",
                table: "parcel_claims");

            migrationBuilder.DropColumn(
                name: "proof_status",
                schema: "vietride_parcel",
                table: "parcel_claim_appeals");
        }
    }
}
