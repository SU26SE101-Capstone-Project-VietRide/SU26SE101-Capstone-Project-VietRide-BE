using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverScheduleBaseFare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "base_fare",
                schema: "vietride_trip",
                table: "driver_schedules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_driver_schedules_base_fare_non_negative",
                schema: "vietride_trip",
                table: "driver_schedules",
                sql: "base_fare IS NULL OR base_fare >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_driver_schedules_base_fare_non_negative",
                schema: "vietride_trip",
                table: "driver_schedules");

            migrationBuilder.DropColumn(
                name: "base_fare",
                schema: "vietride_trip",
                table: "driver_schedules");
        }
    }
}
