using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNoShowPassengerStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "total_no_show_passengers",
                schema: "vietride_booking",
                table: "booking_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                WITH first_mark_no_show AS (
                    SELECT booking_id, MIN(occurred_at) AS occurred_at
                    FROM vietride_booking.booking_status_history
                    WHERE source = 'MARK_NO_SHOW'
                    GROUP BY booking_id
                ), legacy_booking_no_show AS (
                    SELECT
                        b.operator_id,
                        (h.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS stat_date,
                        b.trip_id,
                        COUNT(p.id)::integer AS passenger_count
                    FROM vietride_booking.bookings b
                    INNER JOIN vietride_booking.passengers p ON p.booking_id = b.id
                    INNER JOIN first_mark_no_show h ON h.booking_id = b.id
                    WHERE p.boarding_status = 'NO_SHOW'
                    GROUP BY b.id, b.operator_id, b.trip_id, h.occurred_at
                ), legacy_no_show AS (
                    SELECT operator_id, stat_date, trip_id, SUM(passenger_count)::integer AS passenger_count
                    FROM legacy_booking_no_show
                    GROUP BY operator_id, stat_date, trip_id
                )
                INSERT INTO vietride_booking.booking_stats (
                    id, operator_id, stat_date, trip_id, total_no_show_passengers, updated_at
                )
                SELECT gen_random_uuid(), operator_id, stat_date, trip_id, passenger_count, now()
                FROM legacy_no_show
                ON CONFLICT (
                    operator_id,
                    stat_date,
                    COALESCE(trip_id, '00000000-0000-0000-0000-000000000000'::uuid)
                )
                DO UPDATE SET
                    total_no_show_passengers = EXCLUDED.total_no_show_passengers,
                    updated_at = now();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "total_no_show_passengers",
                schema: "vietride_booking",
                table: "booking_stats");
        }
    }
}
