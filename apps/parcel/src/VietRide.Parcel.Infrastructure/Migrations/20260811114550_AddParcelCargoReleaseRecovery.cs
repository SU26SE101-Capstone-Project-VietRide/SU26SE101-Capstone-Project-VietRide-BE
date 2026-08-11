using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelCargoReleaseRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_cargo_recovery_operation_type",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_cargo_recovery_target",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_cargo_recovery_operation_type",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                sql: "operation_type IN ('TRANSFER', 'RETURN', 'RELEASE')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_cargo_recovery_target",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                sql: "(operation_type = 'TRANSFER' AND target_trip_id IS NOT NULL AND target_state = 'RESERVED')\nOR (operation_type IN ('RETURN', 'RELEASE') AND target_trip_id IS NULL AND target_state IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM vietride_parcel.parcel_cargo_recovery_operations WHERE operation_type = 'RELEASE';");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_cargo_recovery_operation_type",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcel_cargo_recovery_target",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations");

            migrationBuilder.Sql(
                "ALTER TABLE vietride_parcel.parcel_cargo_recovery_operations ALTER COLUMN actor_user_id SET NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_cargo_recovery_operation_type",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                sql: "operation_type IN ('TRANSFER', 'RETURN')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcel_cargo_recovery_target",
                schema: "vietride_parcel",
                table: "parcel_cargo_recovery_operations",
                sql: "(operation_type = 'TRANSFER' AND target_trip_id IS NOT NULL AND target_state = 'RESERVED')\r\nOR (operation_type = 'RETURN' AND target_trip_id IS NULL AND target_state IS NULL)");
        }
    }
}
