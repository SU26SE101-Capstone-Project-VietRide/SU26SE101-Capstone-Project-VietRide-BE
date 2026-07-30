using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelCargoRecoveryOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_cargo_recovery_operations",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    refund_amount_vnd = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    refund_due_vnd = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    source_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_status_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_cargo_recovery_operations", x => x.id);
                    table.CheckConstraint("chk_parcel_cargo_recovery_amounts", "refund_amount_vnd >= 0 AND refund_due_vnd >= 0");
                    table.CheckConstraint("chk_parcel_cargo_recovery_completion", "(status = 'PENDING' AND completed_at IS NULL AND failure_code IS NULL)\r\nOR (status = 'COMPLETED' AND completed_at IS NOT NULL AND failure_code IS NULL)\r\nOR (status = 'FAILED' AND completed_at IS NOT NULL AND failure_code IS NOT NULL)");
                    table.CheckConstraint("chk_parcel_cargo_recovery_operation_type", "operation_type IN ('TRANSFER', 'RETURN')");
                    table.CheckConstraint("chk_parcel_cargo_recovery_status", "status IN ('PENDING', 'COMPLETED', 'FAILED')");
                    table.CheckConstraint("chk_parcel_cargo_recovery_target", "(operation_type = 'TRANSFER' AND target_trip_id IS NOT NULL AND target_state = 'RESERVED')\r\nOR (operation_type = 'RETURN' AND target_trip_id IS NULL AND target_state IS NULL)");
                    table.ForeignKey(
                        name: "fk_parcel_cargo_recovery_operations_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_cargo_recovery_operations_stale",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                columns: new[] { "claimed_at", "id" },
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "uq_parcel_cargo_recovery_operations_active_parcel",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                column: "parcel_id",
                unique: true,
                filter: "status = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_cargo_recovery_operations",
                schema: "vietride_parcel");
        }
    }
}
