using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingShuttleIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vietride_booking.trg_set_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN NEW.updated_at = now(); RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.CreateTable(
                name: "booking_shuttle_intents",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_address = table.Column<string>(type: "text", nullable: false),
                    pickup_latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    pickup_longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_shuttle_intents", x => x.id);
                    table.CheckConstraint("chk_booking_shuttle_intents_latitude", "pickup_latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("chk_booking_shuttle_intents_longitude", "pickup_longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_booking_shuttle_intents_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_booking_shuttle_intents_booking",
                schema: "vietride_booking",
                table: "booking_shuttle_intents",
                column: "booking_id",
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_booking_shuttle_intents_updated_at
                BEFORE UPDATE ON vietride_booking.booking_shuttle_intents
                FOR EACH ROW EXECUTE FUNCTION vietride_booking.trg_set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_shuttle_intents",
                schema: "vietride_booking");
        }
    }
}
