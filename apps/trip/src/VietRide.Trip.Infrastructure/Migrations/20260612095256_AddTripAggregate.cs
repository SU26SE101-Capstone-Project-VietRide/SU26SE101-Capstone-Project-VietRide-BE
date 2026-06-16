using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");

            migrationBuilder.CreateTable(
                name: "trip_generation_skip_logs",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skipped_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "vietride_trip.trip_generation_skip_reason", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_generation_skip_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_trip_generation_skip_logs_driver_schedules_driver_schedule_~",
                        column: x => x.driver_schedule_id,
                        principalSchema: "vietride_trip",
                        principalTable: "driver_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    driver_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    departure_date_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estimated_arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_departure_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disrupted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disruption_reason = table.Column<string>(type: "text", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "vietride_trip.trip_status", nullable: false, defaultValue: "SCHEDULED"),
                    source = table.Column<string>(type: "vietride_trip.trip_source", nullable: false, comment: "VEHICLE_SUBSTITUTION: created by 6.12 flow, exempt from maxTripsPerMonth counter check."),
                    has_substitution = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false, comment: "Set true when Trip_old triggers Vehicle Substitution (6.12). Reporting field."),
                    base_fare = table.Column<long>(type: "bigint", nullable: false),
                    max_cargo_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    estimated_passenger_luggage_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: false, defaultValue: 0m, comment: "Snapshot at Trip create from VehicleType.estimatedPassengerLuggageKgPerSeat ?? Operator.luggagePolicy ?? 10 kg/seat × totalSeats."),
                    reserved_parcel_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: false, defaultValue: 0m),
                    total_loaded_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => x.id);
                    table.CheckConstraint("chk_trips_base_fare_non_negative", "base_fare >= 0");
                    table.CheckConstraint("chk_trips_cargo_counters_non_negative", "reserved_parcel_weight_kg >= 0 AND total_loaded_weight_kg >= 0");
                    table.ForeignKey(
                        name: "FK_trips_driver_schedules_driver_schedule_id",
                        column: x => x.driver_schedule_id,
                        principalSchema: "vietride_trip",
                        principalTable: "driver_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_trips_routes_route_id",
                        column: x => x.route_id,
                        principalSchema: "vietride_trip",
                        principalTable: "routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trips_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "vietride_trip",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_seats",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seat_type = table.Column<string>(type: "vietride_trip.trip_seat_type", nullable: false, defaultValue: "STANDARD"),
                    status = table.Column<string>(type: "vietride_trip.trip_seat_status", nullable: false, defaultValue: "AVAILABLE"),
                    disabled_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_seats", x => x.id);
                    table.ForeignKey(
                        name: "FK_trip_seats_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_stop_fares",
                schema: "vietride_trip",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fare_from_this_stop = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_stop_fares", x => new { x.trip_id, x.stop_id });
                    table.CheckConstraint("chk_trip_stop_fares_fare_non_negative", "fare_from_this_stop >= 0");
                    table.ForeignKey(
                        name: "FK_trip_stop_fares_stops_stop_id",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_stop_fares_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_stops",
                schema: "vietride_trip",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    estimated_arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Static baseline. NEVER updated after Trip generate. Dynamic ETA lives in Redis only."),
                    actual_arrival_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "vietride_trip.trip_stop_status", nullable: false, defaultValue: "PENDING"),
                    allow_pickup = table.Column<bool>(type: "boolean", nullable: false),
                    allow_dropoff = table.Column<bool>(type: "boolean", nullable: false),
                    distance_from_origin_km = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_stops", x => new { x.trip_id, x.stop_id });
                    table.ForeignKey(
                        name: "FK_trip_stops_stops_stop_id",
                        column: x => x.stop_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_stops_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_trip_gen_skip_logs_operator_date",
                schema: "vietride_trip",
                table: "trip_generation_skip_logs",
                columns: new[] { "operator_id", "skipped_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_trip_gen_skip_logs_schedule",
                schema: "vietride_trip",
                table: "trip_generation_skip_logs",
                columns: new[] { "driver_schedule_id", "skipped_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_trip_seats_trip_status",
                schema: "vietride_trip",
                table: "trip_seats",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_trip_seats_trip_seat",
                schema: "vietride_trip",
                table: "trip_seats",
                columns: new[] { "trip_id", "seat_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_trip_stops_estimated_arrival",
                schema: "vietride_trip",
                table: "trip_stops",
                column: "estimated_arrival_time",
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "idx_trip_stops_trip_status",
                schema: "vietride_trip",
                table: "trip_stops",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_trip_stops_trip_order",
                schema: "vietride_trip",
                table: "trip_stops",
                columns: new[] { "trip_id", "order_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_trips_assistant_user_id",
                schema: "vietride_trip",
                table: "trips",
                column: "assistant_user_id",
                filter: "assistant_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_trips_driver_schedule_id",
                schema: "vietride_trip",
                table: "trips",
                column: "driver_schedule_id",
                filter: "driver_schedule_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_trips_operator_status",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_trips_route_departure",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "route_id", "departure_date_time" });

            migrationBuilder.CreateIndex(
                name: "idx_trips_status_departure",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "status", "departure_date_time" });

            migrationBuilder.CreateIndex(
                name: "uq_trips_driver_departure",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "driver_user_id", "departure_date_time" },
                unique: true,
                filter: "status NOT IN ('CANCELLED')");

            migrationBuilder.CreateIndex(
                name: "uq_trips_vehicle_departure",
                schema: "vietride_trip",
                table: "trips",
                columns: new[] { "vehicle_id", "departure_date_time" },
                unique: true,
                filter: "status NOT IN ('CANCELLED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_generation_skip_logs",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "trip_seats",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "trip_stop_fares",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "trip_stops",
                schema: "vietride_trip");

            migrationBuilder.DropTable(
                name: "trips",
                schema: "vietride_trip");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED");
        }
    }
}
