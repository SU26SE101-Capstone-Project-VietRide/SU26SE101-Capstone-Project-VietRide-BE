using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripSeatLayoutSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonElement>(
                name: "seat_layout_snapshot_json",
                schema: "vietride_trip",
                table: "trips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE vietride_trip.trips AS trips
                SET seat_layout_snapshot_json = vehicles.seat_layout_json
                FROM vietride_trip.vehicles AS vehicles
                WHERE vehicles.id = trips.vehicle_id
                  AND trips.seat_layout_snapshot_json IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE vietride_trip.trips
                ALTER COLUMN seat_layout_snapshot_json SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seat_layout_snapshot_json",
                schema: "vietride_trip",
                table: "trips");
        }
    }
}
