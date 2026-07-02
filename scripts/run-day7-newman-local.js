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

const harnessPort = Number(process.env.DAY7_HARNESS_PORT || 3057);
const identitySchema = 'vietride_identity';
const tripSchema = 'vietride_trip';

const seed = {
  approvedOperatorId: '10000000-0000-0000-0000-000000000007',
  approvedAdminUserId: '10000000-0000-0000-0000-0000000000a7',
  approvedStaffUserId: '10000000-0000-0000-0000-0000000000b7',
  nonApprovedOperatorId: '20000000-0000-0000-0000-000000000007',
  nonApprovedAdminUserId: '20000000-0000-0000-0000-0000000000a7',
  mienTayStationId: '30000000-0000-0000-0000-000000000007',
  missingStationId: '30000000-0000-0000-0000-0000000000f7',
  crossOperatorStopId: '40000000-0000-0000-0000-000000000007',
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
  ('${seed.approvedOperatorId}', 'Day 7 Local Approved Operator', 'DAY7-APPROVED-BRN', 'DAY7-APPROVED-TAX',
   'day7-approved-operator@example.test', '+84907000001',
   'APPROVED'::operator_registration_status, true, 'Day 7 Approved Rep', '+84907000002'),
  ('${seed.nonApprovedOperatorId}', 'Day 7 Local Pending Operator', 'DAY7-PENDING-BRN', 'DAY7-PENDING-TAX',
   'day7-pending-operator@example.test', '+84907000003',
   'PENDING'::operator_registration_status, true, 'Day 7 Pending Rep', '+84907000004')
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
  ('${seed.approvedAdminUserId}', 'day7-approved-admin@example.test', '+84907000011', null,
   'Day 7 Approved Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.approvedStaffUserId}', 'day7-approved-staff@example.test', '+84907000012', null,
   'Day 7 Approved Operator Staff', 'OPERATOR_STAFF'::user_role, 'ACTIVE'::user_status, '${seed.approvedOperatorId}'),
  ('${seed.nonApprovedAdminUserId}', 'day7-pending-admin@example.test', '+84907000013', null,
   'Day 7 Pending Operator Admin', 'OPERATOR_ADMIN'::user_role, 'ACTIVE'::user_status, '${seed.nonApprovedOperatorId}')
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

delete from ${tripSchema}.operator_stations
 where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    or station_id in (
      select id from ${tripSchema}.stations where name like 'Bến xe Miền Tây Day 7%'
    );

delete from ${tripSchema}.stops
 where operator_id in ('${seed.approvedOperatorId}', '${seed.nonApprovedOperatorId}')
    or name like 'Day 7 Stop%'
    or name = 'Blocked Stop';

delete from ${tripSchema}.stations
 where name like 'Bến xe Miền Tây Day 7%';

insert into ${tripSchema}.stations (
  id, name, slug, address_street, city, province, latitude, longitude,
  contact_phone, contact_email, operating_hours, facilities, supports_shuttle, is_active
)
values (
  '${seed.mienTayStationId}', 'Bến xe Miền Tây', 'ben-xe-mien-tay-day7-local', '395 Kinh Dương Vương',
  'Ho Chi Minh City', 'Ho Chi Minh', 10.7212345, 106.6267890,
  '02837650601', 'mien-tay-day7@example.test', '{"mon":"05:00-22:00"}'::jsonb,
  '["waiting_room","parking"]'::jsonb, true, true
)
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

insert into ${tripSchema}.stops (
  id, operator_id, name, description, latitude, longitude, address, google_place_id, is_active
)
values (
  '${seed.crossOperatorStopId}', '${seed.nonApprovedOperatorId}', 'Day 7 Cross Operator Stop',
  'Owned by the non-approved local harness operator', 10.7000000, 106.6000000,
  'Cross operator address', 'day7-cross-operator-place', true
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
    'Trip — Day 7 station/stop flow',
    '--env-var',
    'localHarnessEnabled=true',
    '--env-var',
    `localHarnessUrl=http://127.0.0.1:${harnessPort}`,
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
        email: 'day7-approved-admin@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.approvedStaffUserId,
        role: 'OPERATOR_STAFF',
        email: 'day7-approved-staff@example.test',
        operatorId: seed.approvedOperatorId,
      }),
      issueUserToken({
        sub: seed.nonApprovedAdminUserId,
        role: 'OPERATOR_ADMIN',
        email: 'day7-pending-admin@example.test',
        operatorId: seed.nonApprovedOperatorId,
      }),
    ]);

  console.log('Day-7 local Newman harness seeded deterministic local Identity/Trip data.');
  const exitCode = await runNewman({
    adminCreatedOperatorAccessToken: operatorAdminAccessToken,
    operatorUserAccessToken,
    nonApprovedOperatorAccessToken,
    operatorId: seed.approvedOperatorId,
    stationSearchResultId: seed.mienTayStationId,
    missingStationId: seed.missingStationId,
    crossOperatorStopId: seed.crossOperatorStopId,
  });
  process.exitCode = exitCode;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
