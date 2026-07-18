using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStationRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_station_redirects",
                schema: "vietride_booking",
                columns: table => new
                {
                    duplicate_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_station_redirects", x => x.duplicate_station_id);
                    table.CheckConstraint(
                        "chk_booking_station_redirects_not_self",
                        "duplicate_station_id <> canonical_station_id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_booking_station_redirects_canonical",
                schema: "vietride_booking",
                table: "booking_station_redirects",
                column: "canonical_station_id");

            migrationBuilder.CreateIndex(
                name: "uq_booking_station_redirects_source_event",
                schema: "vietride_booking",
                table: "booking_station_redirects",
                column: "source_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_station_redirects",
                schema: "vietride_booking");
        }
    }
}
