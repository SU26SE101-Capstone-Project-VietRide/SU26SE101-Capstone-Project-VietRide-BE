// Day-16 payment regression. Day-15 owns the signed sandbox IPN fixture and Day-17
// owns cancellation/refund cleanup; this runner invokes the dedicated Gateway folder
// between them so both booking payment methods and refund behaviour are asserted.
import { execFileSync, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const folder = 'Payment - Day 16 booking payment + refund flow';
const passengerId = process.env.DAY20_PASSENGER_ID || '90000000-0000-0000-0000-000000000011';
const day11DriverScheduleId = '80000000-0000-0000-0000-000000000011';
const executionEnvironmentPath = path.join(
  os.tmpdir(),
  `vietride-day16-${process.pid}.postman-environment.json`,
);
let previousBookingMode;

function requireVnPayHashSecret() {
  const secret = process.env.VNPAY_HASH_SECRET;
  if (!secret) throw new Error('VNPAY_HASH_SECRET is required for the VNPay E2E harness.');
  return secret;
}

function run(label, command, args, env = process.env) {
  const useNpxCli = command === 'npx' && process.platform === 'win32';
  const npxCli = path.join(
    path.dirname(process.execPath),
    'node_modules',
    'npm',
    'bin',
    'npx-cli.js',
  );
  const result = spawnSync(
    useNpxCli ? process.execPath : command,
    useNpxCli ? [npxCli, ...args] : args,
    {
      cwd: root,
      env,
      // Passing the argument array directly preserves JSON and generated JWTs.
      shell: false,
      stdio: 'inherit',
    },
  );
  if (result.error || result.status !== 0)
    throw new Error(`${label} failed (status=${result.status ?? 1})`);
}

async function startRealSeams() {
  const env = {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: 'false',
    BOOKING_PAYMENT_USE_DEV_STUB: 'false',
    BOOKING_IDENTITY_USE_DEV_STUB: 'false',
  };
  const result = spawnSync(
    'docker',
    [
      'compose',
      '--env-file',
      '.env',
      '-f',
      'infra/docker/docker-compose.yml',
      '--profile',
      'app',
      'build',
      'identity',
      'trip',
      'booking',
    ],
    { cwd: root, env, stdio: 'inherit' },
  );
  if (result.error || result.status !== 0)
    throw new Error(
      'Could not build current Identity, Trip, and Booking images for Day-16 real seams.',
    );

  const up = spawnSync(
    'docker',
    [
      'compose',
      '--env-file',
      '.env',
      '-f',
      'infra/docker/docker-compose.yml',
      '--profile',
      'app',
      'up',
      '-d',
      '--force-recreate',
      'identity',
      'trip',
      'booking',
      'gateway',
    ],
    { cwd: root, env, stdio: 'inherit' },
  );
  if (up.error || up.status !== 0) throw new Error('Could not start Day-16 real Booking seams.');

  for (let attempt = 0; attempt < 30; attempt += 1) {
    try {
      const health = execFileSync(
        'docker',
        ['inspect', '-f', '{{.State.Health.Status}}', 'vietride_gateway'],
        { encoding: 'utf8' },
      ).trim();
      if (health === 'healthy') return;
    } catch {
      // Gateway is still starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error('Day-16 real Gateway seam did not become healthy.');
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

function restoreBookingMode(mode) {
  if (mode !== 'stub') return;
  run('D16 restore Booking stub mode', 'docker', [
    'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app',
    'up', '-d', '--force-recreate', 'booking', 'gateway',
  ], {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: 'true',
    BOOKING_PAYMENT_USE_DEV_STUB: 'true',
    BOOKING_IDENTITY_USE_DEV_STUB: 'true',
  });
}

async function issuePassengerToken() {
  if (process.env.DAY20_PASSENGER_ACCESS_TOKEN) return process.env.DAY20_PASSENGER_ACCESS_TOKEN;
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const key = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  return new SignJWT({ role: 'PASSENGER', email: 'day16@local.test', hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(passengerId)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

async function refreshDay20PassengerToken() {
  if (!process.env.DAY20_PASSENGER_EMAIL || !process.env.DAY20_PASSENGER_PASSWORD)
    return process.env.DAY20_PASSENGER_ACCESS_TOKEN;
  const response = await fetch('http://localhost:3000/v1/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      email: process.env.DAY20_PASSENGER_EMAIL,
      password: process.env.DAY20_PASSENGER_PASSWORD,
    }),
  });
  const body = await response.json().catch(() => undefined);
  if (response.status !== 200 || body?.success !== true || !body?.data?.accessToken)
    throw new Error(`Day-20 passenger re-login after D16 seam recreate failed: HTTP ${response.status}.`);
  process.env.DAY20_PASSENGER_ACCESS_TOKEN = body.data.accessToken;
  console.log('PASS | D16 Day20 passenger auth refresh | Gateway login after seam recreate');
  return body.data.accessToken;
}

function runDay11Fixture(args = [], env = {}) {
  run(
    'Day-11 deterministic Trip fixture',
    process.execPath,
    ['scripts/run-day11-newman-local.js', ...args],
    {
      ...process.env,
      ...env,
    },
  );
}

function fixtureTripId() {
  return execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_trip',
      '-Atc',
      `SELECT id FROM vietride_trip.trips WHERE driver_schedule_id = '${day11DriverScheduleId}' ORDER BY created_at DESC LIMIT 1`,
    ],
    { encoding: 'utf8' },
  ).trim();
}

function provisionPaymentAndSeatFixtures(tripId) {
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_trip',
      '-c',
      `INSERT INTO vietride_trip.trip_seats (trip_id, seat_number) VALUES ('${tripId}', 'A03') ON CONFLICT (trip_id, seat_number) DO NOTHING;`,
    ],
    { stdio: 'inherit' },
  );
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_payment',
      '-c',
      `DELETE FROM vietride_payment.wallets WHERE user_id = '${passengerId}'; INSERT INTO vietride_payment.wallets (user_id, balance, currency) VALUES ('${passengerId}', 1000000, 'VND');`,
    ],
    { stdio: 'inherit' },
  );
}

function cleanupPaymentFixture() {
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_payment',
      '-c',
      `DELETE FROM vietride_payment.wallets WHERE user_id = '${passengerId}';`,
    ],
    { stdio: 'inherit' },
  );
}

function cleanupBookingFixtures() {
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_booking',
      '-c',
      `DELETE FROM vietride_booking.tickets WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.passengers WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (SELECT id FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}');
       DELETE FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}';`,
    ],
    { stdio: 'inherit' },
  );
}

function assertBookingFixturesClean() {
  const rows = execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      'vietride_booking',
      '-Atc',
      `SELECT count(*) FROM vietride_booking.bookings WHERE passenger_user_id = '${passengerId}';`,
    ],
    { encoding: 'utf8' },
  ).trim();
  if (rows !== '0') throw new Error(`Day-16 fixture cleanup failed: booking rows=${rows}`);
}

function readExportedValue(key) {
  const environment = JSON.parse(fs.readFileSync(executionEnvironmentPath, 'utf8'));
  const entry = environment.values.find((value) => value.key === key);
  if (!entry?.value) throw new Error(`Day-16 Newman did not export ${key}.`);
  return entry.value;
}

function redactBookingId(bookingId) {
  return `${bookingId.slice(0, 8)}…${bookingId.slice(-4)}`;
}

async function pollBookingConfirmed(accessToken, bookingId) {
  const maxAttempts = 20;
  const intervalMs = 500;
  let lastStatus = 'no response';
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const response = await fetch(`http://localhost:3000/v1/bookings/${bookingId}`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    const body = await response.json().catch(() => undefined);
    if (response.status === 200 && body?.success === true && body?.data?.bookingId === bookingId) {
      lastStatus = body.data.status;
      if (lastStatus === 'CONFIRMED') {
        console.log(
          `PASS | D16 VNPay eventual booking poll | bookingId=${redactBookingId(bookingId)} status=CONFIRMED attempts=${attempt}`,
        );
        return;
      }
    } else {
      lastStatus = `HTTP ${response.status}`;
    }
    if (attempt < maxAttempts) await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error(
    `D16 VNPay booking did not reach CONFIRMED within ${maxAttempts * intervalMs}ms (bookingId=${redactBookingId(bookingId)}, lastStatus=${lastStatus}).`,
  );
}

async function pollWalletRefund(accessToken, bookingId, refundAmount, balanceAfterDebit) {
  const maxAttempts = 20;
  const intervalMs = 500;
  let lastEvidence = 'no response';
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const headers = { Authorization: `Bearer ${accessToken}` };
    const [bookingResponse, walletResponse, transactionsResponse] = await Promise.all([
      fetch(`http://localhost:3000/v1/bookings/${bookingId}`, { headers }),
      fetch('http://localhost:3000/v1/wallet', { headers }),
      fetch('http://localhost:3000/v1/wallet/transactions?page=1&pageSize=100', { headers }),
    ]);
    const [booking, wallet, transactions] = await Promise.all([
      bookingResponse.json().catch(() => undefined),
      walletResponse.json().catch(() => undefined),
      transactionsResponse.json().catch(() => undefined),
    ]);
    const rows = Array.isArray(transactions?.data?.items)
      ? transactions.data.items
      : (Array.isArray(transactions?.data) ? transactions.data : []);
    const refunds = rows.filter(
      (row) =>
        row.type === 'CREDIT' &&
        row.amount === refundAmount &&
        row.referenceType === 'BOOKING_REFUND' &&
        row.referenceId === bookingId,
    );
    const bookingRefunded =
      bookingResponse.status === 200 &&
      booking?.success === true &&
      booking?.data?.bookingId === bookingId &&
      booking.data.status === 'REFUNDED';
    const walletCredited =
      walletResponse.status === 200 &&
      wallet?.success === true &&
      wallet?.data?.balance === balanceAfterDebit + refundAmount;
    if (bookingRefunded && walletCredited && refunds.length === 1) {
      console.log(
        `PASS | D16 Wallet event-driven refund | bookingId=${redactBookingId(bookingId)} status=REFUNDED amount=${refundAmount} reference=BOOKING_REFUND attempts=${attempt}`,
      );
      return;
    }
    lastEvidence = `booking=${booking?.data?.status ?? `HTTP ${bookingResponse.status}`}, wallet=${wallet?.data?.balance ?? `HTTP ${walletResponse.status}`}, matchingCredits=${refunds.length}`;
    if (attempt < maxAttempts) await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error(
    `D16 Wallet refund did not converge within ${maxAttempts * intervalMs}ms (bookingId=${redactBookingId(bookingId)}, ${lastEvidence}).`,
  );
}

let runError;
try {
  previousBookingMode = readBookingMode();
  await startRealSeams();
  const refreshedJourneyToken = await refreshDay20PassengerToken();
  runDay11Fixture([], { DAY11_RETAIN_FIXTURES: 'true' });
  const tripId = fixtureTripId();
  if (!tripId) throw new Error('Day-16 needs the retained Day-11 generated Trip fixture.');
  provisionPaymentAndSeatFixtures(tripId);
  run('Day-15 signed VNPay top-up', process.execPath, ['scripts/run-day15-newman-local.mjs'], {
    ...process.env,
    DAY15_RETAIN_FIXTURES: 'true',
  });
  if (process.env.DAY16_FORCE_NEWMAN_FAILURE === 'true')
    throw new Error('Forced Day-16 Newman failure requested');
  const passengerAccessToken = refreshedJourneyToken || await issuePassengerToken();
  const walletSeats = JSON.stringify([
    {
      seatNumber: 'A02',
      passenger: {
        fullName: 'Day 16 Passenger',
        phoneNumber: '0900000016',
        idNumber: '079203001016',
      },
    },
  ]);
  const vnPaySeats = JSON.stringify([
    {
      seatNumber: 'A03',
      passenger: {
        fullName: 'Day 16 Passenger',
        phoneNumber: '0900000016',
        idNumber: '079203001016',
      },
    },
  ]);
  run('Day-16 Gateway collection', 'npx', [
    '--yes',
    'newman',
    'run',
    'docs/api/postman/vietride.postman_collection.json',
    '-e',
    'docs/api/postman/vietride.local.postman_environment.json',
    '--folder',
    folder,
    '--reporters',
    'cli',
    '--export-environment',
    executionEnvironmentPath,
    '--env-var',
    `passengerAccessToken=${passengerAccessToken}`,
    '--env-var',
    `day16WalletTripId=${tripId}`,
    '--env-var',
    `day16VnPayTripId=${tripId}`,
    '--env-var',
    'day16PickupStationId=30000000-0000-0000-0000-000000000011',
    '--env-var',
    'day16DropoffStationId=30000000-0000-0000-0000-000000000021',
    '--env-var',
    `day16WalletSeats=${walletSeats}`,
    '--env-var',
    `day16VnPaySeats=${vnPaySeats}`,
    '--env-var',
    `day16WalletIdempotencyKey=${crypto.randomUUID()}`,
    '--env-var',
    `day16VnPayIdempotencyKey=${crypto.randomUUID()}`,
    '--env-var',
    `day16CancelIdempotencyKey=${crypto.randomUUID()}`,
    '--env-var',
    `day16VnPayHashSecret=${requireVnPayHashSecret()}`,
  ]);
  await pollBookingConfirmed(passengerAccessToken, readExportedValue('day16VnPayBookingId'));
  const walletBookingId = readExportedValue('day16WalletBookingId');
  const walletRefundAmount = Number(readExportedValue('day16WalletRefundAmount'));
  const walletBalanceAfterDebit = Number(readExportedValue('day16WalletBalanceAfterDebit'));
  if (!Number.isSafeInteger(walletRefundAmount) || walletRefundAmount <= 0)
    throw new Error('Day-16 cancellation must export a positive Wallet refund amount.');
  if (!Number.isSafeInteger(walletBalanceAfterDebit) || walletBalanceAfterDebit < 0)
    throw new Error('Day-16 wallet debit baseline must be a non-negative integer.');
  await pollWalletRefund(
    passengerAccessToken,
    walletBookingId,
    walletRefundAmount,
    walletBalanceAfterDebit,
  );
  console.log(
    'PASS | D16 | Wallet and VNPay confirmation plus cancellation refund evidence completed',
  );
} catch (error) {
  runError = error;
  console.error(`FAIL | D16 | ${error.message}`);
} finally {
  try {
    cleanupBookingFixtures();
    assertBookingFixturesClean();
    cleanupPaymentFixture();
    runDay11Fixture([], { DAY11_CLEANUP_ONLY: 'true' });
    console.log('PASS | D16 fixture cleanup | retained Day-11 Trip fixture removed');
  } catch (cleanupError) {
    if (!runError) runError = cleanupError;
    else console.error(`FAIL | D16 fixture cleanup | ${cleanupError.message}`);
  }
  try {
    restoreBookingMode(previousBookingMode);
    if (previousBookingMode) console.log(`PASS | D16 mode restore | ${previousBookingMode}`);
  } catch (restoreError) {
    if (!runError) runError = restoreError;
    else console.error(`FAIL | D16 mode restore | ${restoreError.message}`);
  }
  fs.rmSync(executionEnvironmentPath, { force: true });
}
if (runError) throw runError;
