// Reproducible Day-18 Gateway E2E harness. Seeds local-only fixtures, mints
// short-lived development JWTs, runs Newman, then verifies the DB side effect.
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const driverId = '18181818-1818-4181-8181-181818181801';
const passengerUserId = '18181818-1818-4181-8181-181818181802';
const tripId = '18181818-1818-4181-8181-181818181811';
const otherTripId = '18181818-1818-4181-8181-181818181812';
const bookingId = '18181818-1818-4181-8181-181818181821';
const passengerRecordId = '18181818-1818-4181-8181-181818181831';
const bookingCode = 'VR-20260701-DAYE2E22';
const ticketId = '18181818-1818-4181-8181-181818181851';
const ticketCode = 'VT-20260701-DAYE2E22';

function psql(database, sql) {
  return execFileSync(
    'docker',
    ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, '-Atc', sql],
    { encoding: 'utf8' },
  ).trim();
}

const template = psql(
  'vietride_trip',
  `SELECT operator_id || '|' || route_id || '|' || vehicle_id
   FROM vietride_trip.trips ORDER BY created_at LIMIT 1`,
);
if (!template) throw new Error('Day-18 E2E needs one existing local Trip route/vehicle fixture.');
const [operatorId, routeId, vehicleId] = template.split('|');

psql(
  'vietride_trip',
  `DELETE FROM vietride_trip.trips WHERE id IN ('${tripId}', '${otherTripId}');
   INSERT INTO vietride_trip.trips
     (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
      estimated_arrival_time, status, source, base_fare)
   VALUES
     ('${tripId}', '${operatorId}', '${routeId}', '${vehicleId}', '${driverId}',
      now() + interval '1 day', now() + interval '1 day 4 hours', 'BOARDING', 'MANUAL', 200000),
     ('${otherTripId}', '${operatorId}', '${routeId}', '${vehicleId}', '${driverId}',
      now() + interval '2 days', now() + interval '2 days 4 hours', 'BOARDING', 'MANUAL', 200000);`,
);
psql(
  'vietride_booking',
  `DELETE FROM vietride_booking.bookings WHERE id = '${bookingId}' OR booking_code = '${bookingCode}';
   INSERT INTO vietride_booking.bookings
     (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
      base_fare, discount_amount, total_amount, status, confirmed_at)
   VALUES
     ('${bookingId}', '${bookingCode}', '${passengerUserId}', '${tripId}', '${operatorId}',
      '18181818-1818-4181-8181-181818181841', 200000, 0, 200000, 'CONFIRMED', now());
   INSERT INTO vietride_booking.passengers (id, booking_id, seat_number, boarding_status)
   VALUES ('${passengerRecordId}', '${bookingId}', 'A01', 'PENDING');
   INSERT INTO vietride_booking.tickets
     (id, booking_id, passenger_id, ticket_code, seat_number, status, fare_amount,
      discount_amount, paid_amount, issued_at)
   VALUES
     ('${ticketId}', '${bookingId}', '${passengerRecordId}', '${ticketCode}', 'A01',
      'ISSUED', 200000, 0, 200000, now());`,
);

const settings = JSON.parse(
  fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'),
);
const privateKey = await importPKCS8(
  process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
  'RS256',
);
const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
async function token(sub, role) {
  return new SignJWT({ role, email: `${role.toLowerCase()}@day18.local`, hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(privateKey);
}

const driverToken = await token(driverId, 'DRIVER');
const passengerToken = await token(passengerUserId, 'PASSENGER');
const run = spawnSync(
  'npx',
  [
    '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
    '-e', 'docs/api/postman/vietride.local.postman_environment.json',
    '--folder', process.platform === 'win32'
      ? '"Driver - Day 18 schedule + manifest + boarding flow"'
      : 'Driver - Day 18 schedule + manifest + boarding flow',
    '--env-var', `baseUrl=${baseUrl}`,
    '--env-var', `driverAccessToken=${driverToken}`,
    '--env-var', `passengerAccessToken=${passengerToken}`,
    '--env-var', `day18TripId=${tripId}`,
    '--env-var', `day18OtherTripId=${otherTripId}`,
    '--env-var', `day18PassengerRecordId=${passengerRecordId}`,
    '--env-var', `day18BookingCode=${bookingCode}`,
    '--env-var', `day18TicketCode=${ticketCode}`,
    '--reporters', 'cli',
  ],
  { cwd: root, shell: process.platform === 'win32', stdio: 'inherit' },
);
if (run.error) throw run.error;
if (run.status !== 0) process.exit(run.status ?? 1);

const state = psql(
  'vietride_booking',
  `SELECT boarding_status || '|' || (boarded_at IS NOT NULL)::text
   FROM vietride_booking.passengers WHERE id = '${passengerRecordId}'`,
);
if (state !== 'BOARDED|true') throw new Error(`Day-18 DB side-effect check failed: ${state}`);
console.log('PASS | Day-18 DB side effect | boardingStatus=BOARDED boardedAt=set');

// Leave deterministic shared fixtures clean so older day harnesses can safely
// recreate their route/vehicle rows after this cumulative regression flow.
psql('vietride_booking', `DELETE FROM vietride_booking.bookings WHERE id = '${bookingId}';`);
psql(
  'vietride_trip',
  `DELETE FROM vietride_trip.trips WHERE id IN ('${tripId}', '${otherTripId}');`,
);
console.log('PASS | Day-18 fixture cleanup | temporary booking and trips removed');
