// Day-33 Trip disruption E2E. Public mutations run only through Gateway/Newman.
// Direct database access is limited to isolated setup, bounded evidence, and cleanup.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const gatewayBaseUrl = (process.env.GATEWAY_BASE_URL || 'http://localhost:3000').replace(/\/$/, '');
const ids = Object.freeze({
  operator: crypto.randomUUID(),
  admin: crypto.randomUUID(),
  cancelPassenger: crypto.randomUUID(),
  routePassenger: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  fallbackStation: crypto.randomUUID(),
  currentStop: crypto.randomUUID(),
  candidateStop: crypto.randomUUID(),
  route: crypto.randomUUID(),
  alternativeRoute: crypto.randomUUID(),
  differentAlternativeRoute: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  cancelTrip: crypto.randomUUID(),
  routeTrip: crypto.randomUUID(),
  cancelBooking: crypto.randomUUID(),
  routeBooking: crypto.randomUUID(),
  cancelPassengerRow: crypto.randomUUID(),
  routePassengerRow: crypto.randomUUID(),
  cancelTicket: crypto.randomUUID(),
  routeTicket: crypto.randomUUID(),
  parcel: crypto.randomUUID(),
  bookingPayment: crypto.randomUUID(),
  parcelPayment: crypto.randomUUID(),
  parcelAdditionalPayment: crypto.randomUUID(),
  cancelKey: crypto.randomUUID(),
  routeKey: crypto.randomUUID(),
});
const runTag = ids.cancelTrip.replaceAll('-', '').slice(0, 10).toUpperCase();
const codeDate = new Date().toISOString().slice(0, 10).replaceAll('-', '');
const codeSuffix = runTag.slice(0, 8);
let platformWalletBaseline;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

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

async function poll(label, probe, predicate, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = probe();
    if (predicate(value)) {
      console.log(`PASS | ${label} | ${value}`);
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 400));
  }
  throw new Error(`${label} timed out; last=${String(value)}`);
}

function redisKeys() {
  return [ids.cancelKey, ids.routeKey].flatMap((key) => [
    `trip:idem:${key}`,
    `idempotency:${key}`,
  ]);
}

function cleanup() {
  const operations = [
    () =>
      psql(
        'vietride_notification',
        `DELETE FROM vietride_notification.notification_deliveries
         WHERE notification_id IN (
           SELECT id FROM vietride_notification.notifications
           WHERE data->>'tripId' IN ('${ids.cancelTrip}', '${ids.routeTrip}')
              OR data->>'bookingId' IN ('${ids.cancelBooking}', '${ids.routeBooking}'));
         DELETE FROM vietride_notification.email_deliveries
         WHERE notification_id IN (
           SELECT id FROM vietride_notification.notifications
           WHERE data->>'tripId' IN ('${ids.cancelTrip}', '${ids.routeTrip}')
              OR data->>'bookingId' IN ('${ids.cancelBooking}', '${ids.routeBooking}'));
         DELETE FROM vietride_notification.notifications
         WHERE data->>'tripId' IN ('${ids.cancelTrip}', '${ids.routeTrip}')
            OR data->>'bookingId' IN ('${ids.cancelBooking}', '${ids.routeBooking}');`,
      ),
    () =>
      psql(
        'vietride_payment',
        `DELETE FROM vietride_payment.refund_failure_logs
         WHERE booking_id = '${ids.cancelBooking}' OR parcel_id = '${ids.parcel}'
            OR reference_id IN ('${ids.cancelBooking}', '${ids.parcel}');
         DELETE FROM vietride_payment.operator_ledger_entries
         WHERE reference_id IN ('${ids.cancelBooking}', '${ids.parcel}');
         DELETE FROM vietride_payment.wallet_transactions
         WHERE reference_id IN ('${ids.cancelBooking}', '${ids.parcel}');
         DELETE FROM vietride_payment.platform_wallet_transactions
         WHERE reference_id IN ('${ids.cancelBooking}', '${ids.parcel}');
         DELETE FROM vietride_payment.outbox_events
         WHERE payload->>'bookingId' = '${ids.cancelBooking}'
            OR payload->>'parcelId' = '${ids.parcel}'
            OR payload->>'referenceId' IN ('${ids.cancelBooking}', '${ids.parcel}');
         DELETE FROM vietride_payment.payments
         WHERE id IN (
           '${ids.bookingPayment}', '${ids.parcelPayment}', '${ids.parcelAdditionalPayment}');
         DELETE FROM vietride_payment.wallets
         WHERE user_id IN ('${ids.cancelPassenger}', '${ids.routePassenger}');`,
      ),
    () => {
      if (!platformWalletBaseline) return;
      if (platformWalletBaseline.created) {
        psql(
          'vietride_payment',
          `DELETE FROM vietride_payment.platform_wallets WHERE id = '${platformWalletBaseline.id}';`,
        );
      } else {
        psql(
          'vietride_payment',
          `UPDATE vietride_payment.platform_wallets
           SET balance = ${platformWalletBaseline.balance},
               row_version = ${platformWalletBaseline.rowVersion},
               updated_at = '${platformWalletBaseline.updatedAt}'::timestamptz
           WHERE id = '${platformWalletBaseline.id}';`,
        );
      }
    },
    () =>
      psql(
        'vietride_parcel',
        `DELETE FROM vietride_parcel.outbox_events
         WHERE payload->>'tripId' = '${ids.cancelTrip}'
            OR payload->>'parcelId' = '${ids.parcel}';
         DELETE FROM vietride_parcel.parcel_stats WHERE operator_id = '${ids.operator}';
         DELETE FROM vietride_parcel.parcels WHERE id = '${ids.parcel}';`,
      ),
    () =>
      psql(
        'vietride_booking',
        `DELETE FROM vietride_booking.booking_status_history
         WHERE booking_id IN ('${ids.cancelBooking}', '${ids.routeBooking}');
         DELETE FROM vietride_booking.outbox_events
         WHERE payload->>'tripId' IN ('${ids.cancelTrip}', '${ids.routeTrip}')
            OR payload->>'bookingId' IN ('${ids.cancelBooking}', '${ids.routeBooking}');
         DELETE FROM vietride_booking.bookings
         WHERE id IN ('${ids.cancelBooking}', '${ids.routeBooking}');`,
      ),
    () =>
      psql(
        'vietride_trip',
        `DELETE FROM vietride_trip.trip_audit_logs
         WHERE trip_id IN ('${ids.cancelTrip}', '${ids.routeTrip}');
         DELETE FROM vietride_trip.outbox_events
         WHERE payload->>'tripId' IN ('${ids.cancelTrip}', '${ids.routeTrip}');
         DELETE FROM vietride_trip.trip_seats
         WHERE trip_id IN ('${ids.cancelTrip}', '${ids.routeTrip}');
         DELETE FROM vietride_trip.trips
         WHERE id IN ('${ids.cancelTrip}', '${ids.routeTrip}');
         DELETE FROM vietride_trip.alternative_route_stops
         WHERE alternative_route_id = '${ids.alternativeRoute}';
         DELETE FROM vietride_trip.alternative_routes WHERE id = '${ids.alternativeRoute}';
         DELETE FROM vietride_trip.stops WHERE id IN ('${ids.currentStop}', '${ids.candidateStop}');
         DELETE FROM vietride_trip.routes WHERE id = '${ids.route}';
         DELETE FROM vietride_trip.vehicles WHERE id = '${ids.vehicle}';
         DELETE FROM vietride_trip.vehicle_types WHERE id = '${ids.vehicleType}';
         DELETE FROM vietride_trip.stations
         WHERE id IN ('${ids.originStation}', '${ids.destinationStation}', '${ids.fallbackStation}');`,
      ),
    () =>
      psql(
        'vietride_identity',
        `DELETE FROM vietride_identity.users
         WHERE id IN ('${ids.admin}', '${ids.cancelPassenger}', '${ids.routePassenger}');
         DELETE FROM vietride_identity.operators WHERE id = '${ids.operator}';`,
      ),
    () =>
      execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...redisKeys()], {
        cwd: root,
        stdio: 'ignore',
      }),
  ];
  const errors = [];
  for (const operation of operations) {
    try {
      operation();
    } catch (error) {
      errors.push(error);
    }
  }
  if (errors.length > 0) throw new AggregateError(errors, 'Day-33 cleanup failed');
}

function seed() {
  cleanup();
  psql(
    'vietride_identity',
    `INSERT INTO vietride_identity.operators
       (id, name, business_registration_number, tax_code, contact_email, contact_phone,
        registration_status, approved_at, cancellation_policy, is_active)
     VALUES
       ('${ids.operator}', 'Day 33 Operator ${runTag}', 'D33BR${runTag}', 'D33TAX${runTag}',
        'operator-${runTag.toLowerCase()}@day33.local', '0900000033',
        'APPROVED', now(), '[]'::jsonb, true);
     INSERT INTO vietride_identity.users (id, email, display_name, role, status, operator_id)
     VALUES
       ('${ids.admin}', 'admin-${runTag.toLowerCase()}@day33.local',
        'Day 33 Admin', 'OPERATOR_ADMIN', 'ACTIVE', '${ids.operator}'),
       ('${ids.cancelPassenger}', 'cancel-${runTag.toLowerCase()}@day33.local',
        'Day 33 Cancel Passenger', 'PASSENGER', 'ACTIVE', NULL),
       ('${ids.routePassenger}', 'route-${runTag.toLowerCase()}@day33.local',
        'Day 33 Route Passenger', 'PASSENGER', 'ACTIVE', NULL);`,
  );

  const platformWallet = psql(
    'vietride_payment',
    `SELECT id::text || '|' || balance::text || '|' || row_version::text || '|' || updated_at::text
     FROM vietride_payment.platform_wallets LIMIT 1`,
  );
  if (platformWallet) {
    const [id, balance, rowVersion, updatedAt] = platformWallet.split('|');
    platformWalletBaseline = {
      id,
      balance: Number(balance),
      rowVersion: Number(rowVersion),
      updatedAt,
      created: false,
    };
    psql(
      'vietride_payment',
      `UPDATE vietride_payment.platform_wallets
       SET balance = balance + 1000000 WHERE id = '${id}';`,
    );
  } else {
    const id = crypto.randomUUID();
    platformWalletBaseline = { id, balance: 0, rowVersion: 0, updatedAt: '', created: true };
    psql(
      'vietride_payment',
      `INSERT INTO vietride_payment.platform_wallets (id, balance, row_version)
       VALUES ('${id}', 1000000, 0);`,
    );
  }
  psql(
    'vietride_payment',
    `INSERT INTO vietride_payment.wallets (user_id, balance, row_version)
     VALUES ('${ids.cancelPassenger}', 0, 0), ('${ids.routePassenger}', 0, 0);
     INSERT INTO vietride_payment.payments
       (id, reference_type, reference_id, user_id, amount, method, status, succeeded_at, context)
     VALUES
       ('${ids.bookingPayment}', 'BOOKING', '${ids.cancelBooking}', '${ids.cancelPassenger}',
        100000, 'WALLET', 'SUCCEEDED', now(),
        '{"version":1,"allocations":[{"referenceId":"${ids.cancelBooking}","referenceType":"BOOKING","operatorId":"${ids.operator}","tripId":"${ids.cancelTrip}","grossAmount":100000,"voucherVietRideFundedAmount":0,"voucherOperatorFundedAmount":0}]}'::jsonb),
       ('${ids.parcelPayment}', 'PARCEL', '${ids.parcel}', '${ids.cancelPassenger}',
        20000, 'WALLET', 'SUCCEEDED', now(),
        '{"version":1,"allocations":[{"referenceId":"${ids.parcel}","referenceType":"PARCEL","operatorId":"${ids.operator}","tripId":"${ids.cancelTrip}","grossAmount":20000,"voucherVietRideFundedAmount":0,"voucherOperatorFundedAmount":0}]}'::jsonb),
       ('${ids.parcelAdditionalPayment}', 'PARCEL_ADDITIONAL', '${ids.parcel}', '${ids.cancelPassenger}',
        5000, 'WALLET', 'SUCCEEDED', now(),
        '{"version":1,"allocations":[{"referenceId":"${ids.parcel}","referenceType":"PARCEL_ADDITIONAL","operatorId":"${ids.operator}","tripId":"${ids.cancelTrip}","grossAmount":5000,"voucherVietRideFundedAmount":0,"voucherOperatorFundedAmount":0}]}'::jsonb);`,
  );

  psql(
    'vietride_trip',
    `INSERT INTO vietride_trip.stations (id, name, slug, city, province)
     VALUES
       ('${ids.originStation}', 'Day 33 Origin ${runTag}', 'd33-origin-${runTag.toLowerCase()}', 'HCM', 'HCM'),
       ('${ids.destinationStation}', 'Day 33 Destination ${runTag}', 'd33-destination-${runTag.toLowerCase()}', 'Da Lat', 'Lam Dong'),
       ('${ids.fallbackStation}', 'Day 33 Fallback ${runTag}', 'd33-fallback-${runTag.toLowerCase()}', 'Bao Loc', 'Lam Dong');
     INSERT INTO vietride_trip.stops (id, operator_id, name, latitude, longitude)
     VALUES
       ('${ids.currentStop}', '${ids.operator}', 'Day 33 Current Stop', 10.7000000, 106.7000000),
       ('${ids.candidateStop}', '${ids.operator}', 'Day 33 Candidate Stop', 10.8000000, 106.8000000);
     INSERT INTO vietride_trip.vehicle_types
       (id, code, display_name, default_seat_count, is_system_defined)
     VALUES ('${ids.vehicleType}', 'D33_${runTag}', 'Day 33 Vehicle', 2, false);
     INSERT INTO vietride_trip.routes
       (id, operator_id, name, origin_station_id, destination_station_id,
        base_fare, estimated_duration_minutes)
     VALUES
       ('${ids.route}', '${ids.operator}', 'Day 33 Route ${runTag}',
        '${ids.originStation}', '${ids.destinationStation}', 100000, 240);
     INSERT INTO vietride_trip.vehicles
       (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats, status)
     VALUES
       ('${ids.vehicle}', '${ids.operator}', '${ids.vehicleType}', 'D33${runTag}',
        '{"version":1,"vehicleTypeCode":"STANDARD","totalSeats":2,"rows":1,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"seatType":"STANDARD","isEnabled":true},{"seatNumber":"A02","row":1,"col":2,"deck":1,"seatType":"STANDARD","isEnabled":true}]}',
        2, 'ACTIVE');
     INSERT INTO vietride_trip.alternative_routes
       (id, route_id, name, destination_station_id, estimated_duration_minutes)
     VALUES
       ('${ids.alternativeRoute}', '${ids.route}', 'Day 33 Alternative ${runTag}',
        '${ids.fallbackStation}', 300);
     INSERT INTO vietride_trip.alternative_route_stops
       (alternative_route_id, stop_id, order_index, estimated_duration_from_origin_minutes)
     VALUES ('${ids.alternativeRoute}', '${ids.candidateStop}', 1, 120);
     INSERT INTO vietride_trip.trips
       (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
        estimated_arrival_time, status, source, base_fare)
     VALUES
       ('${ids.cancelTrip}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.admin}',
        now() + interval '10 days', now() + interval '10 days 4 hours',
        'SCHEDULED', 'MANUAL', 100000),
       ('${ids.routeTrip}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.admin}',
        now() + interval '11 days', now() + interval '11 days 4 hours',
        'SCHEDULED', 'MANUAL', 100000);
     INSERT INTO vietride_trip.trip_seats (trip_id, seat_number, seat_type, status)
     VALUES
       ('${ids.cancelTrip}', 'A01', 'STANDARD', 'BOOKED'),
       ('${ids.routeTrip}', 'A02', 'STANDARD', 'BOOKED');`,
  );

  psql(
    'vietride_booking',
    `INSERT INTO vietride_booking.bookings
       (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_stop_id,
        dropoff_station_id, base_fare, total_amount, status, confirmed_at)
     VALUES
       ('${ids.cancelBooking}', 'VR-${codeDate}-${codeSuffix}', '${ids.cancelPassenger}',
        '${ids.cancelTrip}', '${ids.operator}', '${ids.currentStop}', '${ids.destinationStation}',
        100000, 100000, 'CONFIRMED', now()),
       ('${ids.routeBooking}', 'VR-${codeDate}-${codeSuffix.split('').reverse().join('')}', '${ids.routePassenger}',
        '${ids.routeTrip}', '${ids.operator}', '${ids.currentStop}', '${ids.destinationStation}',
        100000, 100000, 'CONFIRMED', now());
     INSERT INTO vietride_booking.passengers (id, booking_id, seat_number)
     VALUES
       ('${ids.cancelPassengerRow}', '${ids.cancelBooking}', 'A01'),
       ('${ids.routePassengerRow}', '${ids.routeBooking}', 'A02');
     INSERT INTO vietride_booking.tickets
       (id, booking_id, passenger_id, ticket_code, seat_number, status,
        fare_amount, paid_amount, issued_at)
     VALUES
       ('${ids.cancelTicket}', '${ids.cancelBooking}', '${ids.cancelPassengerRow}',
        'VT-${codeDate}-${codeSuffix}', 'A01', 'ISSUED', 100000, 100000, now()),
       ('${ids.routeTicket}', '${ids.routeBooking}', '${ids.routePassengerRow}',
        'VT-${codeDate}-${codeSuffix.split('').reverse().join('')}', 'A02', 'ISSUED', 100000, 100000, now());`,
  );

  psql(
    'vietride_parcel',
    `INSERT INTO vietride_parcel.parcels
       (id, parcel_code, sender_user_id, recipient_name, recipient_phone,
        operator_id, trip_id, size_category, estimated_weight_kg,
        deposit_amount, additional_amount, status)
     VALUES
       ('${ids.parcel}', 'VRP-D33-${runTag}', '${ids.cancelPassenger}',
        'Day 33 Recipient', '0900000034', '${ids.operator}', '${ids.cancelTrip}',
        'SMALL', 1.00, 20000, 5000, 'PENDING');`,
  );
  console.log('PASS | isolated Day-33 fixtures seeded');
}

async function issueOperatorToken() {
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
    role: 'OPERATOR_ADMIN',
    operatorId: ids.operator,
    email: `admin-${runTag.toLowerCase()}@day33.local`,
    hasPhone: 'true',
  })
    .setProtectedHeader({
      alg: 'RS256',
      kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid,
    })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(ids.admin)
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

function runNewman(token) {
  const variables = {
    baseUrl: gatewayBaseUrl,
    day33OperatorAdminToken: token,
    day33CancelTripId: ids.cancelTrip,
    day33CancelBookingId: ids.cancelBooking,
    day33ParcelId: ids.parcel,
    day33CancelKey: ids.cancelKey,
    day33RouteTripId: ids.routeTrip,
    day33RouteBookingId: ids.routeBooking,
    day33AlternativeRouteId: ids.alternativeRoute,
    day33DifferentAlternativeRouteId: ids.differentAlternativeRoute,
    day33RouteKey: ids.routeKey,
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
    'Day 33 - Trip cancellation and alternative route',
  ];
  for (const [key, value] of Object.entries(variables)) {
    args.push('--env-var', `${key}=${value}`);
  }
  execFileSync(process.execPath, args, { cwd: root, stdio: 'inherit' });
}

async function verifyEffects() {
  await poll(
    'Trip cancellation and Outbox committed',
    () =>
      psql(
        'vietride_trip',
        `SELECT status::text || '|' ||
          (SELECT count(*) FROM vietride_trip.outbox_events
           WHERE event_type = 'trip.trip.cancelled'
             AND payload->>'tripId' = '${ids.cancelTrip}')
         FROM vietride_trip.trips WHERE id = '${ids.cancelTrip}'`,
      ),
    (value) => value === 'CANCELLED|1',
  );
  await poll(
    'Booking cancellation propagated once',
    () =>
      psql(
        'vietride_booking',
        `SELECT status::text || '|' ||
          (SELECT count(*) FROM vietride_booking.outbox_events
           WHERE event_type = 'booking.booking.cancelled'
             AND payload->>'bookingId' = '${ids.cancelBooking}')
         FROM vietride_booking.bookings WHERE id = '${ids.cancelBooking}'`,
      ),
    (value) => value === 'CANCELLED|1',
  );
  await poll(
    'Booking and Parcel refunds reached Wallet without failure rows',
    () =>
      psql(
        'vietride_payment',
        `SELECT balance::text || '|' ||
          (SELECT count(*) FROM vietride_payment.refund_failure_logs
           WHERE booking_id = '${ids.cancelBooking}' OR parcel_id = '${ids.parcel}')
         FROM vietride_payment.wallets WHERE user_id = '${ids.cancelPassenger}'`,
      ),
    (value) => value === '125000|0',
  );
  await poll(
    'Parcel cancellation used full collected amount',
    () =>
      psql(
        'vietride_parcel',
        `SELECT status::text || '|' ||
          (SELECT payload->>'refundAmount' FROM vietride_parcel.outbox_events
           WHERE event_type = 'parcel.parcel.cancelled'
             AND payload->>'parcelId' = '${ids.parcel}' LIMIT 1)
         FROM vietride_parcel.parcels WHERE id = '${ids.parcel}'`,
      ),
    (value) => value === 'CANCELLED|25000',
  );
  await poll(
    'Route change produced one frozen pending action',
    () =>
      psql(
        'vietride_booking',
        `SELECT b.status::text || '|' || count(a.id)::text || '|' ||
                coalesce(max(a.metadata->>'originalStopId'), '') || '|' ||
                coalesce(max(a.metadata->>'fallbackDestinationStationId'), '') || '|' ||
                coalesce(max(a.metadata->>'shuttleRequired'), '')
         FROM vietride_booking.bookings b
         LEFT JOIN vietride_booking.booking_pending_actions a
           ON a.booking_id = b.id AND a.resolved_at IS NULL
         WHERE b.id = '${ids.routeBooking}'
         GROUP BY b.status`,
      ),
    (value) =>
      value ===
      `CONFIRMED|1|${ids.currentStop}|${ids.fallbackStation}|true`,
  );
  const routeState = psql(
    'vietride_trip',
    `SELECT alternative_route_id::text || '|' ||
       (SELECT count(*) FROM vietride_trip.outbox_events
        WHERE event_type = 'trip.trip.route_changed'
          AND payload->>'tripId' = '${ids.routeTrip}')
     FROM vietride_trip.trips WHERE id = '${ids.routeTrip}'`,
  );
  assert(routeState === `${ids.alternativeRoute}|1`, `Unexpected route state: ${routeState}`);
  console.log(`PASS | route idempotency mismatch wrote no second event | ${routeState}`);
}

function verifyCleanup() {
  const remaining =
    Number(
      psql(
        'vietride_trip',
        `SELECT count(*) FROM vietride_trip.trips
         WHERE id IN ('${ids.cancelTrip}', '${ids.routeTrip}')`,
      ),
    ) +
    Number(
      psql(
        'vietride_booking',
        `SELECT count(*) FROM vietride_booking.bookings
         WHERE id IN ('${ids.cancelBooking}', '${ids.routeBooking}')`,
      ),
    ) +
    Number(
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.parcels WHERE id = '${ids.parcel}'`,
      ),
    ) +
    Number(
      psql(
        'vietride_identity',
        `SELECT count(*) FROM vietride_identity.operators WHERE id = '${ids.operator}'`,
      ),
    );
  assert(remaining === 0, `Day-33 cleanup left ${remaining} primary fixture rows`);
  console.log('PASS | Day-33 fixture cleanup verified');
}

let runError;
try {
  const health = await fetch(`${gatewayBaseUrl}/health`);
  assert(health.status === 200, `Gateway health returned HTTP ${health.status}`);
  seed();
  const token = await issueOperatorToken();
  console.log('PASS | short-lived Day-33 JWT generated (redacted)');
  runNewman(token);
  await verifyEffects();
} catch (error) {
  runError = error;
} finally {
  try {
    cleanup();
    verifyCleanup();
  } catch (cleanupError) {
    runError = runError
      ? new AggregateError([runError, cleanupError], 'Day-33 run and cleanup failed')
      : cleanupError;
  }
}

if (runError) throw runError;
console.log('PASS | Day-33 Trip disruption E2E complete');
