-- =============================================================================
-- VietRide :: Trip-Route-Vehicle Service :: PostgreSQL 16 schema
-- Database: vietride_trip
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "unaccent";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE EXTENSION IF NOT EXISTS "btree_gist";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE trip_status AS ENUM (
    'SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'DISRUPTED'
);

CREATE TYPE trip_source AS ENUM (
    'MANUAL', 'AUTO_FROM_SCHEDULE', 'VEHICLE_SUBSTITUTION'
);

CREATE TYPE trip_seat_status AS ENUM (
    'AVAILABLE', 'HELD', 'BOOKED', 'UNAVAILABLE'
);

CREATE TYPE trip_seat_type AS ENUM (
    'STANDARD', 'SLEEPER_LOWER', 'SLEEPER_UPPER', 'VIP', 'DRIVER_AREA'
);

CREATE TYPE trip_stop_status AS ENUM ('PENDING', 'ARRIVED', 'SKIPPED');

CREATE TYPE trip_stop_fare_source AS ENUM (
    'TEMPLATE_SNAPSHOT', 'MANUAL_OVERRIDE'
);

CREATE TYPE vehicle_status AS ENUM ('ACTIVE', 'MAINTENANCE', 'OFF_DUTY', 'RETIRED');

CREATE TYPE trip_generation_skip_reason AS ENUM (
    'SUBSCRIPTION_LIMIT_EXCEEDED', 'VEHICLE_CONFLICT', 'DRIVER_CONFLICT', 'OTHER'
);

CREATE TYPE incident_category AS ENUM (
    'TRAFFIC_JAM', 'VEHICLE_BREAKDOWN', 'ACCIDENT', 'WEATHER', 'OTHER'
);

CREATE TYPE outbox_event_status AS ENUM (
    'PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED'
);

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- stations (canonical platform-level — no operatorId)
-- -----------------------------------------------------------------------------
CREATE TABLE locations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code VARCHAR(20) NOT NULL,
    name VARCHAR(100) NOT NULL,
    type VARCHAR(20) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_locations_type CHECK (type IN ('PROVINCE', 'MUNICIPALITY')),
    CONSTRAINT chk_locations_sort_order_non_negative CHECK (sort_order >= 0)
);

CREATE UNIQUE INDEX uq_locations_code ON locations (code);
CREATE INDEX idx_locations_active_sort ON locations (is_active, sort_order, name);

COMMENT ON TABLE locations IS
    'Admin-managed location catalog for FE trip search/cache. Stations and Stops point here via nullable location_id.';

-- -----------------------------------------------------------------------------
-- stations (canonical platform-level)
-- -----------------------------------------------------------------------------
CREATE TABLE stations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    slug VARCHAR(100) NOT NULL,
    address_street VARCHAR(500) NULL,
    city VARCHAR(100) NOT NULL,
    province VARCHAR(100) NOT NULL,
    location_id UUID NULL REFERENCES locations (id) ON DELETE SET NULL,
    latitude DECIMAL(10,7) NULL,
    longitude DECIMAL(10,7) NULL,
    contact_phone VARCHAR(20) NULL,
    contact_email VARCHAR(255) NULL,
    -- {"mon":"06:00-22:00",...} local ICT
    operating_hours JSONB NULL,
    -- e.g. ["waiting_room","parking","ticket_counter"]
    facilities JSONB NULL,
    supports_shuttle BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    deleted_at TIMESTAMPTZ NULL,
    merged_into_station_id UUID NULL REFERENCES stations (id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_stations_no_self_merge
        CHECK (merged_into_station_id IS NULL OR merged_into_station_id <> id)
);

CREATE UNIQUE INDEX uq_stations_slug ON stations (slug) WHERE deleted_at IS NULL;
CREATE INDEX idx_stations_city_province ON stations (city, province) WHERE is_active = TRUE;
CREATE INDEX idx_stations_location_id ON stations (location_id)
    WHERE location_id IS NOT NULL AND is_active = TRUE;
CREATE INDEX idx_stations_supports_shuttle ON stations (supports_shuttle) WHERE is_active = TRUE;
CREATE INDEX idx_stations_merged_into ON stations (merged_into_station_id)
    WHERE merged_into_station_id IS NOT NULL;
CREATE INDEX idx_stations_name_trgm ON stations USING gin (name gin_trgm_ops)
    WHERE FALSE; -- placeholder: enable with pg_trgm if fuzzy autocomplete needed

COMMENT ON TABLE stations IS
    'Canonical platform-level bến xe. KHÔNG có operatorId. OperatorStation maps which operators serve a Station.';
COMMENT ON COLUMN stations.supports_shuttle IS
    'Per-Station flag toggled by Operator or SYSTEM_ADMIN. Only true Stations support shuttle service. Stops never have shuttle.';

-- -----------------------------------------------------------------------------
-- operator_stations (mapping: nhà xe ↔ bến)
-- -----------------------------------------------------------------------------
CREATE TABLE operator_stations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,   -- logical FK → identity.operators.id
    station_id UUID NOT NULL REFERENCES stations (id) ON DELETE RESTRICT,
    display_name_override VARCHAR(255) NULL,
    counter_location VARCHAR(255) NULL,
    contact_phone VARCHAR(20) NULL,
    instructions TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_operator_stations_operator_station
    ON operator_stations (operator_id, station_id);
CREATE INDEX idx_operator_stations_operator_id ON operator_stations (operator_id) WHERE is_active = TRUE;
CREATE INDEX idx_operator_stations_station_id ON operator_stations (station_id) WHERE is_active = TRUE;

-- -----------------------------------------------------------------------------
-- stops (operator-owned waypoints — created via Google Places)
-- -----------------------------------------------------------------------------
CREATE TABLE stops (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    name VARCHAR(255) NOT NULL,
    description TEXT NULL,
    latitude DECIMAL(10,7) NOT NULL,
    longitude DECIMAL(10,7) NOT NULL,
    location_id UUID NULL REFERENCES locations (id) ON DELETE SET NULL,
    address VARCHAR(500) NULL,
    google_place_id VARCHAR(255) NULL,
    shared_suggestion BOOLEAN NOT NULL DEFAULT FALSE,
    replaced_by_stop_id UUID NULL REFERENCES stops (id) ON DELETE SET NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    deleted_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_stops_no_self_replacement CHECK (replaced_by_stop_id IS NULL OR replaced_by_stop_id <> id)
);

CREATE INDEX idx_stops_operator_id ON stops (operator_id) WHERE is_active = TRUE;
CREATE INDEX idx_stops_location_id ON stops (location_id)
    WHERE location_id IS NOT NULL AND is_active = TRUE;
CREATE INDEX idx_stops_replaced_by ON stops (replaced_by_stop_id) WHERE replaced_by_stop_id IS NOT NULL;
CREATE INDEX idx_stops_shared_suggestion ON stops (shared_suggestion) WHERE shared_suggestion = TRUE AND is_active = TRUE;

COMMENT ON COLUMN stops.replaced_by_stop_id IS
    'Self-FK. When operator disables a Stop, may link to replacement. Cycle prevention enforced app-layer.';

-- -----------------------------------------------------------------------------
-- routes
-- -----------------------------------------------------------------------------
CREATE TABLE routes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    name VARCHAR(255) NOT NULL,
    origin_station_id UUID NOT NULL REFERENCES stations (id) ON DELETE RESTRICT,
    destination_station_id UUID NOT NULL REFERENCES stations (id) ON DELETE RESTRICT,
    return_route_id UUID NULL REFERENCES routes (id) ON DELETE SET NULL,
    base_fare BIGINT NOT NULL,
    total_distance_km DECIMAL(8,2) NULL,
    estimated_duration_minutes INT NULL,
    path_polyline TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    deleted_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_routes_base_fare_non_negative CHECK (base_fare >= 0),
    CONSTRAINT chk_routes_origin_dest_different CHECK (origin_station_id <> destination_station_id)
);

CREATE INDEX idx_routes_operator_id ON routes (operator_id) WHERE is_active = TRUE;
CREATE INDEX idx_routes_origin_destination ON routes (origin_station_id, destination_station_id)
    WHERE is_active = TRUE;
CREATE INDEX idx_routes_return_route_id ON routes (return_route_id) WHERE return_route_id IS NOT NULL;

COMMENT ON COLUMN routes.return_route_id IS
    'Self-FK pointing to the reverse-direction Route. Used by DriverSchedule round-trip UX pairing.';
COMMENT ON COLUMN routes.path_polyline IS
    'Google encoded polyline, precision 5. Nullable until operator confirms route geometry.';

-- -----------------------------------------------------------------------------
-- route_stops (junction: only intermediate stops; not origin/destination Station)
-- -----------------------------------------------------------------------------
CREATE TABLE route_stops (
    route_id UUID NOT NULL REFERENCES routes (id) ON DELETE CASCADE,
    stop_id UUID NOT NULL REFERENCES stops (id) ON DELETE RESTRICT,
    order_index INT NOT NULL,
    estimated_duration_from_origin_minutes INT NOT NULL,
    distance_from_origin_km DECIMAL(8,2) NULL,
    allow_pickup BOOLEAN NOT NULL DEFAULT TRUE,
    allow_dropoff BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (route_id, stop_id),
    CONSTRAINT chk_route_stops_allow_at_least_one CHECK (allow_pickup OR allow_dropoff),
    CONSTRAINT chk_route_stops_order_positive CHECK (order_index > 0)
);

CREATE UNIQUE INDEX uq_route_stops_route_order ON route_stops (route_id, order_index);
CREATE INDEX idx_route_stops_stop_id ON route_stops (stop_id);

COMMENT ON TABLE route_stops IS
    'Junction Route↔Stop for intermediate waypoints. Origin/destination Station live on Route entity, NOT here.';
COMMENT ON COLUMN route_stops.distance_from_origin_km IS
    'Manually entered by Operator. Used for DISRUPTED proportional refund (primary path).';

-- -----------------------------------------------------------------------------
-- route_stop_fare_templates (exception override for Route.baseFare per stop)
-- -----------------------------------------------------------------------------
CREATE TABLE route_stop_fare_templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    route_id UUID NOT NULL REFERENCES routes (id) ON DELETE CASCADE,
    stop_id UUID NOT NULL REFERENCES stops (id) ON DELETE RESTRICT,
    fare_from_this_stop BIGINT NOT NULL,
    effective_from TIMESTAMPTZ NOT NULL,
    effective_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_route_stop_fare_templates_fare_non_negative CHECK (fare_from_this_stop >= 0),
    CONSTRAINT chk_route_stop_fare_templates_effective_order
        CHECK (effective_until IS NULL OR effective_until > effective_from),
    CONSTRAINT ex_route_stop_fare_templates_no_overlap
        EXCLUDE USING gist (
            route_id WITH =,
            stop_id WITH =,
            tstzrange(effective_from, COALESCE(effective_until, 'infinity'::timestamptz), '[)') WITH &&
        )
);

CREATE INDEX idx_route_stop_fare_templates_route_stop_effective
    ON route_stop_fare_templates (route_id, stop_id, effective_from);

COMMENT ON TABLE route_stop_fare_templates IS
    'Exception only — entries exist solely for stops where Operator wants a fare different from Route.baseFare.';

-- -----------------------------------------------------------------------------
-- alternative_routes (max 2 per Route — enforced app-layer)
-- -----------------------------------------------------------------------------
CREATE TABLE alternative_routes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    route_id UUID NOT NULL REFERENCES routes (id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT NULL,
    destination_station_id UUID NOT NULL REFERENCES stations (id) ON DELETE RESTRICT,
    total_distance_km DECIMAL(8,2) NULL,
    estimated_duration_minutes INT NULL,
    path_polyline TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_alternative_routes_route_id ON alternative_routes (route_id) WHERE is_active = TRUE;

COMMENT ON COLUMN alternative_routes.path_polyline IS
    'Google encoded polyline, precision 5. Stored for future alternative-route tracking selection.';

-- -----------------------------------------------------------------------------
-- alternative_route_stops (independent stop sequence — NOT reuse RouteStop)
-- -----------------------------------------------------------------------------
CREATE TABLE alternative_route_stops (
    alternative_route_id UUID NOT NULL REFERENCES alternative_routes (id) ON DELETE CASCADE,
    stop_id UUID NOT NULL REFERENCES stops (id) ON DELETE RESTRICT,
    order_index INT NOT NULL,
    estimated_duration_from_origin_minutes INT NOT NULL,
    distance_from_origin_km DECIMAL(8,2) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (alternative_route_id, stop_id),
    CONSTRAINT chk_alternative_route_stops_order_positive CHECK (order_index > 0)
);

CREATE UNIQUE INDEX uq_alternative_route_stops_route_order
    ON alternative_route_stops (alternative_route_id, order_index);

-- -----------------------------------------------------------------------------
-- vehicle_types
-- -----------------------------------------------------------------------------
CREATE TABLE vehicle_types (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code VARCHAR(50) NOT NULL,
    display_name VARCHAR(255) NOT NULL,
    estimated_passenger_luggage_kg_per_seat INT NULL,
    default_seat_count INT NULL,
    is_system_defined BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_vehicle_types_code ON vehicle_types (code);
CREATE INDEX idx_vehicle_types_is_active ON vehicle_types (is_active);

COMMENT ON COLUMN vehicle_types.is_system_defined IS
    'true for 3 platform-seeded types (STANDARD_BUS, LIMOUSINE, SLEEPER_BUS). Blocks delete in app-layer.';

-- -----------------------------------------------------------------------------
-- vehicles
-- -----------------------------------------------------------------------------
CREATE TABLE vehicles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    vehicle_type_id UUID NOT NULL REFERENCES vehicle_types (id) ON DELETE RESTRICT,
    license_plate VARCHAR(20) NOT NULL,
    seat_layout_json JSONB NOT NULL,
    total_seats INT NOT NULL,
    max_cargo_weight_kg DECIMAL(8,2) NULL,
    max_cargo_volume_m3 DECIMAL(10,4) NULL,
    image_urls JSONB NULL,
    status vehicle_status NOT NULL DEFAULT 'ACTIVE',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    deleted_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_vehicles_total_seats_positive CHECK (total_seats > 0),
    CONSTRAINT chk_vehicles_cargo_weight_non_negative
        CHECK (max_cargo_weight_kg IS NULL OR max_cargo_weight_kg >= 0),
    CONSTRAINT chk_vehicles_cargo_volume_non_negative
        CHECK (max_cargo_volume_m3 IS NULL OR max_cargo_volume_m3 >= 0)
);

CREATE UNIQUE INDEX uq_vehicles_license_plate
    ON vehicles (license_plate) WHERE deleted_at IS NULL;
CREATE INDEX idx_vehicles_operator_status ON vehicles (operator_id, status) WHERE is_active = TRUE;
CREATE INDEX idx_vehicles_vehicle_type_id ON vehicles (vehicle_type_id);

COMMENT ON COLUMN vehicles.seat_layout_json IS
    'See Section 6.1 contract: version, vehicleTypeCode, totalSeats, rows, cols, decks, aisles[], seats[].';

-- -----------------------------------------------------------------------------
-- driver_schedules (recurring assignment driver/assistant ↔ vehicle ↔ route)
-- -----------------------------------------------------------------------------
CREATE TABLE driver_schedules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    route_id UUID NOT NULL REFERENCES routes (id) ON DELETE RESTRICT,
    vehicle_id UUID NULL REFERENCES vehicles (id) ON DELETE SET NULL,
    driver_user_id UUID NOT NULL,    -- logical FK → identity.users (role=DRIVER)
    assistant_user_id UUID NULL,     -- logical FK → identity.users (role=ASSISTANT)
    day_of_week JSONB NOT NULL,      -- e.g. [1,3,5] (1=Mon, 7=Sun)
    departure_time TIME NOT NULL,    -- local ICT
    valid_from DATE NOT NULL,
    valid_until DATE NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_driver_schedules_valid_until_after_from
        CHECK (valid_until IS NULL OR valid_until >= valid_from)
);

CREATE INDEX idx_driver_schedules_operator_active
    ON driver_schedules (operator_id, is_active);
CREATE INDEX idx_driver_schedules_driver_active
    ON driver_schedules (driver_user_id, is_active);
CREATE INDEX idx_driver_schedules_vehicle_active
    ON driver_schedules (vehicle_id, is_active) WHERE vehicle_id IS NOT NULL;
CREATE INDEX idx_driver_schedules_route_active
    ON driver_schedules (route_id, is_active);

COMMENT ON COLUMN driver_schedules.day_of_week IS
    'JSONB array of ints 1-7 (1=Mon). Hangfire weekly job iterates dayOfWeek to generate Trip.';
COMMENT ON COLUMN driver_schedules.departure_time IS
    'TIME (no timezone). Stored as local ICT semantic.';

-- -----------------------------------------------------------------------------
-- driver_schedule_audit_logs (append-only)
-- -----------------------------------------------------------------------------
CREATE TABLE driver_schedule_audit_logs (
    id UUID PRIMARY KEY,
    driver_schedule_id UUID NOT NULL REFERENCES driver_schedules (id) ON DELETE RESTRICT,
    actor_user_id UUID NULL, -- logical FK -> identity.users.id
    action VARCHAR(64) NOT NULL,
    metadata JSONB NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_driver_schedule_audit_logs_schedule_occurred
    ON driver_schedule_audit_logs (driver_schedule_id, occurred_at DESC);
CREATE INDEX idx_driver_schedule_audit_logs_actor_occurred
    ON driver_schedule_audit_logs (actor_user_id, occurred_at DESC)
    WHERE actor_user_id IS NOT NULL;
CREATE INDEX idx_driver_schedule_audit_logs_action_occurred
    ON driver_schedule_audit_logs (action, occurred_at DESC);

-- -----------------------------------------------------------------------------
-- trips
-- -----------------------------------------------------------------------------
CREATE TABLE trips (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    route_id UUID NOT NULL REFERENCES routes (id) ON DELETE RESTRICT,
    vehicle_id UUID NOT NULL REFERENCES vehicles (id) ON DELETE RESTRICT,
    driver_user_id UUID NOT NULL,    -- logical FK
    assistant_user_id UUID NULL,     -- logical FK
    driver_schedule_id UUID NULL REFERENCES driver_schedules (id) ON DELETE SET NULL,
    -- Lifecycle timestamps
    departure_date_time TIMESTAMPTZ NOT NULL,
    estimated_arrival_time TIMESTAMPTZ NOT NULL,
    actual_departure_time TIMESTAMPTZ NULL,
    destination_arrived_at TIMESTAMPTZ NULL,
    destination_arrived_by_user_id UUID NULL, -- logical FK -> identity.users.id
    completed_at TIMESTAMPTZ NULL,
    disrupted_at TIMESTAMPTZ NULL,
    disruption_reason TEXT NULL,
    cancelled_at TIMESTAMPTZ NULL,
    cancelled_by_user_id UUID NULL,
    cancel_reason TEXT NULL,
    completed_by_user_id UUID NULL,
    notes VARCHAR(2000) NULL,
    -- Status
    status trip_status NOT NULL DEFAULT 'SCHEDULED',
    source trip_source NOT NULL,
    has_substitution BOOLEAN NOT NULL DEFAULT FALSE,
    -- Pricing + cargo snapshot (immutable after Trip created)
    base_fare BIGINT NOT NULL,
    max_cargo_weight_kg DECIMAL(8,2) NULL,
    max_cargo_volume_m3 DECIMAL(10,4) NULL,
    estimated_passenger_luggage_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
    reserved_parcel_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
    reserved_parcel_volume_m3 DECIMAL(10,4) NOT NULL DEFAULT 0,
    total_loaded_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0,
    total_loaded_volume_m3 DECIMAL(10,4) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_trips_base_fare_non_negative CHECK (base_fare >= 0),
    CONSTRAINT chk_trips_cargo_counters_non_negative
        CHECK (reserved_parcel_weight_kg >= 0 AND reserved_parcel_volume_m3 >= 0 AND total_loaded_weight_kg >= 0 AND total_loaded_volume_m3 >= 0)
);

CREATE UNIQUE INDEX uq_trips_driver_departure
    ON trips (driver_user_id, departure_date_time)
    WHERE status NOT IN ('CANCELLED');
CREATE UNIQUE INDEX uq_trips_vehicle_departure
    ON trips (vehicle_id, departure_date_time)
    WHERE status NOT IN ('CANCELLED');
CREATE INDEX idx_trips_operator_status ON trips (operator_id, status);
CREATE INDEX idx_trips_route_departure ON trips (route_id, departure_date_time);
CREATE INDEX idx_trips_status_departure ON trips (status, departure_date_time);
CREATE INDEX idx_trips_assistant_user_id ON trips (assistant_user_id) WHERE assistant_user_id IS NOT NULL;
CREATE INDEX idx_trips_driver_schedule_id ON trips (driver_schedule_id) WHERE driver_schedule_id IS NOT NULL;
CREATE INDEX idx_trips_completed_report ON trips (completed_at, operator_id)
    WHERE status = 'COMPLETED' AND completed_at IS NOT NULL;

COMMENT ON COLUMN trips.estimated_passenger_luggage_kg IS
    'Snapshot at Trip create from VehicleType.estimatedPassengerLuggageKgPerSeat ?? Operator.luggagePolicy ?? 10 kg/seat × totalSeats.';
COMMENT ON COLUMN trips.has_substitution IS
    'Set true when Trip_old triggers Vehicle Substitution (6.12). Reporting field.';
COMMENT ON COLUMN trips.source IS
    'VEHICLE_SUBSTITUTION: created by 6.12 flow, exempt from maxTripsPerMonth counter check.';
COMMENT ON COLUMN trips.destination_arrived_at IS
    'Explicit Driver/Assistant destination-terminal anchor. Independent from completed_at; never synthesized by auto-complete.';
COMMENT ON COLUMN trips.destination_arrived_by_user_id IS
    'Logical FK to identity.users.id for the assigned Driver/Assistant who recorded destination arrival.';

-- -----------------------------------------------------------------------------
-- trip_audit_logs (append-only)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_audit_logs (
    id UUID PRIMARY KEY,
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE RESTRICT,
    actor_user_id UUID NULL, -- logical FK -> identity.users.id
    action VARCHAR(64) NOT NULL,
    metadata JSONB NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_trip_audit_logs_trip_occurred
    ON trip_audit_logs (trip_id, occurred_at DESC);
CREATE INDEX idx_trip_audit_logs_actor_occurred
    ON trip_audit_logs (actor_user_id, occurred_at DESC)
    WHERE actor_user_id IS NOT NULL;
CREATE INDEX idx_trip_audit_logs_action_occurred
    ON trip_audit_logs (action, occurred_at DESC);

-- -----------------------------------------------------------------------------
-- trip_cargo_parcels (Parcel-owned items tracked by Trip cargo counter)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_cargo_parcels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE CASCADE,
    parcel_id UUID NOT NULL, -- logical FK to Parcel service
    weight_kg DECIMAL(8,2) NOT NULL,
    volume_m3 DECIMAL(10,4) NOT NULL,
    actual_weight_kg DECIMAL(8,2) NULL,
    actual_volume_m3 DECIMAL(10,4) NULL,
    state VARCHAR(20) NOT NULL,
    loaded_at TIMESTAMPTZ NULL,
    released_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_trip_cargo_parcels_weight_positive CHECK (weight_kg > 0),
    CONSTRAINT chk_trip_cargo_parcels_volume_positive CHECK (volume_m3 > 0),
    CONSTRAINT chk_trip_cargo_parcels_actual_weight_positive CHECK (actual_weight_kg IS NULL OR actual_weight_kg > 0),
    CONSTRAINT chk_trip_cargo_parcels_actual_volume_positive CHECK (actual_volume_m3 IS NULL OR actual_volume_m3 > 0),
    CONSTRAINT chk_trip_cargo_parcels_state CHECK (state IN ('RESERVED', 'LOADED', 'RELEASED'))
);

CREATE UNIQUE INDEX uq_trip_cargo_parcels_trip_parcel
    ON trip_cargo_parcels (trip_id, parcel_id);
CREATE INDEX idx_trip_cargo_parcels_trip_state
    ON trip_cargo_parcels (trip_id, state);

-- -----------------------------------------------------------------------------
-- trip_seats
-- -----------------------------------------------------------------------------
CREATE TABLE trip_seats (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE CASCADE,
    seat_number VARCHAR(20) NOT NULL,
    seat_type trip_seat_type NOT NULL DEFAULT 'STANDARD',
    status trip_seat_status NOT NULL DEFAULT 'AVAILABLE',
    disabled_reason TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_trip_seats_trip_seat ON trip_seats (trip_id, seat_number);
CREATE INDEX idx_trip_seats_trip_status ON trip_seats (trip_id, status);

-- -----------------------------------------------------------------------------
-- trip_stops (snapshot from RouteStop; intermediate only)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_stops (
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE CASCADE,
    stop_id UUID NOT NULL REFERENCES stops (id) ON DELETE RESTRICT,
    order_index INT NOT NULL,
    estimated_arrival_time TIMESTAMPTZ NOT NULL,
    actual_arrival_time TIMESTAMPTZ NULL,
    actual_departure_time TIMESTAMPTZ NULL,
    status trip_stop_status NOT NULL DEFAULT 'PENDING',
    allow_pickup BOOLEAN NOT NULL,
    allow_dropoff BOOLEAN NOT NULL,
    distance_from_origin_km DECIMAL(8,2) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (trip_id, stop_id)
);

CREATE UNIQUE INDEX uq_trip_stops_trip_order ON trip_stops (trip_id, order_index);
CREATE INDEX idx_trip_stops_trip_status ON trip_stops (trip_id, status);
CREATE INDEX idx_trip_stops_estimated_arrival ON trip_stops (estimated_arrival_time)
    WHERE status = 'PENDING';

COMMENT ON COLUMN trip_stops.estimated_arrival_time IS
    'Static planned baseline. An approved pre-departure Route edit or DriverSchedule ALL_PENDING cascade may recompute it; GPS/Tracking dynamic ETA never updates this column.';

-- -----------------------------------------------------------------------------
-- trip_stop_fares (exception override per trip per stop; from RouteStopFareTemplate)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_stop_fares (
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE CASCADE,
    stop_id UUID NOT NULL REFERENCES stops (id) ON DELETE RESTRICT,
    fare_from_this_stop BIGINT NOT NULL,
    source trip_stop_fare_source NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (trip_id, stop_id),
    CONSTRAINT chk_trip_stop_fares_fare_non_negative CHECK (fare_from_this_stop >= 0)
);

COMMENT ON COLUMN trip_stop_fares.source IS
    'TEMPLATE_SNAPSHOT is legacy-readable only; explicit per-Trip fare overrides use MANUAL_OVERRIDE.';

-- -----------------------------------------------------------------------------
-- trip_generation_skip_logs (Hangfire skip audit)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_generation_skip_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    driver_schedule_id UUID NOT NULL REFERENCES driver_schedules (id) ON DELETE CASCADE,
    skipped_date DATE NOT NULL,
    reason trip_generation_skip_reason NOT NULL,
    message TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_trip_gen_skip_logs_operator_date
    ON trip_generation_skip_logs (operator_id, skipped_date DESC);
CREATE INDEX idx_trip_gen_skip_logs_schedule
    ON trip_generation_skip_logs (driver_schedule_id, skipped_date DESC);

-- -----------------------------------------------------------------------------
-- shuttle_trips
-- -----------------------------------------------------------------------------
CREATE TABLE shuttle_trips (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    main_trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE RESTRICT,
    station_id UUID NOT NULL REFERENCES stations (id) ON DELETE RESTRICT,
    direction VARCHAR(30) NOT NULL,
    driver_user_id UUID NOT NULL,
    vehicle_id UUID NOT NULL REFERENCES vehicles (id) ON DELETE RESTRICT,
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

CREATE INDEX idx_shuttle_trips_main_trip ON shuttle_trips (main_trip_id);
CREATE INDEX idx_shuttle_trips_operator_status ON shuttle_trips (operator_id, status);
CREATE INDEX idx_shuttle_trips_station_direction ON shuttle_trips (station_id, direction);
CREATE INDEX idx_shuttle_trips_driver_schedule
    ON shuttle_trips (driver_user_id, scheduled_departure_time, scheduled_end_time)
    WHERE status IN ('SCHEDULED', 'IN_PROGRESS');
CREATE INDEX idx_shuttle_trips_vehicle_schedule
    ON shuttle_trips (vehicle_id, scheduled_departure_time, scheduled_end_time)
    WHERE status IN ('SCHEDULED', 'IN_PROGRESS');

-- -----------------------------------------------------------------------------
-- shuttle_passengers (manifest)
-- -----------------------------------------------------------------------------
CREATE TABLE shuttle_passengers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    shuttle_trip_id UUID NULL REFERENCES shuttle_trips (id) ON DELETE SET NULL,
    main_trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE RESTRICT,
    booking_id UUID NULL,    -- logical FK → booking.bookings
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

CREATE INDEX idx_shuttle_passengers_shuttle_trip ON shuttle_passengers (shuttle_trip_id)
    WHERE shuttle_trip_id IS NOT NULL;
CREATE INDEX idx_shuttle_passengers_main_trip_status ON shuttle_passengers (main_trip_id, status);
CREATE INDEX idx_shuttle_passengers_booking ON shuttle_passengers (booking_id) WHERE booking_id IS NOT NULL;
CREATE UNIQUE INDEX uq_shuttle_passengers_booking_ticket
    ON shuttle_passengers (booking_id, ticket_id)
    WHERE booking_id IS NOT NULL AND ticket_id IS NOT NULL;

-- -----------------------------------------------------------------------------
-- shuttle_dispatch_alerts (warning/cutoff idempotency markers)
-- -----------------------------------------------------------------------------
CREATE TABLE shuttle_dispatch_alerts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    main_trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE RESTRICT,
    operator_id UUID NOT NULL,
    alert_type VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_shuttle_dispatch_alerts_trip_type UNIQUE (main_trip_id, alert_type),
    CONSTRAINT chk_shuttle_dispatch_alerts_type CHECK (alert_type IN ('WARNING_120', 'WARNING_60', 'AUTO_CUTOFF'))
);

CREATE INDEX idx_shuttle_dispatch_alerts_operator_created
    ON shuttle_dispatch_alerts (operator_id, created_at DESC);

COMMENT ON COLUMN shuttle_passengers.shuttle_trip_id IS
    'NULL when passenger registered but Operator has not created ShuttleTrip yet (status=PENDING_ASSIGNMENT).';

-- -----------------------------------------------------------------------------
-- incidents (driver-reported incidents)
-- -----------------------------------------------------------------------------
CREATE TABLE incidents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id UUID NOT NULL REFERENCES trips (id) ON DELETE RESTRICT,
    reported_by_user_id UUID NOT NULL,
    category incident_category NOT NULL,
    description TEXT NULL,
    photo_urls JSONB NULL,    -- string[] max 3 URLs (Firebase Storage)
    latitude DECIMAL(10,7) NULL,
    longitude DECIMAL(10,7) NULL,
    reported_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at TIMESTAMPTZ NULL,
    resolved_by_user_id UUID NULL,
    resolution_note TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_incidents_trip_id ON incidents (trip_id);
CREATE INDEX idx_incidents_reported_by ON incidents (reported_by_user_id);
CREATE INDEX idx_incidents_reported_at ON incidents (reported_at DESC);

-- -----------------------------------------------------------------------------
-- outbox_events
-- -----------------------------------------------------------------------------
CREATE TABLE outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    status outbox_event_status NOT NULL DEFAULT 'PENDING',
    retry_count INT NOT NULL DEFAULT 0,
    last_error TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_outbox_events_status_created
    ON outbox_events (status, created_at) WHERE status IN ('PENDING', 'PUBLISHING', 'FAILED');

-- =============================================================================
-- TRIGGERS — auto-update updated_at
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_stations_updated_at BEFORE UPDATE ON stations
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_stations_updated_at BEFORE UPDATE ON operator_stations
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_stops_updated_at BEFORE UPDATE ON stops
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_routes_updated_at BEFORE UPDATE ON routes
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_route_stops_updated_at BEFORE UPDATE ON route_stops
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_route_stop_fare_templates_updated_at BEFORE UPDATE ON route_stop_fare_templates
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_alternative_routes_updated_at BEFORE UPDATE ON alternative_routes
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_alternative_route_stops_updated_at BEFORE UPDATE ON alternative_route_stops
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_vehicle_types_updated_at BEFORE UPDATE ON vehicle_types
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_vehicles_updated_at BEFORE UPDATE ON vehicles
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_driver_schedules_updated_at BEFORE UPDATE ON driver_schedules
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_trips_updated_at BEFORE UPDATE ON trips
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_trip_seats_updated_at BEFORE UPDATE ON trip_seats
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_trip_stops_updated_at BEFORE UPDATE ON trip_stops
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_shuttle_trips_updated_at BEFORE UPDATE ON shuttle_trips
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_shuttle_passengers_updated_at BEFORE UPDATE ON shuttle_passengers
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_incidents_updated_at BEFORE UPDATE ON incidents
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- =============================================================================
-- Hangfire schema lives in this DB under `hangfire.*` — auto-created at startup.
-- =============================================================================
