using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedTripReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_trips_completed_report",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "completed_at", "operator_id" },
                filter: "status = 'COMPLETED' AND completed_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_trips_completed_report",
                schema: "vietride_trip",
                table: "trips");
        }
    }
}
