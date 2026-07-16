const childProcess = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

require('dotenv').config({ path: path.resolve(process.cwd(), '.env') });

const repoRoot = process.cwd();
const collectionPath = path.join(repoRoot, 'docs/api/postman/vietride.postman_collection.json');
const environmentPath = path.join(
  repoRoot,
  'docs/api/postman/vietride.local.postman_environment.json',
);
const appSettingsPath = path.join(
  repoRoot,
  'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json',
);

const identitySchema = 'vietride_identity';
const tripSchema = 'vietride_trip';
const tripServiceBaseUrl =
  process.env.DAY11_TRIP_SERVICE_BASE_URL ||
  process.env.TRIP_SERVICE_BASE_URL ||
  'http://localhost:5002';

function addDaysUtc(date, days) {
  const next = new Date(date.getTime());
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function formatDateOnlyUtc(date) {
  return date.toISOString().slice(0, 10);
}

function toContractDayOfWeekUtc(date) {
  const day = date.getUTCDay();
  return day === 0 ? 7 : day;
}

const nowInIct = new Date(Date.now() + 7 * 60 * 60 * 1000);
const targetLocalDate = addDaysUtc(
  new Date(Date.UTC(nowInIct.getUTCFullYear(), nowInIct.getUTCMonth(), nowInIct.getUTCDate())),
  1,
);
const day11DepartureDate = formatDateOnlyUtc(targetLocalDate);
const day11ScheduleDayOfWeek = toContractDayOfWeekUtc(targetLocalDate);
const day11DepartureStart = `${day11DepartureDate}T00:00:00Z`;
const day11DepartureEnd = `${formatDateOnlyUtc(addDaysUtc(targetLocalDate, 1))}T00:00:00Z`;

const seed = {
  operatorId: '10000000-0000-0000-0000-000000000011',
  operatorAdminUserId: '10000000-0000-0000-0000-0000000000a1',
  driverUserId: '70000000-0000-0000-0000-000000000011',
  assistantUserId: '70000000-0000-0000-0000-0000000000a1',
  passengerUserId: '90000000-0000-0000-0000-000000000011',
  originStationId: '30000000-0000-0000-0000-000000000011',
  destinationStationId: '30000000-0000-0000-0000-000000000021',
  stopId: '40000000-0000-0000-0000-000000000011',
  routeId: '50000000-0000-0000-0000-000000000011',
  vehicleId: '60000000-0000-0000-0000-000000000011',
  standardVehicleTypeId: '00000000-0000-0000-0000-000000000101',
  driverScheduleId: '80000000-0000-0000-0000-000000000011',
  legacyTripId: '81000000-0000-0000-0000-000000000011',
  subscriptionPlanId: '11000000-0000-0000-0000-000000000011',
  subscriptionId: '12000000-0000-0000-0000-000000000011',
};

const runId = crypto.randomUUID();

function dockerArgs(database) {
  return [
    'exec',
    '-i',
    process.env.POSTGRES_CONTAINER || 'vietride_postgres',
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-At',
    '-F',
    '|',
    '-U',
    process.env.POSTGRES_USER || 'vietride',
    '-d',
    database,
  ];
}

function runSql(database, sql) {
  const child = childProcess.spawnSync('docker', dockerArgs(database), {
    input: sql,
    stdio: ['pipe', 'inherit', 'inherit'],
    encoding: 'utf8',
  });

  if (child.error) throw child.error;
  if (child.status !== 0)
    throw new Error(`psql failed for database ${database} with exit code ${child.status}`);
}

function querySql(database, sql) {
  const child = childProcess.spawnSync('docker', dockerArgs(database), {
    input: sql,
    stdio: ['pipe', 'pipe', 'inherit'],
    encoding: 'utf8',
  });

  if (child.error) throw child.error;
  if (child.status !== 0)
    throw new Error(`psql query failed for database ${database} with exit code ${child.status}`);
  return child.stdout.trim();
}

function readDevJwtOptions() {
  const appSettings = JSON.parse(fs.readFileSync(appSettingsPath, 'utf8'));
  const identityJwt = appSettings.IdentityJwt || {};
  const privateKey = process.env.USER_JWT_PRIVATE_KEY || identityJwt.PrivateKey;
  const kid = process.env.USER_JWT_KID || identityJwt.Kid;

  if (!privateKey || !kid) {
    throw new Error(
      'IdentityJwt dev private key/kid not found. Run against Development config or set USER_JWT_PRIVATE_KEY/USER_JWT_KID.',
    );
  }

  return { privateKey, kid };
}

async function issueUserToken({ sub, role, email, operatorId }) {
  const { SignJWT, importPKCS8 } = await import('jose');
  const { privateKey, kid } = readDevJwtOptions();
  const key = await importPKCS8(privateKey, 'RS256');
  const payload = { role, email, hasPhone: 'true' };
  if (operatorId) payload.operatorId = operatorId;

  return new SignJWT(payload)
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

function seedIdentity() {
  runSql(
    process.env.IDENTITY_DB || 'vietride_identity',
    `begin;

insert into ${identitySchema}.operators (
  id, name, business_registration_number, tax_code, contact_email, contact_phone,
  registration_status, is_active, representative_name, representative_phone
)
values (
  '${seed.operatorId}', 'Day 11 Local Search Operator', 'DAY11-APPROVED-BRN', 'DAY11-APPROVED-TAX',
  'day11-operator@example.test', '+84911000001',
  'APPROVED'::operator_registration_status, true, 'Day 11 Rep', '+84911000002'
)
on conflict (id) do update set
  name = excluded.name,
  business_registration_number = excluded.business_registration_number,
  tax_code = excluded.tax_code,
  contact_email = excluded.contact_email,
  contact_phone = excluded.contact_phone,
  registration_status = excluded.registration_status,
  is_active = excluded.is_active,
  updated_at = now();

insert into ${identitySchema}.users (
  id, email, phone, password_hash, display_name, role, status, operator_id
)
values
  ('${seed.operatorAdminUserId}', 'day11-operator-admin@example.test', '+84911000011', null,
   'Day 11 Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.operatorId}'),
  ('${seed.driverUserId}', 'day11-driver@example.test', '+84911000012', null,
   'Day 11 Driver', 'DRIVER'::user_role, 'ACTIVE'::user_status, '${seed.operatorId}'),
  ('${seed.assistantUserId}', 'day11-assistant@example.test', '+84911000013', null,
   'Day 11 Assistant', 'ASSISTANT'::user_role, 'ACTIVE'::user_status, '${seed.operatorId}'),
  ('${seed.passengerUserId}', 'day11-passenger@example.test', '+84911000014', null,
   'Day 11 Passenger', 'PASSENGER'::user_role, 'ACTIVE'::user_status, null)
on conflict (id) do update set
  email = excluded.email,
  phone = excluded.phone,
  display_name = excluded.display_name,
  role = excluded.role,
  status = excluded.status,
  operator_id = excluded.operator_id,
  deleted_at = null,
  updated_at = now();

insert into ${identitySchema}.subscription_plans (
  id, name, description, price_per_month, price_per_year, max_vehicles, max_drivers,
  max_assistants, max_operator_users, max_routes, max_trips_per_month, is_active
)
values (
  '${seed.subscriptionPlanId}', 'Day 11 deterministic local plan', 'Harness-only quota fixture',
  0, 0, 100, 100, 100, 100, 100, 100, true
)
on conflict (id) do update set
  name = excluded.name,
  description = excluded.description,
  max_vehicles = excluded.max_vehicles,
  max_drivers = excluded.max_drivers,
  max_assistants = excluded.max_assistants,
  max_operator_users = excluded.max_operator_users,
  max_routes = excluded.max_routes,
  max_trips_per_month = excluded.max_trips_per_month,
  is_active = true,
  updated_at = now();

insert into ${identitySchema}.operator_subscriptions (
  id, operator_id, plan_id, status, started_at, expires_at
)
values (
  '${seed.subscriptionId}', '${seed.operatorId}', '${seed.subscriptionPlanId}',
  'ACTIVE'::subscription_status, now() - interval '1 day', now() + interval '30 days'
)
on conflict (operator_id) do update set
  id = excluded.id,
  plan_id = excluded.plan_id,
  status = excluded.status,
  started_at = excluded.started_at,
  expires_at = excluded.expires_at,
  current_trips_this_month = 0,
  updated_at = now();

commit;
`,
  );
}

function seedTripConfig() {
  runSql(
    process.env.TRIP_DB || 'vietride_trip',
    `begin;

create extension if not exists pgcrypto;

delete from ${tripSchema}.trip_stop_fares where trip_id = '${seed.legacyTripId}' or trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trip_stops where trip_id = '${seed.legacyTripId}' or trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trip_seats where trip_id = '${seed.legacyTripId}' or trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trips where id = '${seed.legacyTripId}' or driver_schedule_id = '${seed.driverScheduleId}';
delete from ${tripSchema}.trip_stop_fares where trip_id in (select t.id from ${tripSchema}.trips t join ${tripSchema}.vehicles v on v.id = t.vehicle_id where v.id = '${seed.vehicleId}' or v.license_plate like 'DAY11-%');
delete from ${tripSchema}.trip_stops where trip_id in (select t.id from ${tripSchema}.trips t join ${tripSchema}.vehicles v on v.id = t.vehicle_id where v.id = '${seed.vehicleId}' or v.license_plate like 'DAY11-%');
delete from ${tripSchema}.trip_seats where trip_id in (select t.id from ${tripSchema}.trips t join ${tripSchema}.vehicles v on v.id = t.vehicle_id where v.id = '${seed.vehicleId}' or v.license_plate like 'DAY11-%');
delete from ${tripSchema}.trips where vehicle_id in (select id from ${tripSchema}.vehicles where id = '${seed.vehicleId}' or license_plate like 'DAY11-%');
delete from ${tripSchema}.trip_generation_skip_logs where driver_schedule_id = '${seed.driverScheduleId}';
delete from ${tripSchema}.driver_schedules where id = '${seed.driverScheduleId}' or operator_id = '${seed.operatorId}';
delete from ${tripSchema}.vehicles where id = '${seed.vehicleId}' or license_plate like 'DAY11-%';
delete from ${tripSchema}.route_stop_fare_templates where route_id = '${seed.routeId}' or stop_id = '${seed.stopId}';
delete from ${tripSchema}.route_stops where route_id = '${seed.routeId}' or stop_id = '${seed.stopId}';
delete from ${tripSchema}.routes where id = '${seed.routeId}' or operator_id = '${seed.operatorId}' or name like 'Day 11 %';
delete from ${tripSchema}.operator_stations
 where operator_id = '${seed.operatorId}'
    or station_id in ('${seed.originStationId}', '${seed.destinationStationId}');
delete from ${tripSchema}.stops where id = '${seed.stopId}' or operator_id = '${seed.operatorId}' or name like 'Day 11 %';
delete from ${tripSchema}.stations where id in ('${seed.originStationId}', '${seed.destinationStationId}');

insert into ${tripSchema}.stations (
  id, name, slug, address_street, city, province, latitude, longitude,
  contact_phone, contact_email, operating_hours, facilities, supports_shuttle, is_active
)
values
  ('${seed.originStationId}', 'Day 11 Saigon Station', 'day11-saigon-station', '1 Day 11 Saigon',
   'Ho Chi Minh City', 'Ho Chi Minh', 10.7212345, 106.6267890,
   '02811000001', 'day11-saigon@example.test', '{"mon":"05:00-22:00"}'::jsonb,
   '["waiting_room"]'::jsonb, true, true),
  ('${seed.destinationStationId}', 'Day 11 Can Tho Station', 'day11-can-tho-station', '2 Day 11 Can Tho',
   'Can Tho', 'Can Tho', 10.0451618, 105.7468535,
   '02921100002', 'day11-cantho@example.test', '{"mon":"05:00-22:00"}'::jsonb,
   '["waiting_room"]'::jsonb, true, true)
on conflict (id) do update set
  name = excluded.name,
  slug = excluded.slug,
  address_street = excluded.address_street,
  city = excluded.city,
  province = excluded.province,
  latitude = excluded.latitude,
  longitude = excluded.longitude,
  contact_phone = excluded.contact_phone,
  contact_email = excluded.contact_email,
  operating_hours = excluded.operating_hours,
  facilities = excluded.facilities,
  supports_shuttle = excluded.supports_shuttle,
  is_active = excluded.is_active,
  deleted_at = null,
  updated_at = now();

insert into ${tripSchema}.operator_stations (operator_id, station_id, is_active)
values
  ('${seed.operatorId}', '${seed.originStationId}', true),
  ('${seed.operatorId}', '${seed.destinationStationId}', true)
on conflict (operator_id, station_id) do update set
  is_active = excluded.is_active,
  updated_at = now();

insert into ${tripSchema}.stops (
  id, operator_id, name, description, latitude, longitude, address, google_place_id, is_active
)
values (
  '${seed.stopId}', '${seed.operatorId}', 'Day 11 Mekong Rest Stop',
  'Intermediate Day 11 stop for detail/fare projection', 10.4300000, 106.0500000,
  'Day 11 Stop Address', 'day11-mekong-rest-stop', true
)
on conflict (id) do update set
  operator_id = excluded.operator_id,
  name = excluded.name,
  description = excluded.description,
  latitude = excluded.latitude,
  longitude = excluded.longitude,
  address = excluded.address,
  google_place_id = excluded.google_place_id,
  is_active = excluded.is_active,
  deleted_at = null,
  updated_at = now();

insert into ${tripSchema}.routes (
  id, operator_id, name, origin_station_id, destination_station_id, return_route_id,
  base_fare, total_distance_km, estimated_duration_minutes, is_active, deleted_at
)
values (
  '${seed.routeId}', '${seed.operatorId}', 'Day 11 Saigon to Can Tho Route',
  '${seed.originStationId}', '${seed.destinationStationId}', null,
  180000, 170.50, 210, true, null
)
on conflict (id) do update set
  operator_id = excluded.operator_id,
  name = excluded.name,
  origin_station_id = excluded.origin_station_id,
  destination_station_id = excluded.destination_station_id,
  return_route_id = excluded.return_route_id,
  base_fare = excluded.base_fare,
  total_distance_km = excluded.total_distance_km,
  estimated_duration_minutes = excluded.estimated_duration_minutes,
  is_active = excluded.is_active,
  deleted_at = null,
  updated_at = now();

insert into ${tripSchema}.route_stops (
  route_id, stop_id, order_index, estimated_duration_from_origin_minutes,
  distance_from_origin_km, allow_pickup, allow_dropoff
)
values ('${seed.routeId}', '${seed.stopId}', 1, 90, 75.50, true, true)
on conflict (route_id, stop_id) do update set
  order_index = excluded.order_index,
  estimated_duration_from_origin_minutes = excluded.estimated_duration_from_origin_minutes,
  distance_from_origin_km = excluded.distance_from_origin_km,
  allow_pickup = excluded.allow_pickup,
  allow_dropoff = excluded.allow_dropoff,
  updated_at = now();

insert into ${tripSchema}.route_stop_fare_templates (
  id, route_id, stop_id, fare_from_this_stop, effective_from, effective_until
)
values (
  '41000000-0000-0000-0000-000000000011', '${seed.routeId}', '${seed.stopId}',
  120000, '2026-01-01T00:00:00Z', null
)
on conflict (id) do update set
  route_id = excluded.route_id,
  stop_id = excluded.stop_id,
  fare_from_this_stop = excluded.fare_from_this_stop,
  effective_from = excluded.effective_from,
  effective_until = excluded.effective_until,
  updated_at = now();

insert into ${tripSchema}.vehicles (
  id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats,
  max_cargo_weight_kg, max_cargo_volume_m3, status, is_active, deleted_at
)
values (
  '${seed.vehicleId}', '${seed.operatorId}', '${seed.standardVehicleTypeId}', 'DAY11-LOCAL-01',
  '{"Version":1,"VehicleTypeCode":"STANDARD_BUS","TotalSeats":6,"Rows":3,"Cols":2,"Decks":1,"Aisles":[{"AfterCol":1}],"Seats":[{"SeatNumber":"A01","Row":1,"Col":1,"Deck":1,"Type":"STANDARD","IsWindow":true,"IsAisle":false,"Disabled":false},{"SeatNumber":"A02","Row":1,"Col":2,"Deck":1,"Type":"STANDARD","IsWindow":false,"IsAisle":true,"Disabled":false},{"SeatNumber":"A03","Row":2,"Col":1,"Deck":1,"Type":"STANDARD","IsWindow":true,"IsAisle":false,"Disabled":false},{"SeatNumber":"A04","Row":2,"Col":2,"Deck":1,"Type":"STANDARD","IsWindow":false,"IsAisle":true,"Disabled":false},{"SeatNumber":"A05","Row":3,"Col":1,"Deck":1,"Type":"STANDARD","IsWindow":true,"IsAisle":false,"Disabled":false},{"SeatNumber":"A06","Row":3,"Col":2,"Deck":1,"Type":"STANDARD","IsWindow":false,"IsAisle":true,"Disabled":false}]}'::jsonb,
  6, 100.00, 1.50, 'ACTIVE', true, null
)
on conflict (id) do update set
  operator_id = excluded.operator_id,
  vehicle_type_id = excluded.vehicle_type_id,
  license_plate = excluded.license_plate,
  seat_layout_json = excluded.seat_layout_json,
  total_seats = excluded.total_seats,
  max_cargo_weight_kg = excluded.max_cargo_weight_kg,
  max_cargo_volume_m3 = excluded.max_cargo_volume_m3,
  status = excluded.status,
  is_active = excluded.is_active,
  deleted_at = null,
  updated_at = now();

insert into ${tripSchema}.driver_schedules (
  id, operator_id, route_id, vehicle_id, driver_user_id, assistant_user_id,
  day_of_week, departure_time, valid_from, valid_until, is_active
)
values (
  '${seed.driverScheduleId}', '${seed.operatorId}', '${seed.routeId}', '${seed.vehicleId}',
  '${seed.driverUserId}', '${seed.assistantUserId}', '[${day11ScheduleDayOfWeek}]'::jsonb,
  '08:00:00', '${day11DepartureDate}', '${day11DepartureDate}', false
)
on conflict (id) do update set
  operator_id = excluded.operator_id,
  route_id = excluded.route_id,
  vehicle_id = excluded.vehicle_id,
  driver_user_id = excluded.driver_user_id,
  assistant_user_id = excluded.assistant_user_id,
  day_of_week = excluded.day_of_week,
  departure_time = excluded.departure_time,
  valid_from = excluded.valid_from,
  valid_until = excluded.valid_until,
  is_active = false,
  updated_at = now();

commit;
`,
  );
}

function cleanupFixtures() {
  runSql(
    process.env.TRIP_DB || 'vietride_trip',
    `begin;
delete from ${tripSchema}.trip_stop_fares where trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trip_stops where trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trip_seats where trip_id in (select id from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}');
delete from ${tripSchema}.trips where driver_schedule_id = '${seed.driverScheduleId}';
delete from ${tripSchema}.trip_generation_skip_logs where driver_schedule_id = '${seed.driverScheduleId}';
delete from ${tripSchema}.driver_schedules where id = '${seed.driverScheduleId}';
delete from ${tripSchema}.vehicles where id = '${seed.vehicleId}';
delete from ${tripSchema}.route_stop_fare_templates where route_id = '${seed.routeId}';
delete from ${tripSchema}.route_stops where route_id = '${seed.routeId}';
delete from ${tripSchema}.routes where id = '${seed.routeId}';
delete from ${tripSchema}.operator_stations where operator_id = '${seed.operatorId}';
delete from ${tripSchema}.stops where id = '${seed.stopId}';
delete from ${tripSchema}.stations where id in ('${seed.originStationId}', '${seed.destinationStationId}');
commit;`,
  );
  runSql(
    process.env.IDENTITY_DB || 'vietride_identity',
    `begin;
delete from ${identitySchema}.subscription_quota_allocations where operator_id = '${seed.operatorId}';
delete from ${identitySchema}.operator_subscriptions where operator_id = '${seed.operatorId}';
delete from ${identitySchema}.users where id in ('${seed.operatorAdminUserId}', '${seed.driverUserId}', '${seed.assistantUserId}', '${seed.passengerUserId}');
delete from ${identitySchema}.operators where id = '${seed.operatorId}';
delete from ${identitySchema}.subscription_plans where id = '${seed.subscriptionPlanId}';
commit;`,
  );
}

function quoteCommandArg(value) {
  return `"${String(value).replace(/"/g, '""')}"`;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function redactValue(value, visibleChars = 6) {
  const text = String(value ?? '');
  return text.length <= visibleChars * 2
    ? `${text.slice(0, visibleChars)}...`
    : `${text.slice(0, visibleChars)}...${text.slice(-visibleChars)}`;
}

function normalizeUuid(value) {
  return String(value).replace(/-/g, '').toLowerCase();
}

function toUuidFromHash(input) {
  const bytes = Buffer.from(
    crypto.createHash('sha256').update(String(input)).digest().subarray(0, 16),
  );
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return [
    hex.slice(0, 8),
    hex.slice(8, 12),
    hex.slice(12, 16),
    hex.slice(16, 20),
    hex.slice(20),
  ].join('-');
}

function createInternalJwt(secret, payloadOverrides = {}) {
  if (!secret || secret.length < 32)
    throw new Error('INTERNAL_JWT_SECRET must be at least 32 characters long.');

  const now = Math.floor(Date.now() / 1000);
  const header = { alg: 'HS256', typ: 'JWT' };
  const payload = {
    iss: 'vietride-gateway',
    aud: 'vietride-internal',
    iat: now,
    exp: now + 120,
    sub: 'day11-local-harness',
    ...payloadOverrides,
  };
  const base64urlJson = (value) => Buffer.from(JSON.stringify(value)).toString('base64url');
  const signingInput = `${base64urlJson(header)}.${base64urlJson(payload)}`;
  const signature = crypto.createHmac('sha256', secret).update(signingInput).digest('base64url');
  return `${signingInput}.${signature}`;
}

async function fetchJson(url, options = {}) {
  const controller = new AbortController();
  const timeoutMs = options.timeoutMs ?? 10000;
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, { ...options, signal: controller.signal });
    const text = await response.text();
    const json = text ? JSON.parse(text) : null;
    return { response, json, text };
  } finally {
    clearTimeout(timer);
  }
}

function ensureCurrentRealSeamImages() {
  const compose = [
    'compose',
    '--env-file',
    '.env',
    '-f',
    'infra/docker/docker-compose.yml',
    '--profile',
    'app',
  ];
  const build = childProcess.spawnSync('docker', [...compose, 'build', 'identity', 'trip'], {
    cwd: repoRoot,
    stdio: 'inherit',
  });
  if (build.error || build.status !== 0)
    throw new Error('Could not build current Identity and Trip images for the Day-11 real seam.');

  const up = childProcess.spawnSync(
    'docker',
    [...compose, 'up', '-d', '--force-recreate', 'identity', 'trip'],
    { cwd: repoRoot, stdio: 'inherit' },
  );
  if (up.error || up.status !== 0)
    throw new Error('Could not recreate current Identity and Trip containers for the Day-11 real seam.');
}

async function waitForContainerHealthy(containerName) {
  for (let attempt = 0; attempt < 30; attempt += 1) {
    const probe = childProcess.spawnSync(
      'docker',
      ['inspect', '--format', '{{.State.Health.Status}}', containerName],
      { cwd: repoRoot, encoding: 'utf8' },
    );
    if (probe.status === 0 && probe.stdout.trim() === 'healthy') return;
    await sleep(1000);
  }
  throw new Error(`Day-11 real-seam container did not become healthy: ${containerName}`);
}

async function verifyIdentityQuotaAllocationRoute() {
  const identityBaseUrl =
    process.env.DAY11_IDENTITY_SERVICE_BASE_URL || process.env.IDENTITY_SERVICE_BASE_URL || 'http://localhost:5001';
  const { response, text } = await fetchJson(
    `${identityBaseUrl.replace(/\/$/, '')}/internal/v1/operators/${seed.operatorId}/quota-allocations`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    },
  );

  // An unauthenticated request must match the protected route and return 401.
  // This catches a stale Identity image that lacks the endpoint without mutating a quota.
  if (response.status !== 401) {
    throw new Error(
      `Day-11 quota-allocation route preflight expected protected 401, got ${response.status}: ${text}`,
    );
  }
  console.log('PASS | D11 Identity quota route | protected internal quota-allocation endpoint is current');
}

async function activateDriverScheduleThroughGateway(operatorAdminAccessToken) {
  const baseUrl =
    process.env.DAY11_GATEWAY_BASE_URL || process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
  const { response, json, text } = await fetchJson(
    `${baseUrl.replace(/\/$/, '')}/v1/operator/driver-schedules/${seed.driverScheduleId}/activate`,
    {
      method: 'PATCH',
      headers: {
        Authorization: `Bearer ${operatorAdminAccessToken}`,
      },
      timeoutMs: 20000,
    },
  );

  if (response.status !== 200 || json?.success !== true) {
    throw new Error(`Day-11 pre-Newman activation failed: ${response.status} ${text}`);
  }

  console.log(
    'Day-11 activation preflight: Gateway PATCH returned 200; polling generated Trip before Newman folder.',
  );
}

function parsePipeRows(output) {
  return output
    ? output
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter(Boolean)
        .map((line) => line.split('|'))
    : [];
}

async function runNewman(envVars) {
  const args = [
    'newman',
    'run',
    collectionPath,
    '-e',
    environmentPath,
    '--folder',
    'Trip — Day 11 search + seat-lock flow',
  ];

  for (const [key, value] of Object.entries(envVars)) args.push('--env-var', `${key}=${value}`);

  if (process.platform !== 'win32') {
    const child = childProcess.spawn('npx', args, { stdio: 'inherit' });
    return new Promise((resolve) => child.on('exit', (code) => resolve(code || 0)));
  }

  const npxCmd = fs.existsSync('C:\\Program Files\\nodejs\\npx.cmd')
    ? 'C:\\Program Files\\nodejs\\npx.cmd'
    : 'npx.cmd';
  const command = [npxCmd, ...args].map(quoteCommandArg).join(' ');
  const child = childProcess.exec(command, { maxBuffer: 20 * 1024 * 1024 });
  child.stdout?.pipe(process.stdout);
  child.stderr?.pipe(process.stderr);
  return new Promise((resolve) => child.on('exit', (code) => resolve(code || 0)));
}

function loadGenerationCount() {
  return Number(
    querySql(
      process.env.TRIP_DB || 'vietride_trip',
      `select count(*)::text
from ${tripSchema}.trips t
where t.driver_schedule_id = '${seed.driverScheduleId}'
  and t.operator_id = '${seed.operatorId}'
  and t.route_id = '${seed.routeId}'
  and t.departure_date_time >= '${day11DepartureStart}'
  and t.departure_date_time < '${day11DepartureEnd}'
  and t.status in ('SCHEDULED'::${tripSchema}.trip_status, 'BOARDING'::${tripSchema}.trip_status);`,
    ) || '0',
  );
}

function loadGeneratedTripEvidence() {
  const rows = parsePipeRows(
    querySql(
      process.env.TRIP_DB || 'vietride_trip',
      `select
  t.id::text,
  t.status::text,
  count(distinct ts.seat_number) filter (where ts.seat_number in ('A01', 'A02'))::text,
  count(*) filter (where ts.seat_number = 'A01' and ts.status = 'AVAILABLE'::${tripSchema}.trip_seat_status)::text,
  count(*) filter (where ts.seat_number = 'A02' and ts.status = 'AVAILABLE'::${tripSchema}.trip_seat_status)::text,
  count(distinct trip_stop.stop_id) filter (where trip_stop.stop_id = '${seed.stopId}')::text,
  count(distinct trip_stop_fare.stop_id) filter (where trip_stop_fare.stop_id = '${seed.stopId}')::text
from ${tripSchema}.trips t
left join ${tripSchema}.trip_seats ts on ts.trip_id = t.id
left join ${tripSchema}.trip_stops trip_stop on trip_stop.trip_id = t.id
left join ${tripSchema}.trip_stop_fares trip_stop_fare on trip_stop_fare.trip_id = t.id
where t.driver_schedule_id = '${seed.driverScheduleId}'
  and t.operator_id = '${seed.operatorId}'
  and t.route_id = '${seed.routeId}'
  and t.departure_date_time >= '${day11DepartureStart}'
  and t.departure_date_time < '${day11DepartureEnd}'
  and t.status in ('SCHEDULED'::${tripSchema}.trip_status, 'BOARDING'::${tripSchema}.trip_status)
group by t.id, t.status;`,
    ),
  );

  return rows.map(
    ([
      tripId,
      status,
      seatCount,
      a01Available,
      a02Available,
      tripStopsCount,
      tripStopFaresCount,
    ]) => ({
      tripId,
      status,
      seatCount: Number(seatCount || 0),
      a01Available: Number(a01Available || 0),
      a02Available: Number(a02Available || 0),
      tripStopsCount: Number(tripStopsCount || 0),
      tripStopFaresCount: Number(tripStopFaresCount || 0),
    }),
  );
}

function loadSeatStatuses(tripId) {
  const rows = parsePipeRows(
    querySql(
      process.env.TRIP_DB || 'vietride_trip',
      `select seat_number, status::text
from ${tripSchema}.trip_seats
where trip_id = '${tripId}'
  and seat_number in ('A01', 'A02')
order by seat_number;`,
    ),
  );

  return rows.reduce((acc, [seatNumber, status]) => ({ ...acc, [seatNumber]: status }), {});
}

async function waitForGeneratedTripEvidence() {
  const timeoutMs = 30000;
  const startedAt = Date.now();
  let lastCount = 0;
  let lastEvidence = [];

  while (Date.now() - startedAt < timeoutMs) {
    lastCount = loadGenerationCount();
    lastEvidence = loadGeneratedTripEvidence();
    if (
      lastCount === 1 &&
      lastEvidence.length === 1 &&
      lastEvidence[0].seatCount >= 2 &&
      lastEvidence[0].a01Available >= 1 &&
      lastEvidence[0].a02Available >= 1 &&
      lastEvidence[0].tripStopsCount >= 1
    ) {
      return lastEvidence[0];
    }
    await sleep(1500);
  }

  throw new Error(
    `Day-11 generated trip was not ready within ${timeoutMs}ms. count=${lastCount}; evidence=${JSON.stringify(lastEvidence)}`,
  );
}

async function tripInternalRequest(method, tripId, pathSuffix, body, headers = {}) {
  if (!process.env.INTERNAL_JWT_SECRET)
    throw new Error('INTERNAL_JWT_SECRET is required to call Trip internal endpoints.');

  const token = createInternalJwt(process.env.INTERNAL_JWT_SECRET, {
    sub: seed.operatorAdminUserId,
    role: 'OPERATOR_ADMIN',
  });
  const url = `${tripServiceBaseUrl.replace(/\/$/, '')}/internal/v1/trips/${tripId}${pathSuffix}`;

  return fetchJson(url, {
    method,
    headers: {
      'X-Internal-Auth': `Bearer ${token}`,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body ? JSON.stringify(body) : undefined,
  });
}

function expectSeatStatus(tripId, seatNumber, expected) {
  const seatStatuses = loadSeatStatuses(tripId);
  if (seatStatuses[seatNumber] !== expected) {
    throw new Error(
      `Day-11 expected ${seatNumber}=${expected}, got ${JSON.stringify(seatStatuses)}`,
    );
  }
  return seatStatuses;
}

async function runInternalSeamFlow(generatedTripId) {
  const lockBody = { seatNumbers: ['A01'], holdOwnerId: seed.passengerUserId, ttlSeconds: 600 };
  const bookingId = toUuidFromHash(`day11-booking-${runId}`);

  const lock1 = await tripInternalRequest('POST', generatedTripId, '/lock-seats', lockBody, {
    'Idempotency-Key': `day11-lock-1-${normalizeUuid(runId)}`,
  });
  if (lock1.response.status !== 200 || lock1.json?.success !== true)
    throw new Error(`Day-11 lock-seats #1 failed: ${lock1.response.status} ${lock1.text}`);
  const seatLockToken1 = lock1.json.data?.seatLockToken;
  const expiresAt1 = lock1.json.data?.expiresAt;
  if (!seatLockToken1 || !expiresAt1)
    throw new Error(`Day-11 lock-seats #1 missing token/expiresAt: ${lock1.text}`);
  expectSeatStatus(generatedTripId, 'A01', 'HELD');

  const release1 = await tripInternalRequest('POST', generatedTripId, '/release-seats', {
    seatLockToken: seatLockToken1,
    seatNumbers: ['A01'],
  });
  if (release1.response.status !== 204)
    throw new Error(`Day-11 release-seats failed: ${release1.response.status} ${release1.text}`);
  expectSeatStatus(generatedTripId, 'A01', 'AVAILABLE');

  const lock2 = await tripInternalRequest('POST', generatedTripId, '/lock-seats', lockBody, {
    'Idempotency-Key': `day11-lock-2-${normalizeUuid(runId)}`,
  });
  if (lock2.response.status !== 200 || lock2.json?.success !== true)
    throw new Error(`Day-11 lock-seats #2 failed: ${lock2.response.status} ${lock2.text}`);
  const seatLockToken2 = lock2.json.data?.seatLockToken;
  if (!seatLockToken2) throw new Error(`Day-11 lock-seats #2 missing token: ${lock2.text}`);
  expectSeatStatus(generatedTripId, 'A01', 'HELD');

  const book1 = await tripInternalRequest('POST', generatedTripId, '/book-seats', {
    seatLockToken: seatLockToken2,
    bookingId,
    passengerSeatAssignments: [{ passengerId: seed.passengerUserId, seatNumber: 'A01' }],
  });
  if (book1.response.status !== 204)
    throw new Error(`Day-11 book-seats failed: ${book1.response.status} ${book1.text}`);
  expectSeatStatus(generatedTripId, 'A01', 'BOOKED');

  const lock3 = await tripInternalRequest('POST', generatedTripId, '/lock-seats', lockBody, {
    'Idempotency-Key': `day11-lock-3-${normalizeUuid(runId)}`,
  });
  const lock3Code = lock3.json?.error?.code || lock3.json?.code || null;
  if (lock3.response.status !== 409 || lock3Code !== 'BOOKING_SEAT_UNAVAILABLE') {
    throw new Error(
      `Day-11 lock-seats #3 expected 409 BOOKING_SEAT_UNAVAILABLE, got ${lock3.response.status}: ${lock3.text}`,
    );
  }

  const finalSeatStatuses = loadSeatStatuses(generatedTripId);
  if (finalSeatStatuses.A01 !== 'BOOKED' || finalSeatStatuses.A02 !== 'AVAILABLE') {
    throw new Error(`Day-11 final seat state unexpected: ${JSON.stringify(finalSeatStatuses)}`);
  }

  console.log('Day-11 internal seam evidence:');
  console.log(
    [
      `tripId=${generatedTripId}`,
      `lock1.token=${redactValue(seatLockToken1)}`,
      `lock1.expiresAt=${expiresAt1}`,
      'release1=204',
      `lock2.token=${redactValue(seatLockToken2)}`,
      `book1=204 bookingId=${bookingId}`,
      `lock3.status=${lock3.response.status}`,
      `lock3.code=${lock3Code}`,
      `seatStates=${JSON.stringify(finalSeatStatuses)}`,
    ].join('; '),
  );
}

async function main() {
  if (process.env.DAY11_CLEANUP_ONLY === 'true') {
    cleanupFixtures();
    console.log('PASS | D11 fixture cleanup | deterministic Identity and Trip fixtures removed');
    return;
  }

  const retainFixtures = process.env.DAY11_RETAIN_FIXTURES === 'true';
  let runError;
  try {
  ensureCurrentRealSeamImages();
  await waitForContainerHealthy('vietride_identity');
  await waitForContainerHealthy('vietride_trip');
  await verifyIdentityQuotaAllocationRoute();
  cleanupFixtures();
  seedIdentity();
  seedTripConfig();

  const [operatorAdminAccessToken, passengerAccessToken] = await Promise.all([
    issueUserToken({
      sub: seed.operatorAdminUserId,
      role: 'OPERATOR_ADMIN',
      email: 'day11-operator-admin@example.test',
      operatorId: seed.operatorId,
    }),
    issueUserToken({
      sub: seed.passengerUserId,
      role: 'PASSENGER',
      email: 'day11-passenger@example.test',
    }),
  ]);

  await activateDriverScheduleThroughGateway(operatorAdminAccessToken);
  const generatedTrip = await waitForGeneratedTripEvidence();
  console.log('Day-11 generation evidence:');
  console.log(
    `tripId=${generatedTrip.tripId}, status=${generatedTrip.status}, schedule=${seed.driverScheduleId}, seats=A01/A02 available, tripStops=${generatedTrip.tripStopsCount}, legacyTripStopFares=${generatedTrip.tripStopFaresCount} (informational)`,
  );

  console.log(
    'Day-11 local Newman harness seeded deterministic prerequisite Identity/Trip config data.',
  );
  if (process.env.DAY11_FORCE_NEWMAN_FAILURE === 'true')
    throw new Error('Forced Day-11 Newman failure requested');
  const exitCode = await runNewman({
    operatorAdminAccessToken,
    passengerAccessToken,
    day11DriverScheduleId: seed.driverScheduleId,
    day11OriginStationId: seed.originStationId,
    day11DestinationStationId: seed.destinationStationId,
    day11MissingStationId: '00000000-0000-0000-0000-000000000011',
    day11DepartureDate,
  });

  if (exitCode === 0 && process.env.DAY11_SKIP_INTERNAL_SEAM !== 'true') {
    await runInternalSeamFlow(generatedTrip.tripId);
  }

  if (exitCode !== 0) throw new Error(`Newman failed with status ${exitCode}`);
  } catch (error) {
    runError = error;
  } finally {
    if (retainFixtures) {
      console.log('PASS | D11 fixture provision | cleanup ownership transferred to the calling harness');
    } else {
      cleanupFixtures();
      console.log('PASS | D11 fixture cleanup | deterministic Identity and Trip fixtures removed');
    }
  }
  if (runError) throw runError;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
