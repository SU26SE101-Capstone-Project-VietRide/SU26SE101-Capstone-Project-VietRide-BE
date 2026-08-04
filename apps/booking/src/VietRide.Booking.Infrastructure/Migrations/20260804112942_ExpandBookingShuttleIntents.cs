using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandBookingShuttleIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_booking_shuttle_intents_booking",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.AddColumn<string>(
                name: "direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "INBOUND_TO_STATION");

            migrationBuilder.AddColumn<int>(
                name: "road_distance_meters",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_booking_shuttle_intents_booking_direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                columns: new[] { "booking_id", "direction" },
                unique: true,
                filter: "is_active = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "chk_booking_shuttle_intents_direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                sql: "direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_booking_shuttle_intents_road_distance",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                sql: "road_distance_meters IS NULL OR road_distance_meters >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The legacy schema allows one shuttle intent per Booking only. Refuse an unsafe
            // rollback when a valid two-way Booking would lose one direction; the migration
            // transaction then preserves all rows and the operator can resolve data explicitly.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_booking.booking_shuttle_intents
                        GROUP BY booking_id
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back shuttle direction migration while two-way intents exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "uq_booking_shuttle_intents_booking_direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.DropCheckConstraint(
                name: "chk_booking_shuttle_intents_direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.DropCheckConstraint(
                name: "chk_booking_shuttle_intents_road_distance",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.DropColumn(
                name: "road_distance_meters",
                schema: "vietride_booking",
                table: "booking_shuttle_intents");

            migrationBuilder.CreateIndex(
                name: "uq_booking_shuttle_intents_booking",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                column: "booking_id",
                unique: true);
        }
    }
}
