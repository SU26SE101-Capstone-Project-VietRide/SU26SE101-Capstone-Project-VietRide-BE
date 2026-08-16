using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitBookingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vietride_booking");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";");

            // Enum types — must exist before any table that references them.
            migrationBuilder.Sql("CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');");
            migrationBuilder.Sql("CREATE TYPE public.booking_status AS ENUM ('PENDING_PAYMENT', 'CONFIRMED', 'COMPLETED', 'EXPIRED', 'CANCELLED', 'NO_SHOW', 'PARTIAL_NO_SHOW', 'REFUNDED', 'DISRUPTED');");
            migrationBuilder.Sql("CREATE TYPE booking_cancellation_reason AS ENUM ('USER_INITIATED', 'OPERATOR_CANCELLED_TRIP', 'OPERATOR_DISRUPTED_IN_PROGRESS', 'SCHEDULE_CHANGED', 'ROUTE_CHANGED_REFUSED', 'VEHICLE_SUBSTITUTION_DOWNGRADE', 'VEHICLE_SUBSTITUTION_NO_SEAT', 'STOP_DISABLED_REFUSED');");
            migrationBuilder.Sql("CREATE TYPE trip_direction AS ENUM ('OUTBOUND', 'RETURN');");
            migrationBuilder.Sql("CREATE TYPE passenger_boarding_status AS ENUM ('PENDING', 'BOARDED', 'NO_SHOW');");
            migrationBuilder.Sql("CREATE TYPE booking_pending_action_reason AS ENUM ('ROUTE_CHANGE', 'SEAT_DOWNGRADE', 'SCHEDULE_CHANGE', 'PENDING_SEAT_ASSIGNMENT', 'STOP_DISABLED');");
            migrationBuilder.Sql("CREATE TYPE booking_pending_action_severity AS ENUM ('MEDIUM', 'MAJOR');");
            migrationBuilder.Sql("CREATE TYPE booking_pending_action_resolved AS ENUM ('ACCEPTED', 'REJECTED', 'AUTO_FALLBACK_DESTINATION', 'AUTO_CANCELLED_NO_SEAT', 'OPERATOR_RESOLVED', 'SUPERSEDED');");

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    passenger_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pickup_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dropoff_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dropoff_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_fare = table.Column<long>(type: "bigint", nullable: false),
                    discount_amount = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    total_amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "booking_status", nullable: false, defaultValueSql: "'PENDING_PAYMENT'"),
                    cancellation_reason = table.Column<string>(type: "booking_cancellation_reason", nullable: true),
                    refund_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    booking_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_direction = table.Column<string>(type: "trip_direction", nullable: true),
                    trip_snapshot_origin_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    trip_snapshot_dest_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    trip_snapshot_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trip_snapshot_route_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("chk_bookings_amounts_non_negative", "base_fare >= 0 AND discount_amount >= 0 AND total_amount >= 0");
                    table.CheckConstraint("chk_bookings_dropoff_at_most_one", "(dropoff_station_id IS NOT NULL)::INT + (dropoff_stop_id IS NOT NULL)::INT <= 1");
                    table.CheckConstraint("chk_bookings_pickup_exactly_one", "(pickup_station_id IS NOT NULL)::INT + (pickup_stop_id IS NOT NULL)::INT = 1");
                    table.CheckConstraint("chk_bookings_total_le_base", "total_amount <= base_fare");
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "outbox_event_status", nullable: false, defaultValueSql: "'PENDING'"),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_pending_actions",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "booking_pending_action_reason", nullable: false),
                    severity = table.Column<string>(type: "booking_pending_action_severity", nullable: true),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_action = table.Column<string>(type: "booking_pending_action_resolved", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_pending_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_pending_actions_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "passengers",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    boarding_status = table.Column<string>(type: "passenger_boarding_status", nullable: false, defaultValueSql: "'PENDING'"),
                    boarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    boarded_at_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passengers", x => x.id);
                    table.ForeignKey(
                        name: "fk_passengers_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_booking_pending_actions_deadline_unresolved",
                schema: "vietride_booking",
                table: "booking_pending_actions",
                column: "deadline",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_booking_pending_actions_reason",
                schema: "vietride_booking",
                table: "booking_pending_actions",
                column: "reason");

            migrationBuilder.CreateIndex(
                name: "uq_booking_pending_actions_active_per_booking",
                schema: "vietride_booking",
                table: "booking_pending_actions",
                column: "booking_id",
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_booking_group_id",
                schema: "vietride_booking",
                table: "bookings",
                column: "booking_group_id",
                filter: "booking_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_operator_id_status",
                schema: "vietride_booking",
                table: "bookings",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_bookings_passenger_user_id_created_at",
                schema: "vietride_booking",
                table: "bookings",
                columns: new[] { "passenger_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_bookings_status_created_at",
                schema: "vietride_booking",
                table: "bookings",
                columns: new[] { "status", "created_at" },
                filter: "status IN ('PENDING_PAYMENT', 'CONFIRMED')");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_trip_id_status",
                schema: "vietride_booking",
                table: "bookings",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_bookings_trip_snapshot_departure",
                schema: "vietride_booking",
                table: "bookings",
                column: "trip_snapshot_departure");

            migrationBuilder.CreateIndex(
                name: "uq_bookings_booking_code",
                schema: "vietride_booking",
                table: "bookings",
                column: "booking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_events_status_created",
                schema: "vietride_booking",
                table: "outbox_events",
                columns: new[] { "status", "created_at" },
                filter: "status IN ('PENDING', 'PUBLISHING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "idx_passengers_boarding_status",
                schema: "vietride_booking",
                table: "passengers",
                columns: new[] { "booking_id", "boarding_status" });

            migrationBuilder.CreateIndex(
                name: "uq_passengers_booking_seat",
                schema: "vietride_booking",
                table: "passengers",
                columns: new[] { "booking_id", "seat_number" },
                unique: true);

            // Trigger function + trigger: enforce max 5 passengers per booking at DB layer.
            // Per db-schema/booking/schema.sql lines 346-359.
            // NOTE: the trigger body below uses schema-qualified "vietride_booking.passengers"
            // rather than the unqualified "passengers" in db-schema/booking/schema.sql line 349.
            // This is intentional: the trigger function runs in the default search_path which may
            // not include vietride_booking, so explicit qualification guarantees correct resolution
            // regardless of the session search_path.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION trg_check_passenger_max_per_booking()
RETURNS TRIGGER AS $$
BEGIN
    IF (SELECT COUNT(*) FROM vietride_booking.passengers WHERE booking_id = NEW.booking_id) >= 5 THEN
        RAISE EXCEPTION 'Booking % already has 5 passengers (max). v6 Section 6.1 hard limit.', NEW.booking_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_passengers_max_5_per_booking
    BEFORE INSERT ON vietride_booking.passengers
    FOR EACH ROW EXECUTE FUNCTION trg_check_passenger_max_per_booking();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop triggers before tables
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_passengers_max_5_per_booking ON vietride_booking.passengers;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_check_passenger_max_per_booking();");

            migrationBuilder.DropTable(
                name: "booking_pending_actions",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "passengers",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "bookings",
                schema: "vietride_booking");

            // Drop enum types (after tables that reference them are dropped)
            migrationBuilder.Sql("DROP TYPE IF EXISTS booking_pending_action_resolved;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS booking_pending_action_severity;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS booking_pending_action_reason;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS passenger_boarding_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS trip_direction;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS booking_cancellation_reason;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS public.booking_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS outbox_event_status;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"pgcrypto\";");
        }
    }
}
