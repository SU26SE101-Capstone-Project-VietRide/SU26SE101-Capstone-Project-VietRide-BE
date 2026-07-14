// Day-12 deterministic lock regression. The Day-11 harness owns the generated Trip
// fixture and proves the Trip lock protocol; this runner executes the dedicated
// Gateway checkout assertions. Child harnesses clean their fixtures in finally.
import { execFileSync, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import dotenv from 'dotenv';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
dotenv.config({ path: path.join(root, '.env') });
const folder = 'Booking - Day 12 seat lock flow';
const passengerId = '91000000-0000-0000-0000-000000000012';
const competingPassengerId = '92000000-0000-4000-8000-000000000012';
const lockSeatNumbers = ['A07', 'A08', 'A09', 'A10', 'A11'];
let previousBookingMode;

function run(label, command, args, env = process.env) {
  const useNpxCli = command === 'npx' && process.platform === 'win32';
  const npxCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js');
  const result = spawnSync(useNpxCli ? process.execPath : command, useNpxCli ? [npxCli, ...args] : args, {
    cwd: root,
    env,
    // Passing the argument array directly preserves JSON and generated JWTs.
    shell: false,
    stdio: 'inherit',
  });
  if (result.error || result.status !== 0) throw new Error(`${label} failed (status=${result.status ?? 1})`);
}

async function startRealSeams() {
  const env = {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: 'false',
    BOOKING_PAYMENT_USE_DEV_STUB: 'false',
    BOOKING_IDENTITY_USE_DEV_STUB: 'false',
  };
  const result = spawnSync('docker', [
    'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app',
    'up', '-d', '--force-recreate', 'booking', 'gateway',
  ], { cwd: root, env, stdio: 'inherit' });
  if (result.error || result.status !== 0) throw new Error('Could not start Day-12 real Booking seams.');

  for (let attempt = 0; attempt < 30; attempt += 1) {
    try {
      const response = await fetch('http://localhost:3000/health');
      if (response.ok) return;
    } catch {
      // Gateway is still starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error('Day-12 real Gateway seam did not become healthy.');
}

function readBookingMode() {
  const environment = execFileSync(
    'docker',
    ['inspect', '--format', '{{range .Config.Env}}{{println .}}{{end}}', 'vietride_booking'],
    { encoding: 'utf8' },
  );
  const values = Object.fromEntries(environment.split(/\r?\n/).filter(Boolean).map((entry) => {
    const separator = entry.indexOf('=');
    return [entry.slice(0, separator), entry.slice(separator + 1)];
  }));
  return values.BOOKING_TRIP_USE_DEV_STUB === 'true' ? 'stub' : 'real';
}

function createInternalJwt(secret) {
  if (!secret || secret.length < 32)
    throw new Error('INTERNAL_JWT_SECRET must be available to the D12 Trip seam.');
  const now = Math.floor(Date.now() / 1000);
  const encode = (value) => Buffer.from(JSON.stringify(value)).toString('base64url');
  const header = encode({ alg: 'HS256', typ: 'JWT' });
  const payload = encode({
    iss: 'vietride-gateway', aud: 'vietride-internal', iat: now, exp: now + 120,
    sub: passengerId, role: 'PASSENGER',
  });
  const signature = crypto.createHmac('sha256', secret).update(`${header}.${payload}`).digest('base64url');
  return `${header}.${payload}.${signature}`;
}

async function tripInternalRequest(tripId, pathSuffix, body, idempotencyKey) {
  const response = await fetch(`http://localhost:5002/internal/v1/trips/${tripId}${pathSuffix}`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${createInternalJwt(process.env.INTERNAL_JWT_SECRET)}`,
      'Content-Type': 'application/json',
      ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
    },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  let json;
  try { json = text ? JSON.parse(text) : undefined; } catch { json = undefined; }
  return { response, json };
}

function expectSeatStatuses(tripId, expectedStatus) {
  const output = execFileSync('docker', [
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', 'vietride_trip', '-At', '-F', '|', '-c',
    `SELECT seat_number, status FROM vietride_trip.trip_seats WHERE trip_id = '${tripId}' AND seat_number IN (${lockSeatNumbers.map((seat) => `'${seat}'`).join(', ')}) ORDER BY seat_number;`,
  ], { encoding: 'utf8' }).trim();
  const actual = Object.fromEntries(output.split(/\r?\n/).filter(Boolean).map((row) => row.split('|')));
  if (Object.keys(actual).length !== lockSeatNumbers.length || lockSeatNumbers.some((seat) => actual[seat] !== expectedStatus))
    throw new Error(`D12 expected ${lockSeatNumbers.join(',')} to be ${expectedStatus}, got ${JSON.stringify(actual)}.`);
}

async function proveAtomicLockLifecycle(tripId) {
  const held = await tripInternalRequest(tripId, '/lock-seats', {
    seatNumbers: lockSeatNumbers, holdOwnerId: passengerId, ttlSeconds: 1,
  }, `day12-hold-${crypto.randomUUID()}`);
  if (held.response.status !== 200 || held.json?.success !== true || !held.json?.data?.seatLockToken)
    throw new Error(`D12 five-seat atomic hold failed: HTTP ${held.response.status}.`);
  expectSeatStatuses(tripId, 'HELD');

  const competing = await tripInternalRequest(tripId, '/lock-seats', {
    seatNumbers: ['A07'], holdOwnerId: competingPassengerId, ttlSeconds: 60,
  }, `day12-competing-${crypto.randomUUID()}`);
  if (competing.response.status !== 409 || competing.json?.error?.code !== 'BOOKING_SEAT_UNAVAILABLE')
    throw new Error(`D12 competing holder was not rejected: HTTP ${competing.response.status}.`);

  await new Promise((resolve) => setTimeout(resolve, 1_500));
  const released = await tripInternalRequest(tripId, '/lock-seats', {
    seatNumbers: lockSeatNumbers, holdOwnerId: passengerId, ttlSeconds: 60,
  }, `day12-after-ttl-${crypto.randomUUID()}`);
  const lockToken = released.json?.data?.seatLockToken;
  if (released.response.status !== 200 || released.json?.success !== true || !lockToken)
    throw new Error(`D12 TTL release did not make the full set available: HTTP ${released.response.status}.`);
  expectSeatStatuses(tripId, 'HELD');

  const booked = await tripInternalRequest(tripId, '/book-seats', {
    seatLockToken: lockToken,
    bookingId: crypto.randomUUID(),
    passengerSeatAssignments: lockSeatNumbers.map((seatNumber) => ({ passengerId, seatNumber })),
  });
  if (booked.response.status !== 204)
    throw new Error(`D12 HELD-to-BOOKED transition failed: HTTP ${booked.response.status}.`);
  expectSeatStatuses(tripId, 'BOOKED');
  console.log('PASS | D12 | five-seat atomic hold, competing rejection, TTL release, HELD-to-BOOKED');
}

function runDay11Fixture(args = [], env = {}) {
  const result = spawnSync(process.execPath, ['scripts/run-day11-newman-local.js', ...args], {
    cwd: root,
    env: { ...process.env, ...env },
    stdio: 'inherit',
  });
  if (result.error || result.status !== 0) throw new Error(`Day-11 fixture ${args.join(' ')} failed (status=${result.status ?? 1})`);
}

function fixtureTripId() {
  return execFileSync(
    'docker',
    ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', 'vietride_trip', '-Atc', "SELECT id FROM vietride_trip.trips WHERE driver_schedule_id = '80000000-0000-0000-0000-000000000011' ORDER BY created_at DESC LIMIT 1"],
    { encoding: 'utf8' },
  ).trim();
}

function provisionWalletFixture(tripId) {
  execFileSync(
    'docker',
    [
      'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
      '-d', 'vietride_trip', '-c',
      `INSERT INTO vietride_trip.trip_seats (trip_id, seat_number) VALUES
         ('${tripId}', 'A03'), ('${tripId}', 'A04'), ('${tripId}', 'A05'),
         ('${tripId}', 'A06'), ('${tripId}', 'A07'), ('${tripId}', 'A08'),
         ('${tripId}', 'A09'), ('${tripId}', 'A10'), ('${tripId}', 'A11')
       ON CONFLICT (trip_id, seat_number) DO NOTHING;`,
    ],
    { stdio: 'inherit' },
  );
  execFileSync(
    'docker',
    [
      'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
      '-d', 'vietride_payment', '-c',
      `DELETE FROM vietride_payment.wallets WHERE user_id = '${passengerId}';
       INSERT INTO vietride_payment.wallets (user_id, balance, currency)
       VALUES ('${passengerId}', 1000000, 'VND');`,
    ],
    { stdio: 'inherit' },
  );
}

function cleanupOwnedFixtures() {
  execFileSync(
    'docker',
    [
      'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
      '-d', 'vietride_booking', '-c',
      `DELETE FROM vietride_booking.tickets WHERE booking_id IN
         (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.passengers WHERE booking_id IN
         (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN
         (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}';`,
    ],
    { stdio: 'inherit' },
  );
  execFileSync(
    'docker',
    [
      'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
      '-d', 'vietride_payment', '-c',
      `DELETE FROM vietride_payment.wallets WHERE user_id = '${passengerId}';`,
    ],
    { stdio: 'inherit' },
  );
}

async function issuePassengerToken() {
  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  const sign = (subject) => new SignJWT({ role: 'PASSENGER', email: `day12-${subject.slice(0, 8)}@local.test`, hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid }).setIssuer('vietride-identity').setAudience('vietride-api')
    .setSubject(subject).setIssuedAt().setExpirationTime('15m').sign(key);
  return Promise.all([sign(passengerId), sign('92000000-0000-4000-8000-000000000012')]);
}

let runError;
try {
  previousBookingMode = readBookingMode();
  await startRealSeams();
  // D12 owns this fixture lifecycle. Day-11 supplies the deterministic Trip
  // provisioning seam but retains nothing after this harness's finally cleanup.
  runDay11Fixture([], {
    DAY11_RETAIN_FIXTURES: 'true',
  });
  provisionWalletFixture(fixtureTripId());
  await proveAtomicLockLifecycle(fixtureTripId());
  if (process.env.DAY12_FORCE_NEWMAN_FAILURE === 'true') throw new Error('Forced Day-12 Newman failure requested');
  const [passengerAccessToken, day12CompetingPassengerToken] = await issuePassengerToken();
  const day12FiveReleasedSeats = JSON.stringify(['A02', 'A03', 'A04', 'A05', 'A06'].map((seatNumber, index) => ({
    seatNumber, passenger: {
      fullName: `Day 12 Passenger ${index + 1}`,
      phoneNumber: `09000000${index + 11}`,
      idNumber: `0792030010${index + 11}`,
    },
  })));
  run('Day-12 Gateway collection', 'npx', [
    '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
    '-e', 'docs/api/postman/vietride.local.postman_environment.json',
    '--folder', folder, '--reporters', 'cli',
    '--env-var', `passengerAccessToken=${passengerAccessToken}`,
    '--env-var', `day12CompetingPassengerToken=${day12CompetingPassengerToken}`,
    '--env-var', `day12TripId=${fixtureTripId()}`,
    '--env-var', 'day12PickupStationId=30000000-0000-0000-0000-000000000011',
    '--env-var', 'day12DropoffStationId=30000000-0000-0000-0000-000000000021',
    '--env-var', `day12FiveReleasedSeats=${day12FiveReleasedSeats}`,
    '--env-var', `day12CompetingIdempotencyKey=${crypto.randomUUID()}`,
    '--env-var', `day12ReleasedIdempotencyKey=${crypto.randomUUID()}`,
  ]);
  console.log('PASS | D12 Gateway checkout | released five-seat set confirmed together');
} catch (error) {
  runError = error;
  console.error(`FAIL | D12 | ${error.message}`);
} finally {
  try {
    cleanupOwnedFixtures();
    console.log('PASS | D12 payment and booking cleanup | deterministic passenger fixtures removed');
  } catch (cleanupError) {
    if (!runError) runError = cleanupError;
    else console.error(`FAIL | D12 payment and booking cleanup | ${cleanupError.message}`);
  }
  try {
    runDay11Fixture([], { DAY11_CLEANUP_ONLY: 'true' });
    console.log('PASS | D12 fixture cleanup | deterministic Trip fixture removed');
  } catch (cleanupError) {
    if (!runError) runError = cleanupError;
    else console.error(`FAIL | D12 fixture cleanup | ${cleanupError.message}`);
  }
  if (previousBookingMode === 'stub') {
    try {
      run('D12 restore Booking stub mode', 'docker', [
        'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app',
        'up', '-d', '--force-recreate', 'booking', 'gateway',
      ], {
        ...process.env,
        BOOKING_TRIP_USE_DEV_STUB: 'true',
        BOOKING_PAYMENT_USE_DEV_STUB: 'true',
        BOOKING_IDENTITY_USE_DEV_STUB: 'true',
      });
      console.log('PASS | D12 mode restore | stub');
    } catch (restoreError) {
      if (!runError) runError = restoreError;
      else console.error(`FAIL | D12 mode restore | ${restoreError.message}`);
    }
  } else if (previousBookingMode === 'real') {
    // The requested seam equals the captured mode, so no second recreation is needed.
    console.log('PASS | D12 mode restore | real');
  }
}
if (runError) throw runError;
