using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandShuttleDistance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_shuttle_passengers_booking_ticket",
                schema: "vietride_trip",
                table: "shuttle_passengers");

            migrationBuilder.AddColumn<int>(
                name: "road_distance_meters",
                schema: "vietride_trip",
                table: "shuttle_passengers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_shuttle_passengers_road_distance",
                schema: "vietride_trip",
                table: "shuttle_passengers",
                sql: "road_distance_meters IS NULL OR road_distance_meters >= 0");

            migrationBuilder.CreateIndex(
                name: "uq_shuttle_passengers_booking_ticket_direction",
                schema: "vietride_trip",
                table: "shuttle_passengers",
                columns: new[] { "booking_id", "ticket_id", "direction" },
                unique: true,
                filter: "booking_id IS NOT NULL AND ticket_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A two-way manifest cannot be represented by the legacy one-row-per-ticket index.
            // Abort before any destructive operation so the transaction preserves every row.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_trip.shuttle_passengers
                        WHERE booking_id IS NOT NULL AND ticket_id IS NOT NULL
                        GROUP BY booking_id, ticket_id
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back shuttle direction migration while two-way manifests exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "uq_shuttle_passengers_booking_ticket_direction",
                schema: "vietride_trip",
                table: "shuttle_passengers");

            migrationBuilder.DropCheckConstraint(
                name: "chk_shuttle_passengers_road_distance",
                schema: "vietride_trip",
                table: "shuttle_passengers");

            migrationBuilder.DropColumn(
                name: "road_distance_meters",
                schema: "vietride_trip",
                table: "shuttle_passengers");

            migrationBuilder.CreateIndex(
                name: "uq_shuttle_passengers_booking_ticket",
                schema: "vietride_trip",
                table: "shuttle_passengers",
                columns: new[] { "booking_id", "ticket_id" },
                unique: true,
                filter: "booking_id IS NOT NULL AND ticket_id IS NOT NULL");
        }
    }
}
