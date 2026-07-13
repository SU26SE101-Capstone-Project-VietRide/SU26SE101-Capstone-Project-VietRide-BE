using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VietRide.Trip.Infrastructure.Migrations;

[DbContext(typeof(TripDbContext))]
[Migration("20260713090000_AddShuttleBackend")]
public sealed class AddShuttleBackend : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE vietride_trip.shuttle_trips (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                operator_id UUID NOT NULL,
                main_trip_id UUID NOT NULL REFERENCES vietride_trip.trips (id) ON DELETE RESTRICT,
                station_id UUID NOT NULL REFERENCES vietride_trip.stations (id) ON DELETE RESTRICT,
                direction VARCHAR(30) NOT NULL,
                driver_user_id UUID NOT NULL,
                vehicle_id UUID NOT NULL REFERENCES vietride_trip.vehicles (id) ON DELETE RESTRICT,
                status VARCHAR(20) NOT NULL DEFAULT 'SCHEDULED',
                scheduled_departure_time TIMESTAMPTZ NOT NULL,
                scheduled_end_time TIMESTAMPTZ NOT NULL,
                actual_departure_time TIMESTAMPTZ NULL,
                completed_at TIMESTAMPTZ NULL,
                notes TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT chk_shuttle_trips_schedule CHECK (scheduled_end_time > scheduled_departure_time),
                CONSTRAINT chk_shuttle_trips_direction CHECK (direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')),
                CONSTRAINT chk_shuttle_trips_status CHECK (status IN ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED'))
            );
            CREATE INDEX idx_shuttle_trips_main_trip ON vietride_trip.shuttle_trips (main_trip_id);
            CREATE INDEX idx_shuttle_trips_operator_status ON vietride_trip.shuttle_trips (operator_id, status);
            CREATE INDEX idx_shuttle_trips_station_direction ON vietride_trip.shuttle_trips (station_id, direction);
            CREATE INDEX idx_shuttle_trips_driver_schedule ON vietride_trip.shuttle_trips
                (driver_user_id, scheduled_departure_time, scheduled_end_time)
                WHERE status IN ('SCHEDULED', 'IN_PROGRESS');
            CREATE INDEX idx_shuttle_trips_vehicle_schedule ON vietride_trip.shuttle_trips
                (vehicle_id, scheduled_departure_time, scheduled_end_time)
                WHERE status IN ('SCHEDULED', 'IN_PROGRESS');

            CREATE TABLE vietride_trip.shuttle_passengers (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                shuttle_trip_id UUID NULL REFERENCES vietride_trip.shuttle_trips (id) ON DELETE SET NULL,
                main_trip_id UUID NOT NULL REFERENCES vietride_trip.trips (id) ON DELETE RESTRICT,
                booking_id UUID NULL,
                ticket_id UUID NULL,
                passenger_user_id UUID NULL,
                direction VARCHAR(30) NOT NULL,
                pickup_address TEXT NOT NULL,
                pickup_lat DECIMAL(10,7) NOT NULL,
                pickup_lng DECIMAL(10,7) NOT NULL,
                scheduled_pickup_time TIMESTAMPTZ NULL,
                pickup_order INT NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'PENDING_ASSIGNMENT',
                picked_up_at TIMESTAMPTZ NULL,
                delivered_at TIMESTAMPTZ NULL,
                cancel_reason TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT chk_shuttle_passengers_direction CHECK (direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')),
                CONSTRAINT chk_shuttle_passengers_status CHECK (status IN ('PENDING_ASSIGNMENT', 'PENDING', 'PICKED_UP', 'DELIVERED', 'NO_SHOW', 'CANCELLED'))
            );
            CREATE INDEX idx_shuttle_passengers_shuttle_trip ON vietride_trip.shuttle_passengers (shuttle_trip_id)
                WHERE shuttle_trip_id IS NOT NULL;
            CREATE INDEX idx_shuttle_passengers_main_trip_status ON vietride_trip.shuttle_passengers (main_trip_id, status);
            CREATE INDEX idx_shuttle_passengers_booking ON vietride_trip.shuttle_passengers (booking_id)
                WHERE booking_id IS NOT NULL;
            CREATE UNIQUE INDEX uq_shuttle_passengers_booking_ticket ON vietride_trip.shuttle_passengers (booking_id, ticket_id)
                WHERE booking_id IS NOT NULL AND ticket_id IS NOT NULL;

            CREATE TABLE vietride_trip.shuttle_dispatch_alerts (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                main_trip_id UUID NOT NULL REFERENCES vietride_trip.trips (id) ON DELETE RESTRICT,
                operator_id UUID NOT NULL,
                alert_type VARCHAR(20) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT uq_shuttle_dispatch_alerts_trip_type UNIQUE (main_trip_id, alert_type),
                CONSTRAINT chk_shuttle_dispatch_alerts_type CHECK (alert_type IN ('WARNING_120', 'WARNING_60', 'AUTO_CUTOFF'))
            );
            CREATE INDEX idx_shuttle_dispatch_alerts_operator_created
                ON vietride_trip.shuttle_dispatch_alerts (operator_id, created_at DESC);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS vietride_trip.shuttle_dispatch_alerts;
            DROP TABLE IF EXISTS vietride_trip.shuttle_passengers;
            DROP TABLE IF EXISTS vietride_trip.shuttle_trips;
            """);
    }
}
