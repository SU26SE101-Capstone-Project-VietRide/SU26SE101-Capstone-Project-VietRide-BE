using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripSeatBookingOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This release intentionally targets reset/reseed local, dev, and demo databases.
            // Do not apply over legacy BOOKED rows without a separately approved production backfill.
            migrationBuilder.AddColumn<Guid>(
                name: "booking_id",
                schema: "vietride_trip",
                table: "trip_seats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_trip_seats_trip_booking",
                schema: "vietride_trip",
                table: "trip_seats",
                columns: new[] { "trip_id", "booking_id" },
                filter: "booking_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trip_seats_booking_owner",
                schema: "vietride_trip",
                table: "trip_seats",
                sql: "(status = 'BOOKED' AND booking_id IS NOT NULL) OR (status <> 'BOOKED' AND booking_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_trip_seats_trip_booking",
                schema: "vietride_trip",
                table: "trip_seats");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trip_seats_booking_owner",
                schema: "vietride_trip",
                table: "trip_seats");

            migrationBuilder.DropColumn(
                name: "booking_id",
                schema: "vietride_trip",
                table: "trip_seats");
        }
    }
}
