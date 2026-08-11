using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentStartBlockedAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_shuttle_dispatch_alerts_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts");

            migrationBuilder.AlterColumn<string>(
                name: "alert_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "chk_shuttle_dispatch_alerts_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts",
                sql: "alert_type IN ('WARNING_120', 'WARNING_60', 'AUTO_CUTOFF', 'ASSIGNMENT_START_BLOCKED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_shuttle_dispatch_alerts_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts");

            migrationBuilder.Sql(
                "DELETE FROM vietride_trip.shuttle_dispatch_alerts " +
                "WHERE alert_type = 'ASSIGNMENT_START_BLOCKED';");

            migrationBuilder.AlterColumn<string>(
                name: "alert_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddCheckConstraint(
                name: "chk_shuttle_dispatch_alerts_type",
                schema: "vietride_trip",
                table: "shuttle_dispatch_alerts",
                sql: "alert_type IN ('WARNING_120', 'WARNING_60', 'AUTO_CUTOFF')");
        }
    }
}
