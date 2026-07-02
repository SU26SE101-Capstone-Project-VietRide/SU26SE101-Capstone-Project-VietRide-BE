import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const passengerUserId = '18181818-1818-4181-8181-181818181801';
const driverUserId = '70000000-0000-0000-0000-000000000009';
const operatorId = '10000000-0000-0000-0000-000000000009';
const routeId = '50000000-0000-0000-0000-000000000009';

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: root, stdio: 'inherit', ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} exited with ${result.status}`);
}

function psql(database, sql) {
  return execFileSync('docker', [
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
    '-d', database, '-Atc', sql,
  ], { cwd: root, encoding: 'utf8' }).trim();
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function waitForGeneratedTrip(scheduleId) {
  for (let attempt = 0; attempt < 45; attempt += 1) {
    const row = psql('vietride_trip', `
      select t.id || '|' || r.origin_station_id || '|' || t.status
      from vietride_trip.trips t
      join vietride_trip.routes r on r.id=t.route_id
      where t.driver_schedule_id='${scheduleId}'
      order by t.departure_date_time asc limit 1;`);
    if (row) return row.split('|');
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`No Trip was generated for API-created DriverSchedule ${scheduleId}`);
}

async function request(url, options, expected) {
  const response = await fetch(url, options);
  const text = await response.text();
  let body;
  try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  assert(response.status === expected, `${options.method || 'GET'} ${url}: expected ${expected}, got ${response.status}: ${text}`);
  return body;
}

const realEnv = {
  ...process.env,
  BOOKING_TRIP_USE_DEV_STUB: 'false',
  BOOKING_PAYMENT_USE_DEV_STUB: 'false',
  BOOKING_IDENTITY_USE_DEV_STUB: 'false',
};
run('docker', [
  'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app',
  'up', '-d', '--force-recreate', 'booking', 'gateway',
], { env: realEnv });

// Day 9 creates the vehicle and DriverSchedule through Gateway APIs. Its deterministic
// prerequisite seed is setup only; the Schedule row consumed below is API-created.
run(process.execPath, [path.join(root, 'scripts/run-day9-newman-local.js')]);

const scheduleId = psql('vietride_trip', `
  select id from vietride_trip.driver_schedules
  where operator_id='${operatorId}' and route_id='${routeId}' and driver_user_id='${driverUserId}'
  order by created_at desc limit 1;`);
assert(scheduleId, 'Day 9 did not persist the API-created DriverSchedule.');

const [tripId, originStationId, tripStatus] = await waitForGeneratedTrip(scheduleId);
assert(tripStatus === 'SCHEDULED', `Generated Trip must be SCHEDULED, got ${tripStatus}`);

const seatNumber = psql('vietride_trip', `
  select seat_number from vietride_trip.trip_seats
  where trip_id='${tripId}' and status='AVAILABLE'
  order by seat_number limit 1;`);
assert(seatNumber, `Generated Trip ${tripId} has no AVAILABLE seat.`);

psql('vietride_booking', `delete from vietride_booking.bookings where passenger_user_id='${passengerUserId}';`);
psql('vietride_payment', `
  insert into vietride_payment.wallets (user_id, balance, currency)
  values ('${passengerUserId}', 1000000, 'VND')
  on conflict (user_id) do update set balance=1000000, currency='VND', updated_at=now();`);

const settings = JSON.parse(fs.readFileSync(
  path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
async function token(sub, role, operator) {
  const claims = { role, email: `${role.toLowerCase()}@day18-crossday.local`, hasPhone: 'true' };
  if (operator) claims.operatorId = operator;
  return new SignJWT(claims).setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(sub)
    .setIssuedAt().setExpirationTime('15m').sign(key);
}

const [passengerToken, driverToken] = await Promise.all([
  token(passengerUserId, 'PASSENGER'),
  token(driverUserId, 'DRIVER', operatorId),
]);

const bookingResponse = await request('http://localhost:3000/v1/bookings', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${passengerToken}`,
    'Content-Type': 'application/json',
    'Idempotency-Key': `day18-crossday-${tripId}-${seatNumber}`,
  },
  body: JSON.stringify({
    tripId,
    pickup: { stationId: originStationId },
    seats: [{
      seatNumber,
      passenger: { fullName: 'Day 18 Cross Day', phoneNumber: '0900000018', idNumber: '018181818181' },
    }],
    paymentMethod: 'WALLET',
  }),
}, 201);
assert(bookingResponse?.success === true && bookingResponse?.data?.status === 'CONFIRMED',
  'Real Booking/Payment flow did not produce a CONFIRMED booking.');

const bookingId = bookingResponse.data.bookingId;
const bookingCode = bookingResponse.data.bookingCode;
const passengerRecordId = psql('vietride_booking', `
  select p.id from vietride_booking.passengers p
  join vietride_booking.bookings b on b.id=p.booking_id
  where b.id='${bookingId}' and p.seat_number='${seatNumber}';`);
assert(passengerRecordId, 'Confirmed booking did not persist its Passenger record.');

const schedule = await request('http://localhost:3000/v1/driver/me/schedule', {
  headers: { Authorization: `Bearer ${driverToken}` },
}, 200);
assert(schedule?.data?.trips?.some((trip) => (trip.tripId || trip.id) === tripId),
  'Driver schedule did not contain the Trip generated from its API-created DriverSchedule.');

const manifest = await request(`http://localhost:3000/v1/bookings/trips/${tripId}/manifest`, {
  headers: { Authorization: `Bearer ${driverToken}` },
}, 200);
const manifestText = JSON.stringify(manifest);
assert(manifestText.includes(bookingCode) && manifestText.includes(seatNumber),
  'Manifest did not contain the real confirmed Booking passenger.');
for (const pii of ['fullName', 'phoneNumber', 'idNumber', '0900000018', '018181818181']) {
  assert(!manifestText.includes(pii), `Manifest leaked passenger PII: ${pii}`);
}

const boarded = await request(
  `http://localhost:3000/v1/bookings/trips/${tripId}/boarding/passenger/${passengerRecordId}`,
  { method: 'POST', headers: { Authorization: `Bearer ${driverToken}`, 'Idempotency-Key': `board-${passengerRecordId}` } },
  200);
assert(boarded?.data?.boardingStatus === 'BOARDED', 'Boarding endpoint did not return BOARDED.');

const persisted = psql('vietride_booking', `
  select boarding_status || '|' || (boarded_at is not null)::text
  from vietride_booking.passengers where id='${passengerRecordId}';`);
assert(persisted === 'BOARDED|true', `Persisted boarding state is incorrect: ${persisted}`);

console.log('PASS | Cross-day causal flow');
console.log(`schedule=${scheduleId} -> trip=${tripId} -> booking=${bookingId} (${bookingCode}) -> passenger=${passengerRecordId} BOARDED`);
console.log('PASS | Real seams: Trip snapshot/seat lock/book + Payment wallet charge');
console.log('PASS | Driver schedule contains generated Trip; manifest contains booking and no PII');
