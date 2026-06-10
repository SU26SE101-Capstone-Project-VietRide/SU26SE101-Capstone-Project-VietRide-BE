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
  approvedOperatorId: '10000000-0000-0000-0000-000000000008',
  approvedAdminUserId: '10000000-0000-0000-0000-0000000000a8',
  approvedStaffUserId: '10000000-0000-0000-0000-0000000000b8',
  nonApprovedOperatorId: '20000000-0000-0000-0000-000000000008',
  nonApprovedAdminUserId: '20000000-0000-0000-0000-0000000000a8',
  originStationId: '30000000-0000-0000-0000-000000000008',
  destinationStationId: '30000000-0000-0000-0000-000000000018',
  alternativeDestinationStationId: '30000000-0000-0000-0000-000000000028',
  missingStationId: '00000000-0000-0000-0000-000000000008',
  approvedStopId: '40000000-0000-0000-0000-000000000008',
  secondApprovedStopId: '40000000-0000-0000-0000-000000000018',
  crossOperatorRouteId: '50000000-0000-0000-0000-000000000008',
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
  ('${seed.approvedOperatorId}', 'Day 8 Local Approved Operator', 'DAY8-APPROVED-BRN', 'DAY8-APPROVED-TAX',
   'day8-approved-operator@example.test', '+84908000001',
   'APPROVED'::operator_registration_status, true, 'Day 8 Approved Rep', '+84908000002'),
  ('${seed.nonApprovedOperatorId}', 'Day 8 Local Pending Operator', 'DAY8-PENDING-BRN', 'DAY8-PENDING-TAX',
   'day8-pending-operator@example.test', '+84908000003',
   'PENDING'::operator_registration_status, true, 'Day 8 Pending Rep', '+84908000004')
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
  ('${seed.approvedAdminUserId}', 'day8-approved-admin@example.test', '+84908000011', null,
   'Day 8 Approved Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.approvedStaffUserId}', 'day8-approved-staff@example.test', '+84908000012', null,
   'Day 8 Approved Operator Staff', 'OPERATOR_STAFF'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.nonApprovedAdminUserId}', 'day8-pending-admin@example.test', '+84908000013', null,
   'Day 8 Pending Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.nonApprovedOperatorId}')
on conflict (id) do update set
  email = excluded.email,
  phone = excluded.phone,
  display_name = excluded.display_name,
  role = excluded.role,
  status = excluded.status,
  operator_id = excluded.operator_id,
  deleted_at = null,
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

delete from ${tripSchema}.alternative_route_stops
 where alternative_route_id in (
   select id from ${tripSchema}.alternative_routes
    where route_id in (
      select id from ${tripSchema}.routes where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    )
 );

delete from ${tripSchema}.alternative_routes
 where route_id in (
   select id from ${tripSchema}.routes where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
 );

delete from ${tripSchema}.route_stop_fare_templates
 where route_id in (
   select id from ${tripSchema}.routes where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
 );

delete from ${tripSchema}.route_stops
 where route_id in (
   select id from ${tripSchema}.routes where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
 );

delete from ${tripSchema}.routes
 where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    or name like 'Day 8 %';

delete from ${tripSchema}.operator_stations
 where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    or station_id in ('${seed.originStationId}', '${seed.destinationStationId}', '${seed.alternativeDestinationStationId}');

delete from ${tripSchema}.stops
 where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    or id in ('${seed.approvedStopId}', '${seed.secondApprovedStopId}');

delete from ${tripSchema}.stations
 where id in ('${seed.originStationId}', '${seed.destinationStationId}', '${seed.alternativeDestinationStationId}');

insert into ${tripSchema}.stations (
  id, name, slug, address_street, city, province, latitude, longitude,
  contact_phone, contact_email, operating_hours, facilities, supports_shuttle, is_active
)
values
  ('${seed.originStationId}', 'Day 8 Origin Station', 'day8-origin-station', '1 Day 8 Origin',
   'Ho Chi Minh City', 'Ho Chi Minh', 10.7212345, 106.6267890,
   '02880000001', 'day8-origin@example.test', '{"mon":"05:00-22:00"}'::jsonb,
   '["waiting_room"]'::jsonb, true, true),
  ('${seed.destinationStationId}', 'Day 8 Destination Station', 'day8-destination-station', '2 Day 8 Destination',
   'Da Lat', 'Lam Dong', 11.9404192, 108.4583132,
   '02638000002', 'day8-destination@example.test', '{"mon":"05:00-22:00"}'::jsonb,
   '["waiting_room"]'::jsonb, true, true),
  ('${seed.alternativeDestinationStationId}', 'Day 8 Alternative Destination Station', 'day8-alternative-destination-station', '3 Day 8 Alternative',
   'Bao Loc', 'Lam Dong', 11.5479800, 107.8077200,
   '02638000003', 'day8-alt-destination@example.test', '{"mon":"05:00-22:00"}'::jsonb,
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
  ('${seed.approvedOperatorId}', '${seed.alternativeDestinationStationId}', true),
  ('${seed.nonApprovedOperatorId}', '${seed.originStationId}', true),
  ('${seed.nonApprovedOperatorId}', '${seed.destinationStationId}', true)
on conflict (operator_id, station_id) do update set
  is_active = excluded.is_active,
  updated_at = now();

insert into ${tripSchema}.stops (
  id, operator_id, name, description, latitude, longitude, address, google_place_id, is_active
)
values
  ('${seed.approvedStopId}', '${seed.approvedOperatorId}', 'Day 8 Main Route Stop',
   'Primary Day 8 stop used as a RouteStop', 10.9000000, 107.1000000,
   'Day 8 Stop 1 Address', 'day8-main-route-stop', true),
  ('${seed.secondApprovedStopId}', '${seed.approvedOperatorId}', 'Day 8 Secondary Stop',
   'Secondary Day 8 stop used for stopId-not-on-route and alternative routes', 11.1000000, 107.3000000,
   'Day 8 Stop 2 Address', 'day8-secondary-stop', true)
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
  '${seed.crossOperatorRouteId}', '${seed.nonApprovedOperatorId}', 'Day 8 Cross Operator Route',
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
    'Trip — Day 8 route flow',
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

  const [operatorAdminAccessToken, operatorUserAccessToken, nonApprovedOperatorAccessToken] =
    await Promise.all([
      issueUserToken({
        sub: seed.approvedAdminUserId,
        role: 'OPERATOR_ADMIN',
        email: 'day8-approved-admin@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.approvedStaffUserId,
        role: 'OPERATOR_STAFF',
        email: 'day8-approved-staff@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.nonApprovedAdminUserId,
        role: 'OPERATOR_ADMIN',
        email: 'day8-pending-admin@example.test',
        operatorId: seed.nonApprovedOperatorId,
      }),
    ]);

  console.log('Day-8 local Newman harness seeded deterministic local Identity/Trip data.');
  const exitCode = await runNewman({
    operatorAdminAccessToken,
    operatorUserAccessToken,
    nonApprovedOperatorAccessToken,
    operatorId: seed.approvedOperatorId,
    day8OriginStationId: seed.originStationId,
    day8DestinationStationId: seed.destinationStationId,
    day8AlternativeDestinationStationId: seed.alternativeDestinationStationId,
    day8MissingStationId: seed.missingStationId,
    day8StopId: seed.approvedStopId,
    day8SecondStopId: seed.secondApprovedStopId,
    day8CrossOperatorRouteId: seed.crossOperatorRouteId,
  });
  process.exitCode = exitCode;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
