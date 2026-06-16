using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripsAndTripSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    CREATE TYPE trip_status AS ENUM ('SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'DISRUPTED');
                EXCEPTION WHEN duplicate_object THEN NULL; END $$;

                DO $$ BEGIN
                    CREATE TYPE trip_source AS ENUM ('MANUAL', 'AUTO_FROM_SCHEDULE', 'VEHICLE_SUBSTITUTION');
                EXCEPTION WHEN duplicate_object THEN NULL; END $$;

                DO $$ BEGIN
                    CREATE TYPE trip_seat_status AS ENUM ('AVAILABLE', 'HELD', 'BOOKED', 'UNAVAILABLE');
                EXCEPTION WHEN duplicate_object THEN NULL; END $$;

                DO $$ BEGIN
                    CREATE TYPE trip_seat_type AS ENUM ('STANDARD', 'SLEEPER_LOWER', 'SLEEPER_UPPER', 'VIP', 'DRIVER_AREA');
                EXCEPTION WHEN duplicate_object THEN NULL; END $$;

                CREATE TABLE IF NOT EXISTS vietride_trip.trips (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    operator_id UUID NOT NULL,
                    route_id UUID NOT NULL REFERENCES vietride_trip.routes (id) ON DELETE RESTRICT,
                    vehicle_id UUID NOT NULL REFERENCES vietride_trip.vehicles (id) ON DELETE RESTRICT,
                    driver_user_id UUID NOT NULL,
                    assistant_user_id UUID NULL,
                    driver_schedule_id UUID NULL REFERENCES vietride_trip.driver_schedules (id) ON DELETE SET NULL,
                    departure_date_time TIMESTAMPTZ NOT NULL,
                    estimated_arrival_time TIMESTAMPTZ NOT NULL,
                    actual_departure_time TIMESTAMPTZ NULL,
                    completed_at TIMESTAMPTZ NULL,
                    disrupted_at TIMESTAMPTZ NULL,
                    disruption_reason TEXT NULL,
                    cancelled_at TIMESTAMPTZ NULL,
                    cancelled_by_user_id UUID NULL,
                    cancel_reason TEXT NULL,
                    completed_by_user_id UUID NULL,
                    status trip_status NOT NULL DEFAULT 'SCHEDULED',
                    source trip_source NOT NULL,
                    has_substitution BOOLEAN NOT NULL DEFAULT FALSE,
                    base_fare BIGINT NOT NULL,
                    max_cargo_weight_kg DECIMAL(8,2) NULL,
                    estimated_passenger_luggage_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
                    reserved_parcel_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
                    total_loaded_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    CONSTRAINT chk_trips_base_fare_non_negative CHECK (base_fare >= 0),
                    CONSTRAINT chk_trips_cargo_counters_non_negative
                        CHECK (reserved_parcel_weight_kg >= 0 AND total_loaded_weight_kg >= 0)
                );

                CREATE TABLE IF NOT EXISTS vietride_trip.trip_seats (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    trip_id UUID NOT NULL REFERENCES vietride_trip.trips (id) ON DELETE CASCADE,
                    seat_number VARCHAR(20) NOT NULL,
                    seat_type trip_seat_type NOT NULL DEFAULT 'STANDARD',
                    status trip_seat_status NOT NULL DEFAULT 'AVAILABLE',
                    disabled_reason TEXT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );

                CREATE UNIQUE INDEX IF NOT EXISTS uq_trip_seats_trip_seat
                    ON vietride_trip.trip_seats (trip_id, seat_number);
                CREATE INDEX IF NOT EXISTS idx_trip_seats_trip_status
                    ON vietride_trip.trip_seats (trip_id, status);
                CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_driver_departure
                    ON vietride_trip.trips (driver_user_id, departure_date_time)
                    WHERE status NOT IN ('CANCELLED');
                CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_vehicle_departure
                    ON vietride_trip.trips (vehicle_id, departure_date_time)
                    WHERE status NOT IN ('CANCELLED');
                CREATE INDEX IF NOT EXISTS idx_trips_operator_status
                    ON vietride_trip.trips (operator_id, status);
                CREATE INDEX IF NOT EXISTS idx_trips_route_departure
                    ON vietride_trip.trips (route_id, departure_date_time);
                CREATE INDEX IF NOT EXISTS idx_trips_status_departure
                    ON vietride_trip.trips (status, departure_date_time);
                CREATE INDEX IF NOT EXISTS idx_trips_assistant_user_id
                    ON vietride_trip.trips (assistant_user_id) WHERE assistant_user_id IS NOT NULL;
                CREATE INDEX IF NOT EXISTS idx_trips_driver_schedule_id
                    ON vietride_trip.trips (driver_schedule_id) WHERE driver_schedule_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS vietride_trip.trip_seats;
                DROP TABLE IF EXISTS vietride_trip.trips;
                DROP TYPE IF EXISTS trip_seat_type CASCADE;
                DROP TYPE IF EXISTS trip_seat_status CASCADE;
                DROP TYPE IF EXISTS trip_source CASCADE;
                DROP TYPE IF EXISTS trip_status CASCADE;
                """);
        }
    }
}
