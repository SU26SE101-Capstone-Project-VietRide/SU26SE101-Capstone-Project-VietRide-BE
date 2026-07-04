using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_stats",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_name = table.Column<string>(type: "text", nullable: true),
                    stat_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_bookings = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_confirmed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_cancelled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_no_show = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_completed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_revenue = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    total_refunded = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    total_seats_booked = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_stats", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_booking_stats_operator_date",
                schema: "vietride_booking",
                table: "booking_stats",
                columns: new[] { "operator_id", "stat_date" },
                descending: new[] { false, true });

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX uq_booking_stats_operator_date_trip
                    ON vietride_booking.booking_stats (
                        operator_id,
                        stat_date,
                        COALESCE(trip_id, '00000000-0000-0000-0000-000000000000'::uuid)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_stats",
                schema: "vietride_booking");
        }
    }
}
