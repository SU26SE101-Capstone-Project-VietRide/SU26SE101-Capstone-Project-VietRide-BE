using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resource_reservations",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resource_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shuttle_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    start_longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    end_latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    end_longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_reservations", x => x.id);
                    table.CheckConstraint("chk_resource_reservations_end_coordinates", "(end_latitude IS NULL) = (end_longitude IS NULL)");
                    table.CheckConstraint("chk_resource_reservations_period", "planned_end_at > planned_start_at");
                    table.CheckConstraint("chk_resource_reservations_role", "resource_role IN ('DRIVER', 'ASSISTANT', 'VEHICLE')");
                    table.CheckConstraint("chk_resource_reservations_source", "num_nonnulls(trip_id, shuttle_trip_id) = 1");
                    table.CheckConstraint("chk_resource_reservations_start_coordinates", "(start_latitude IS NULL) = (start_longitude IS NULL)");
                    table.CheckConstraint("chk_resource_reservations_status", "status IN ('RESERVED', 'ACTIVE', 'RELEASED', 'CANCELLED')");
                    table.CheckConstraint("chk_resource_reservations_type", "resource_type IN ('CREW', 'VEHICLE')");
                    table.CheckConstraint("chk_resource_reservations_type_role", "(resource_type = 'VEHICLE' AND resource_role = 'VEHICLE') OR (resource_type = 'CREW' AND resource_role IN ('DRIVER', 'ASSISTANT'))");
                    table.ForeignKey(
                        name: "FK_resource_reservations_shuttle_trips_shuttle_trip_id",
                        column: x => x.shuttle_trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "shuttle_trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resource_reservations_stations_end_station_id",
                        column: x => x.end_station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resource_reservations_stations_start_station_id",
                        column: x => x.start_station_id,
                        principalSchema: "vietride_trip",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resource_reservations_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_resource_reservations_operator_status",
                schema: "vietride_trip",
                table: "resource_reservations",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_resource_reservations_resource_start",
                schema: "vietride_trip",
                table: "resource_reservations",
                columns: new[] { "resource_type", "resource_id", "planned_start_at" },
                filter: "status IN ('RESERVED', 'ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "uq_resource_reservations_shuttle_role",
                schema: "vietride_trip",
                table: "resource_reservations",
                columns: new[] { "shuttle_trip_id", "resource_role" },
                unique: true,
                filter: "shuttle_trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_resource_reservations_trip_role",
                schema: "vietride_trip",
                table: "resource_reservations",
                columns: new[] { "trip_id", "resource_role" },
                unique: true,
                filter: "trip_id IS NOT NULL");

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_trip.resource_reservations (
                    id, operator_id, resource_type, resource_role, resource_id,
                    trip_id, shuttle_trip_id, planned_start_at, planned_end_at,
                    start_station_id, end_station_id,
                    start_latitude, start_longitude, end_latitude, end_longitude,
                    status, activated_at, released_at, created_at, updated_at)
                SELECT
                    gen_random_uuid(), trip.operator_id, resource.resource_type, resource.resource_role,
                    resource.resource_id, trip.id, NULL, trip.departure_date_time, trip.estimated_arrival_time,
                    route.origin_station_id,
                    COALESCE(alternative.destination_station_id, route.destination_station_id),
                    origin.latitude, origin.longitude, destination.latitude, destination.longitude,
                    CASE WHEN trip.status = 'IN_PROGRESS' THEN 'ACTIVE' ELSE 'RESERVED' END,
                    CASE WHEN trip.status = 'IN_PROGRESS' THEN COALESCE(trip.actual_departure_time, trip.departure_date_time) END,
                    NULL, now(), now()
                FROM vietride_trip.trips AS trip
                JOIN vietride_trip.routes AS route ON route.id = trip.route_id
                LEFT JOIN vietride_trip.alternative_routes AS alternative ON alternative.id = trip.alternative_route_id
                JOIN vietride_trip.stations AS origin ON origin.id = route.origin_station_id
                JOIN vietride_trip.stations AS destination
                    ON destination.id = COALESCE(alternative.destination_station_id, route.destination_station_id)
                CROSS JOIN LATERAL (
                    VALUES
                        ('CREW', 'DRIVER', trip.driver_user_id),
                        ('CREW', 'ASSISTANT', trip.assistant_user_id),
                        ('VEHICLE', 'VEHICLE', trip.vehicle_id)
                ) AS resource(resource_type, resource_role, resource_id)
                WHERE trip.status IN ('SCHEDULED', 'BOARDING', 'IN_PROGRESS')
                  AND resource.resource_id IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_trip.resource_reservations (
                    id, operator_id, resource_type, resource_role, resource_id,
                    trip_id, shuttle_trip_id, planned_start_at, planned_end_at,
                    start_station_id, end_station_id,
                    start_latitude, start_longitude, end_latitude, end_longitude,
                    status, activated_at, released_at, created_at, updated_at)
                SELECT
                    gen_random_uuid(), shuttle.operator_id, resource.resource_type, resource.resource_role,
                    resource.resource_id, NULL, shuttle.id,
                    shuttle.scheduled_departure_time, shuttle.scheduled_end_time,
                    CASE WHEN shuttle.direction = 'OUTBOUND_FROM_STATION' THEN shuttle.station_id END,
                    CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN shuttle.station_id END,
                    CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN first_stop.pickup_lat ELSE station.latitude END,
                    CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN first_stop.pickup_lng ELSE station.longitude END,
                    CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN station.latitude ELSE last_stop.pickup_lat END,
                    CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN station.longitude ELSE last_stop.pickup_lng END,
                    CASE WHEN shuttle.status = 'IN_PROGRESS' THEN 'ACTIVE' ELSE 'RESERVED' END,
                    CASE WHEN shuttle.status = 'IN_PROGRESS' THEN COALESCE(shuttle.actual_departure_time, shuttle.scheduled_departure_time) END,
                    NULL, now(), now()
                FROM vietride_trip.shuttle_trips AS shuttle
                JOIN vietride_trip.stations AS station ON station.id = shuttle.station_id
                LEFT JOIN LATERAL (
                    SELECT passenger.pickup_lat, passenger.pickup_lng
                    FROM vietride_trip.shuttle_passengers AS passenger
                    WHERE passenger.shuttle_trip_id = shuttle.id
                    ORDER BY passenger.pickup_order ASC NULLS LAST, passenger.created_at, passenger.id
                    LIMIT 1
                ) AS first_stop ON TRUE
                LEFT JOIN LATERAL (
                    SELECT passenger.pickup_lat, passenger.pickup_lng
                    FROM vietride_trip.shuttle_passengers AS passenger
                    WHERE passenger.shuttle_trip_id = shuttle.id
                    ORDER BY passenger.pickup_order DESC NULLS LAST, passenger.created_at DESC, passenger.id DESC
                    LIMIT 1
                ) AS last_stop ON TRUE
                CROSS JOIN LATERAL (
                    VALUES
                        ('CREW', 'DRIVER', shuttle.driver_user_id),
                        ('VEHICLE', 'VEHICLE', shuttle.vehicle_id)
                ) AS resource(resource_type, resource_role, resource_id)
                WHERE shuttle.status IN ('SCHEDULED', 'IN_PROGRESS');
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.resource_reservations
                ADD CONSTRAINT ex_resource_reservations_no_overlap
                EXCLUDE USING gist (
                    resource_type WITH =,
                    resource_id WITH =,
                    tstzrange(planned_start_at, planned_end_at, '[)') WITH &&
                ) WHERE (status IN ('RESERVED', 'ACTIVE'));
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION vietride_trip.trg_set_resource_reservation_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_resource_reservations_updated_at
                BEFORE UPDATE ON vietride_trip.resource_reservations
                FOR EACH ROW EXECUTE FUNCTION vietride_trip.trg_set_resource_reservation_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_resource_reservations_updated_at
                    ON vietride_trip.resource_reservations;
                DROP FUNCTION IF EXISTS vietride_trip.trg_set_resource_reservation_updated_at();
                """);

            migrationBuilder.DropTable(
                name: "resource_reservations",
                schema: "vietride_trip");
        }
    }
}
