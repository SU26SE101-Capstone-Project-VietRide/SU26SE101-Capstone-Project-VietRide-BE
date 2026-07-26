// Day-34 vehicle-substitution E2E. Public mutations run through Gateway/Newman.
// Direct database access is limited to isolated setup, evidence, and cleanup.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const uuid = () => crypto.randomUUID();
const ids = {
  operator: '40000000-0000-4000-8000-000000000001',
  otherOperator: uuid(),
  admin: uuid(),
  otherAdmin: uuid(),
  driver: '40000000-0000-4000-8000-000000000014',
  oldTrip: uuid(),
  replacementVehicle: uuid(),
  booking: uuid(),
  owner: uuid(),
  passengers: Array.from({ length: 5 }, uuid),
  substitutionKey: uuid(),
  crossTenantKey: uuid(),
  confirmationKeys: Array.from({ length: 3 }, uuid),
};
const legacyBookingCode = `LEGACY-D34-${ids.booking.slice(0, 8)}`;
let substitutionId = '';
let newTripId = '';

function psql(database, sql) {
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
      database,
      '-Atc',
      sql,
    ],
    { cwd: root, encoding: 'utf8' },
  ).trim();
}

function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${expected}, got ${actual}`);
  }
}

async function issueToken(subject, role, operatorId) {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const privateKey = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  return new SignJWT({
    role,
    operatorId,
    email: `${role.toLowerCase()}@day34.local`,
    hasPhone: 'true',
  })
    .setProtectedHeader({
      alg: 'RS256',
      kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid,
    })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(privateKey);
}

function resolveNpxCli() {
  const candidates = [
    path.join(process.env.APPDATA || '', 'npm', 'node_modules', 'npm', 'bin', 'npx-cli.js'),
    path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js'),
  ];
  const result = candidates.find((candidate) => fs.existsSync(candidate));
  if (!result) throw new Error('Unable to locate npm npx-cli.js');
  return result;
}

function seed() {
  const seats = ids.passengers
    .map((_, index) => `('${uuid()}','${ids.oldTrip}','A0${index + 1}','STANDARD','BOOKED')`)
    .join(',');
  psql(
    'vietride_trip',
    `
    INSERT INTO vietride_trip.vehicles (
      id, operator_id, vehicle_type_id, license_plate, seat_layout_json,
      total_seats, status, is_active, created_at, updated_at)
    SELECT
      '${ids.replacementVehicle}', operator_id, vehicle_type_id,
      'D34-${ids.replacementVehicle.slice(0, 8)}',
      '{"seats":[
        {"seatNumber":"B01","type":"STANDARD","disabled":false},
        {"seatNumber":"B02","type":"STANDARD","disabled":false},
        {"seatNumber":"B03","type":"STANDARD","disabled":false},
        {"seatNumber":"B04","type":"STANDARD","disabled":false}
      ]}'::jsonb,
      4, 'ACTIVE'::public.vehicle_status, TRUE, now(), now()
    FROM vietride_trip.vehicles
    WHERE id = '40000000-0000-4000-8000-000000000402';

    INSERT INTO vietride_trip.trips (
      id, operator_id, route_id, vehicle_id, driver_user_id,
      departure_date_time, estimated_arrival_time, actual_departure_time,
      status, source, base_fare, created_at, updated_at)
    VALUES (
      '${ids.oldTrip}', '${ids.operator}',
      '40000000-0000-4000-8000-000000000411',
      '40000000-0000-4000-8000-000000000402',
      '${ids.driver}', now() - interval '30 minutes', now() + interval '3 hours',
      now() - interval '30 minutes', 'IN_PROGRESS', 'MANUAL', 100000, now(), now());

    INSERT INTO vietride_trip.trip_seats (
      id, trip_id, seat_number, seat_type, status)
    VALUES ${seats};
  `,
  );

  const passengers = ids.passengers
    .map(
      (passengerId, index) =>
        `('${passengerId}','${ids.booking}','A0${index + 1}','BOARDED',
        now() - interval '20 minutes',now(),now())`,
    )
    .join(',');
  psql(
    'vietride_booking',
    `
    INSERT INTO vietride_booking.bookings (
      id, booking_code, passenger_user_id, trip_id, operator_id,
      pickup_station_id, base_fare, discount_amount, total_amount, status,
      refund_override, confirmed_at, created_at, updated_at)
    VALUES (
      '${ids.booking}', '${legacyBookingCode}', '${ids.owner}', '${ids.oldTrip}',
      '${ids.operator}', '40000000-0000-4000-8000-000000000302',
      500000, 0, 500000, 'CONFIRMED', FALSE,
      now() - interval '1 hour', now(), now());

    INSERT INTO vietride_booking.passengers (
      id, booking_id, seat_number, boarding_status, boarded_at, created_at, updated_at)
    VALUES ${passengers};
  `,
  );
}

async function runNewman() {
  const recoveryAt = new Date(Date.now() + 20 * 60 * 1000).toISOString();
  const variables = {
    baseUrl: 'http://localhost:3000',
    day34OperatorAdminToken: await issueToken(ids.admin, 'OPERATOR_ADMIN', ids.operator),
    day34OtherOperatorAdminToken: await issueToken(
      ids.otherAdmin,
      'OPERATOR_ADMIN',
      ids.otherOperator,
    ),
    day34DriverToken: await issueToken(ids.driver, 'DRIVER', ids.operator),
    day34OldTripId: ids.oldTrip,
    day34ReplacementVehicleId: ids.replacementVehicle,
    day34RecoveryDepartureAt: recoveryAt,
    day34NotifyPassengers: 'false',
    day34SubstitutionKey: ids.substitutionKey,
    day34CrossTenantKey: ids.crossTenantKey,
    day34PassengerId1: ids.passengers[0],
    day34PassengerId2: ids.passengers[1],
    day34PassengerId3: ids.passengers[2],
    day34ConfirmKey1: ids.confirmationKeys[0],
    day34ConfirmKey2: ids.confirmationKeys[1],
    day34ConfirmKey3: ids.confirmationKeys[2],
  };
  const args = [
    resolveNpxCli(),
    '--yes',
    'newman',
    'run',
    'docs/api/postman/vietride.postman_collection.json',
    '-e',
    'docs/api/postman/vietride.local.postman_environment.json',
    '--folder',
    'Day34',
  ];
  for (const [key, value] of Object.entries(variables)) {
    args.push('--env-var', `${key}=${value}`);
  }
  execFileSync(process.execPath, args, { cwd: root, stdio: 'inherit' });
}

function verify() {
  substitutionId = psql(
    'vietride_trip',
    `SELECT payload->>'eventId'
     FROM vietride_trip.outbox_events
     WHERE event_type = 'trip.trip.vehicle_substituted'
       AND payload->>'oldTripId' = '${ids.oldTrip}'
     ORDER BY created_at DESC LIMIT 1;`,
  );
  newTripId = psql(
    'vietride_trip',
    `SELECT payload->>'newTripId'
     FROM vietride_trip.outbox_events WHERE id = '${substitutionId}';`,
  );
  assertEqual(
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.outbox_events
       WHERE id = '${substitutionId}'
         AND payload->>'eventId' = '${substitutionId}';`,
    ),
    '1',
    'Trip Outbox identity',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) || ':' ||
              count(*) FILTER (WHERE confirmation_status = 'CONFIRMED') || ':' ||
              count(*) FILTER (WHERE confirmation_status = 'PENDING_CONFIRM') || ':' ||
              count(*) FILTER (WHERE new_seat_number IS NULL)
       FROM vietride_booking.booking_transfers
       WHERE booking_id = '${ids.booking}'
         AND original_trip_id = '${ids.oldTrip}'
         AND new_trip_id = '${newTripId}';`,
    ),
    '5:3:2:1',
    'transfer/confirmation/null-seat counts',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) FROM vietride_booking.outbox_events
       WHERE event_type = 'booking.booking.transferred'
         AND payload->>'bookingId' = '${ids.booking}'
         AND payload->>'eventId' = id::text;`,
    ),
    '1',
    'Booking Outbox identity',
  );
  assertEqual(
    psql(
      'vietride_notification',
      `SELECT count(*) FROM vietride_notification.notifications
       WHERE data->>'bookingId' = '${ids.booking}'
         AND type = 'VEHICLE_SUBSTITUTED';`,
    ),
    '0',
    'notification suppression',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT booking_code FROM vietride_booking.bookings WHERE id = '${ids.booking}';`,
    ),
    legacyBookingCode,
    'legacy booking code',
  );
  console.log('PASS | persisted 5:3:2:1 flow, Outbox identities, suppression, legacy code');
}

function cleanup() {
  if (substitutionId) {
    psql(
      'vietride_booking',
      `DELETE FROM vietride_booking.integration_inbox
       WHERE message_id = '${substitutionId}';`,
    );
  }
  psql(
    'vietride_notification',
    `
    DELETE FROM vietride_notification.notification_deliveries
    WHERE notification_id IN (
      SELECT id FROM vietride_notification.notifications
      WHERE data->>'bookingId' = '${ids.booking}');
    DELETE FROM vietride_notification.email_deliveries
    WHERE notification_id IN (
      SELECT id FROM vietride_notification.notifications
      WHERE data->>'bookingId' = '${ids.booking}');
    DELETE FROM vietride_notification.notifications
    WHERE data->>'bookingId' = '${ids.booking}';
  `,
  );
  psql(
    'vietride_booking',
    `
    DELETE FROM vietride_booking.outbox_events
    WHERE payload->>'bookingId' = '${ids.booking}';
    DELETE FROM vietride_booking.booking_transfers WHERE booking_id = '${ids.booking}';
    DELETE FROM vietride_booking.passengers WHERE booking_id = '${ids.booking}';
    DELETE FROM vietride_booking.bookings WHERE id = '${ids.booking}';
  `,
  );
  const tripIds = newTripId ? `'${ids.oldTrip}','${newTripId}'` : `'${ids.oldTrip}'`;
  psql(
    'vietride_trip',
    `
    DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id = '${ids.oldTrip}';
    DELETE FROM vietride_trip.outbox_events
    WHERE payload->>'oldTripId' = '${ids.oldTrip}'
       OR payload->>'tripId' = '${ids.oldTrip}';
    DELETE FROM vietride_trip.trip_seats WHERE trip_id IN (${tripIds});
    DELETE FROM vietride_trip.trip_stops WHERE trip_id IN (${tripIds});
    DELETE FROM vietride_trip.trips WHERE id IN (${tripIds});
    DELETE FROM vietride_trip.vehicles WHERE id = '${ids.replacementVehicle}';
  `,
  );
  console.log('PASS | Day34 audit fixtures cleaned');
}

try {
  seed();
  await runNewman();
  verify();
} finally {
  cleanup();
}
