// Reproducible Day-18 Gateway E2E harness. Seeds local-only fixtures, mints
// short-lived development JWTs, runs Newman, then verifies the DB side effect.
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const driverId = '18181818-1818-4181-8181-181818181801';
const assistantId = '18181818-1818-4181-8181-181818181803';
const unassignedDriverId = '18181818-1818-4181-8181-181818181804';
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

function cleanup() {
  psql('vietride_booking', `DELETE FROM vietride_booking.tickets WHERE booking_id = '${bookingId}'; DELETE FROM vietride_booking.passengers WHERE booking_id = '${bookingId}'; DELETE FROM vietride_booking.bookings WHERE id = '${bookingId}';`);
  psql('vietride_trip', `DELETE FROM vietride_trip.trips WHERE id IN ('${tripId}', '${otherTripId}');`);
}

function assertClean() {
  const bookingRows = psql('vietride_booking', `SELECT count(*) FROM vietride_booking.bookings WHERE id = '${bookingId}';`);
  const tripRows = psql('vietride_trip', `SELECT count(*) FROM vietride_trip.trips WHERE id IN ('${tripId}', '${otherTripId}');`);
  if (bookingRows !== '0' || tripRows !== '0') throw new Error(`Day-18 fixture cleanup failed: bookings=${bookingRows}, trips=${tripRows}`);
}

let runError;
try {
  const template = psql(
  'vietride_trip',
  `SELECT t.operator_id || '|' || t.route_id || '|' || t.vehicle_id
   FROM vietride_trip.trips t
   JOIN vietride_trip.routes r ON r.id = t.route_id
   WHERE r.path_polyline IS NULL OR r.path_polyline = ''
   ORDER BY t.created_at LIMIT 1`,
  );
  if (!template) throw new Error('Day-18 E2E needs one existing local Trip route/vehicle fixture without path geometry.');
  const [operatorId, routeId, vehicleId] = template.split('|');
  const encodedPolylineTemplate = psql(
  'vietride_trip',
  `SELECT id || '|' || path_polyline
   FROM vietride_trip.routes
   WHERE path_polyline IS NOT NULL AND path_polyline <> ''
   ORDER BY created_at LIMIT 1`,
  );
  if (!encodedPolylineTemplate)
    throw new Error('Day-18 E2E needs one existing Route with an encoded polyline fixture.');
  const encodedPolylineSeparator = encodedPolylineTemplate.indexOf('|');
  const encodedPolylineRouteId = encodedPolylineTemplate.slice(0, encodedPolylineSeparator);
  const encodedPolyline = encodedPolylineTemplate.slice(encodedPolylineSeparator + 1);
  const encodedPolylineBase64 = Buffer.from(encodedPolyline, 'utf8').toString('base64');

psql(
  'vietride_trip',
  `DELETE FROM vietride_trip.trips WHERE id IN ('${tripId}', '${otherTripId}');
   INSERT INTO vietride_trip.trips
     (id, operator_id, route_id, vehicle_id, driver_user_id, assistant_user_id, departure_date_time,
      estimated_arrival_time, status, source, base_fare)
   VALUES
     ('${tripId}', '${operatorId}', '${routeId}', '${vehicleId}', '${driverId}', '${assistantId}',
      now() + interval '1 day', now() + interval '1 day 4 hours', 'BOARDING', 'MANUAL', 200000),
     ('${otherTripId}', '${operatorId}', '${encodedPolylineRouteId}', '${vehicleId}', '${driverId}', '${assistantId}',
      now() + interval '2 days', now() + interval '2 days 4 hours', 'BOARDING', 'MANUAL', 200000);
   INSERT INTO vietride_trip.trip_stops
     (trip_id, stop_id, order_index, estimated_arrival_time, status, allow_pickup,
      allow_dropoff, distance_from_origin_km)
   SELECT '${otherTripId}', stop_id, order_index,
     now() + interval '2 days' + estimated_duration_from_origin_minutes * interval '1 minute',
     'PENDING', allow_pickup, allow_dropoff, distance_from_origin_km
   FROM vietride_trip.route_stops
   WHERE route_id = '${encodedPolylineRouteId}';`,
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
const assistantToken = await token(assistantId, 'ASSISTANT');
const unassignedDriverToken = await token(unassignedDriverId, 'DRIVER');
const passengerToken = await token(passengerUserId, 'PASSENGER');
const newmanArgs = [
  '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
  '-e', 'docs/api/postman/vietride.local.postman_environment.json',
  '--folder', 'Driver - Day 18 schedule + manifest + boarding flow',
  '--env-var', `baseUrl=${baseUrl}`,
  '--env-var', `driverAccessToken=${driverToken}`,
  '--env-var', `assistantAccessToken=${assistantToken}`,
  '--env-var', `unassignedDriverAccessToken=${unassignedDriverToken}`,
  '--env-var', `passengerAccessToken=${passengerToken}`,
  '--env-var', `day18TripId=${tripId}`,
  '--env-var', `day18OtherTripId=${otherTripId}`,
  '--env-var', `day18EncodedPolylineBase64=${encodedPolylineBase64}`,
  '--env-var', `day18PassengerRecordId=${passengerRecordId}`,
  '--env-var', `day18BookingCode=${bookingCode}`,
  '--env-var', `day18TicketCode=${ticketCode}`,
  '--reporters', 'cli',
];
const run = process.env.DAY18_FORCE_NEWMAN_FAILURE === 'true'
  ? { status: 1 }
  : (() => {
    const useNpxCli = process.platform === 'win32';
    const npxCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js');
    return spawnSync(useNpxCli ? process.execPath : 'npx', useNpxCli ? [npxCli, ...newmanArgs] : newmanArgs, {
      cwd: root,
      // Passing the argument array directly preserves generated bearer tokens.
      shell: false,
      stdio: 'inherit',
    });
  })();
if (run.error) throw run.error;
if (run.status !== 0) throw new Error(`Newman failed with status ${run.status ?? 1}`);

const state = psql(
  'vietride_booking',
  `SELECT boarding_status || '|' || (boarded_at IS NOT NULL)::text
   FROM vietride_booking.passengers WHERE id = '${passengerRecordId}'`,
);
if (state !== 'BOARDED|true') throw new Error(`Day-18 DB side-effect check failed: ${state}`);
console.log('PASS | Day-18 DB side effect | boardingStatus=BOARDED boardedAt=set');

} catch (error) {
  runError = error;
} finally {
  // Leave deterministic shared fixtures clean so older day harnesses can safely
  // recreate their route/vehicle rows after this cumulative regression flow.
  cleanup();
  assertClean();
  console.log('PASS | D18 fixture cleanup | temporary booking and trips removed');
}
if (runError) throw runError;
