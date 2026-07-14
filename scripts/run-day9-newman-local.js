const childProcess = require('node:child_process');
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

const seed = {
  approvedOperatorId: '10000000-0000-0000-0000-000000000009',
  approvedAdminUserId: '10000000-0000-0000-0000-0000000000a9',
  approvedStaffUserId: '10000000-0000-0000-0000-0000000000b9',
  otherOperatorId: '20000000-0000-0000-0000-000000000009',
  otherAdminUserId: '20000000-0000-0000-0000-0000000000a9',
  originStationId: '30000000-0000-0000-0000-000000000009',
  destinationStationId: '30000000-0000-0000-0000-000000000019',
  routeId: '50000000-0000-0000-0000-000000000009',
  crossOperatorVehicleId: '60000000-0000-0000-0000-000000000009',
  standardVehicleTypeId: '00000000-0000-0000-0000-000000000101',
  subscriptionPlanId: '00000000-0000-0000-0000-000000000109',
  subscriptionId: '90000000-0000-0000-0000-000000000009',
  unknownVehicleTypeId: '00000000-0000-0000-0000-000000009999',
  driverUserId: '70000000-0000-0000-0000-000000000009',
  assistantUserId: '70000000-0000-0000-0000-000000000019',
};

function dockerArgs(database) {
  return [
    'exec',
    '-i',
    process.env.POSTGRES_CONTAINER || 'vietride_postgres',
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
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

  if (child.error) {
    throw child.error;
  }

  if (child.status !== 0) {
    throw new Error(`psql failed for database ${database} with exit code ${child.status}`);
  }
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

  return new SignJWT({
    role,
    email,
    hasPhone: 'true',
    operatorId,
  })
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
values
  ('${seed.approvedOperatorId}', 'Day 9 Local Approved Operator', 'DAY9-APPROVED-BRN', 'DAY9-APPROVED-TAX',
   'day9-approved-operator@example.test', '+84909000001',
   'APPROVED'::operator_registration_status, true, 'Day 9 Approved Rep', '+84909000002'),
  ('${seed.otherOperatorId}', 'Day 9 Local Other Operator', 'DAY9-OTHER-BRN', 'DAY9-OTHER-TAX',
   'day9-other-operator@example.test', '+84909000003',
   'APPROVED'::operator_registration_status, true, 'Day 9 Other Rep', '+84909000004')
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
  ('${seed.approvedAdminUserId}', 'day9-approved-admin@example.test', '+84909000011', null,
   'Day 9 Approved Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.approvedStaffUserId}', 'day9-approved-staff@example.test', '+84909000012', null,
   'Day 9 Approved Operator Staff', 'OPERATOR_STAFF'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.driverUserId}', 'day9-driver@example.test', '+84909000014', null,
   'Day 9 Driver', 'DRIVER'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.assistantUserId}', 'day9-assistant@example.test', '+84909000015', null,
   'Day 9 Assistant', 'ASSISTANT'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.otherAdminUserId}', 'day9-other-admin@example.test', '+84909000013', null,
   'Day 9 Other Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.otherOperatorId}')
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
  max_assistants, max_operator_users, max_routes, max_trips_per_month, enable_shuttle, is_active
)
values (
  '${seed.subscriptionPlanId}', 'Day 9 deterministic local plan', 'Harness-only quota fixture',
  0, 0, 100, 100, 100, 100, 100, 100, true, true
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
  enable_shuttle = true,
  is_active = true,
  updated_at = now();

insert into ${identitySchema}.operator_subscriptions (
  id, operator_id, plan_id, status, started_at, expires_at
)
values (
  '${seed.subscriptionId}', '${seed.approvedOperatorId}', '${seed.subscriptionPlanId}',
  'ACTIVE'::subscription_status, now() - interval '1 day', now() + interval '30 days'
)
on conflict (operator_id) do update set
  id = excluded.id,
  plan_id = excluded.plan_id,
  status = excluded.status,
  started_at = excluded.started_at,
  expires_at = excluded.expires_at,
  current_vehicles = 0,
  current_routes = 0,
  current_trips_this_month = 0,
  updated_at = now();

commit;
`,
  );
}

function seedTrip() {
  runSql(
    process.env.TRIP_DB || 'vietride_trip',
    `begin;

create extension if not exists pgcrypto;

delete from ${tripSchema}.trips
 where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
    or vehicle_id in (
      select id from ${tripSchema}.vehicles
       where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
          or license_plate like 'DAY9-%'
    );

delete from ${tripSchema}.driver_schedules
 where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
    or driver_user_id in ('${seed.driverUserId}', '${seed.assistantUserId}');

delete from ${tripSchema}.vehicles
 where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
    or license_plate like 'DAY9-%';

delete from ${tripSchema}.routes
 where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
    or id = '${seed.routeId}'
    or name like 'Day 9 %';

delete from ${tripSchema}.operator_stations
 where operator_id in ('${seed.approvedOperatorId}', '${seed.otherOperatorId}')
    or station_id in ('${seed.originStationId}', '${seed.destinationStationId}');

delete from ${tripSchema}.stations
 where id in ('${seed.originStationId}', '${seed.destinationStationId}');

insert into ${tripSchema}.stations (
  id, name, slug, address_street, city, province, latitude, longitude,
  contact_phone, contact_email, operating_hours, facilities, supports_shuttle, is_active
)
values
  ('${seed.originStationId}', 'Day 9 Origin Station', 'day9-origin-station', '1 Day 9 Origin',
   'Ho Chi Minh City', 'Ho Chi Minh', 10.7212345, 106.6267890,
   '02890000001', 'day9-origin@example.test', '{"mon":"05:00-22:00"}'::jsonb,
   '["waiting_room"]'::jsonb, true, true),
  ('${seed.destinationStationId}', 'Day 9 Destination Station', 'day9-destination-station', '2 Day 9 Destination',
   'Da Lat', 'Lam Dong', 11.9404192, 108.4583132,
   '02639000002', 'day9-destination@example.test', '{"mon":"05:00-22:00"}'::jsonb,
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
  ('${seed.approvedOperatorId}', '${seed.originStationId}', true),
  ('${seed.approvedOperatorId}', '${seed.destinationStationId}', true),
  ('${seed.otherOperatorId}', '${seed.originStationId}', true),
  ('${seed.otherOperatorId}', '${seed.destinationStationId}', true)
on conflict (operator_id, station_id) do update set
  is_active = excluded.is_active,
  updated_at = now();

insert into ${tripSchema}.routes (
  id, operator_id, name, origin_station_id, destination_station_id, return_route_id,
  base_fare, total_distance_km, estimated_duration_minutes, is_active, deleted_at
)
values (
  '${seed.routeId}', '${seed.approvedOperatorId}', 'Day 9 Driver Schedule Route',
  '${seed.originStationId}', '${seed.destinationStationId}', null,
  125000, 310.50, 420, true, null
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

insert into ${tripSchema}.vehicles (
  id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats,
  max_cargo_weight_kg, max_cargo_volume_m3, status, is_active, deleted_at
)
values (
  '${seed.crossOperatorVehicleId}', '${seed.otherOperatorId}', '${seed.standardVehicleTypeId}', 'DAY9-CROSS-01',
  '{"version":1,"vehicleTypeCode":"STANDARD_BUS","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A1","row":1,"col":1,"deck":1,"type":"STANDARD","isWindow":true,"isAisle":false,"disabled":false}]}'::jsonb,
  1, 100.00, 1.00, 'ACTIVE'::vehicle_status, true, null
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

commit;
`,
  );
}

function quoteCommandArg(value) {
  return `"${String(value).replace(/"/g, '""')}"`;
}

async function runNewman(envVars) {
  const args = [
    'newman',
    'run',
    collectionPath,
    '-e',
    environmentPath,
    '--folder',
    'Trip — Day 9 vehicle + driver-schedule flow',
  ];

  for (const [key, value] of Object.entries(envVars)) {
    args.push('--env-var', `${key}=${value}`);
  }

  if (process.platform !== 'win32') {
    const child = childProcess.spawn('npx', args, { stdio: 'inherit' });
    return new Promise((resolve) => {
      child.on('exit', (code) => resolve(code || 0));
    });
  }

  const npxCmd = fs.existsSync('C:\\Program Files\\nodejs\\npx.cmd')
    ? 'C:\\Program Files\\nodejs\\npx.cmd'
    : 'npx.cmd';
  const command = [npxCmd, ...args].map(quoteCommandArg).join(' ');
  const child = childProcess.exec(command, { maxBuffer: 20 * 1024 * 1024 });
  child.stdout?.pipe(process.stdout);
  child.stderr?.pipe(process.stderr);

  return new Promise((resolve) => {
    child.on('exit', (code) => resolve(code || 0));
  });
}

async function main() {
  seedIdentity();
  seedTrip();

  const [operatorAdminAccessToken, operatorUserAccessToken, otherOperatorAccessToken] =
    await Promise.all([
      issueUserToken({
        sub: seed.approvedAdminUserId,
        role: 'OPERATOR_ADMIN',
        email: 'day9-approved-admin@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.approvedStaffUserId,
        role: 'OPERATOR_STAFF',
        email: 'day9-approved-staff@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.otherAdminUserId,
        role: 'OPERATOR_ADMIN',
        email: 'day9-other-admin@example.test',
        operatorId: seed.otherOperatorId,
      }),
    ]);

  console.log('Day-9 local Newman harness seeded deterministic local Identity/Trip data.');
  const exitCode = await runNewman({
    operatorAdminAccessToken,
    operatorUserAccessToken,
    day9OtherOperatorAccessToken: otherOperatorAccessToken,
    operatorId: seed.approvedOperatorId,
    day9RouteId: seed.routeId,
    day9CrossOperatorVehicleId: seed.crossOperatorVehicleId,
    day9StandardVehicleTypeId: seed.standardVehicleTypeId,
    day9UnknownVehicleTypeId: seed.unknownVehicleTypeId,
    day9DriverUserId: seed.driverUserId,
    day9AssistantUserId: seed.assistantUserId,
  });
  process.exitCode = exitCode;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
