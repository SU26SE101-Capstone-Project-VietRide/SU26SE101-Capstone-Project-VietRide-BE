using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedBookingReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_bookings_completed_report",
                schema: "vietride_booking",
                table: "bookings",
                columns: new[] { "completed_at", "operator_id" },
                filter: "status = 'COMPLETED' AND completed_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_bookings_completed_report",
                schema: "vietride_booking",
                table: "bookings");
        }
    }
}
