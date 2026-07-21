using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingTripCurrentDeparture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trip_current_departure",
                schema: "vietride_booking",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                -- UPDATE bookings from the immutable departure snapshot during rollout.
                UPDATE vietride_booking.bookings
                SET trip_current_departure = trip_snapshot_departure
                WHERE trip_current_departure IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "idx_bookings_trip_current_departure",
                schema: "vietride_booking",
                table: "bookings",
                column: "trip_current_departure",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_bookings_trip_current_departure",
                schema: "vietride_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "trip_current_departure",
                schema: "vietride_booking",
                table: "bookings");
        }
    }
}
