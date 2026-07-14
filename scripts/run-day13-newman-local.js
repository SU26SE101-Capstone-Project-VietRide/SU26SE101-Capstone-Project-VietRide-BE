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
const compose = ['compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app'];

const seed = {
  passengerUserId: '13000000-0000-4000-8000-000000000013',
  passengerEmail: 'day13-passenger@example.test',
  tripId: '00000000-0000-4000-8000-000000000012',
  returnTripId: '00000000-0000-4000-8000-000000000113',
  pickupStationId: '44444444-4444-4444-8444-444444444444',
  dropoffStationId: '55555555-5555-4555-8555-555555555555',
};

function psql(sql) {
  return childProcess.execFileSync(
    'docker',
    ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', 'vietride_booking', '-Atc', sql],
    { encoding: 'utf8' },
  ).trim();
}

// The Day-13 collection creates only Booking-owned records for this fixed
// passenger. Delete dependent rows first, so cleanup is safe under the local
// FK graph and never reaches pre-existing records.
function cleanupFixtures() {
  psql(`DELETE FROM vietride_booking.tickets WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${seed.passengerUserId}');
DELETE FROM vietride_booking.passengers WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${seed.passengerUserId}');
DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${seed.passengerUserId}');
DELETE FROM vietride_booking.bookings WHERE passenger_user_id = '${seed.passengerUserId}';`);
}

function assertFixturesAbsent() {
  const count = psql(`SELECT count(*) FROM vietride_booking.bookings WHERE passenger_user_id = '${seed.passengerUserId}';`);
  if (count !== '0') throw new Error(`Day-13 fixture cleanup failed: booking rows=${count}`);
}

function readBookingMode() {
  const output = childProcess.execFileSync(
    'docker',
    ['inspect', '--format', '{{range .Config.Env}}{{println .}}{{end}}', 'vietride_booking'],
    { encoding: 'utf8' },
  );
  return output.split(/\r?\n/).some((value) => value === 'BOOKING_TRIP_USE_DEV_STUB=true') ? 'stub' : 'real';
}

function composeBookingMode(mode) {
  const stub = mode === 'stub' ? 'true' : 'false';
  const result = childProcess.spawnSync('docker', [...compose, 'up', '-d', '--force-recreate', 'booking', 'gateway'], {
    cwd: repoRoot,
    env: {
      ...process.env,
      BOOKING_TRIP_USE_DEV_STUB: stub,
      BOOKING_PAYMENT_USE_DEV_STUB: stub,
      BOOKING_IDENTITY_USE_DEV_STUB: stub,
    },
    stdio: 'inherit',
  });
  if (result.error || result.status !== 0)
    throw new Error(`Day-13 could not start Booking/Gateway in ${mode} mode.`);
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

async function issueUserToken({ sub, role, email }) {
  const { SignJWT, importPKCS8 } = await import('jose');
  const { privateKey, kid } = readDevJwtOptions();
  const key = await importPKCS8(privateKey, 'RS256');

  return new SignJWT({
    role,
    email,
    hasPhone: 'true',
  })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
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
    'Booking — Bookings',
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
  let runError;
  let previousBookingMode;
  try {
    previousBookingMode = readBookingMode();
    composeBookingMode('stub');
    cleanupFixtures();
    const accessToken = await issueUserToken({
      sub: seed.passengerUserId,
      role: 'PASSENGER',
      email: seed.passengerEmail,
    });

    console.log(
      'Day-13 local Newman harness issued deterministic PASSENGER token and DevTripServiceClient IDs.',
    );
    if (process.env.DAY13_FORCE_NEWMAN_FAILURE === 'true')
      throw new Error('Forced Day-13 Newman failure requested');
    const exitCode = await runNewman({
      accessToken,
      day12TripId: seed.tripId,
      day12PickupStationId: seed.pickupStationId,
      day12DropoffStationId: seed.dropoffStationId,
      day12AlternatePickupStationId: seed.pickupStationId,
      day12AlternateDropoffStationId: seed.dropoffStationId,
      day12ReturnTripId: seed.returnTripId,
      day12ReturnPickupStationId: seed.pickupStationId,
      day12ReturnDropoffStationId: seed.dropoffStationId,
    });
    if (exitCode !== 0) throw new Error(`Day-13 Newman failed with status ${exitCode}`);
  } catch (error) {
    runError = error;
  } finally {
    try {
      cleanupFixtures();
      assertFixturesAbsent();
      console.log('PASS | D13 fixture cleanup | deterministic booking fixtures removed');
    } catch (cleanupError) {
      if (!runError) runError = cleanupError;
      else console.error(`FAIL | D13 fixture cleanup | ${cleanupError.message}`);
    }
    try {
      if (previousBookingMode && previousBookingMode !== 'stub') composeBookingMode(previousBookingMode);
      if (previousBookingMode) console.log(`PASS | D13 mode restore | ${previousBookingMode}`);
    } catch (restoreError) {
      if (!runError) runError = restoreError;
      else console.error(`FAIL | D13 mode restore | ${restoreError.message}`);
    }
  }
  if (runError) throw runError;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
