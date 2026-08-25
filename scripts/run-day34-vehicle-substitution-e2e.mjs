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
  capacityProbeKey: uuid(),
  substitutionKey: uuid(),
  crossTenantKey: uuid(),
  confirmationKeys: Array.from({ length: 3 }, uuid),
  lateConfirmationKey: uuid(),
  missingSeatConfirmationKey: uuid(),
  paymentLedgerEntry: uuid(),
  paymentSourceEvent: uuid(),
  paymentReference: uuid(),
  replacementCompletedEvent: uuid(),
  confirmedParcel: uuid(),
  escalatedParcel: uuid(),
  parcelConfirmKey: uuid(),
};
const legacyBookingCode = `LEGACY-D34-${ids.booking.slice(0, 8)}`;
const confirmablePassengerIds = [...ids.passengers].sort().slice(0, 3);
const confirmedParcelCode = `VRP-D35-C${ids.confirmedParcel.replaceAll('-', '').slice(0, 8)}`;
const escalatedParcelCode = `VRP-D35-E${ids.escalatedParcel.replaceAll('-', '').slice(0, 8)}`;
const initialCargo = Object.freeze({ weightKg: '5.50', volumeM3: '0.0070' });
let substitutionId = '';
let newTripId = '';
let disruptedEventId = '';
let ownedMessageIds = [];

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

async function poll(label, probe, predicate, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = probe();
    if (predicate(value)) {
      console.log(`PASS | ${label} | ${value}`);
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${label} timed out; last=${String(value)}`);
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
    ...(operatorId ? { operatorStatus: 'APPROVED' } : {}),
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

async function api(pathname, token, idempotencyKey, body) {
  const response = await fetch(`http://localhost:3000${pathname}`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${token}`,
      'content-type': 'application/json',
      'idempotency-key': idempotencyKey,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return {
    status: response.status,
    body: text ? JSON.parse(text) : undefined,
  };
}

function publishRabbit(routingKey, messageId, payload) {
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_rabbitmq',
      'rabbitmqadmin',
      '--username=vietride',
      '--password=vietride_dev',
      'publish',
      'exchange=vietride.events',
      `routing_key=${routingKey}`,
      `payload=${JSON.stringify(payload)}`,
      `properties=${JSON.stringify({
        content_type: 'application/json',
        message_id: messageId,
      })}`,
    ],
    { cwd: root, stdio: 'ignore' },
  );
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
  psql(
    'vietride_identity',
    `INSERT INTO vietride_identity.users (
       id, email, display_name, role, status, operator_id, failed_login_attempts)
     VALUES (
       '${ids.admin}', 'admin-${ids.admin.slice(0, 8)}@day34.local',
       'Day 34 Operator Admin', 'OPERATOR_ADMIN', 'ACTIVE', '${ids.operator}', 0);`,
  );

  const seats = ids.passengers
    .map(
      (_, index) =>
        `('${uuid()}','${ids.oldTrip}','A0${index + 1}','STANDARD','BOOKED','${ids.booking}')`,
    )
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
      status, source, base_fare, seat_layout_snapshot_json,
      total_loaded_weight_kg, total_loaded_volume_m3, created_at, updated_at)
    VALUES (
      '${ids.oldTrip}', '${ids.operator}',
      '40000000-0000-4000-8000-000000000411',
      '40000000-0000-4000-8000-000000000402',
      '${ids.driver}', now() - interval '30 minutes', now() + interval '3 hours',
      now() - interval '30 minutes', 'IN_PROGRESS', 'MANUAL', 100000,
      (SELECT seat_layout_json FROM vietride_trip.vehicles
       WHERE id = '40000000-0000-4000-8000-000000000402'),
      ${initialCargo.weightKg}, ${initialCargo.volumeM3}, now(), now());

    INSERT INTO vietride_trip.trip_cargo_parcels (
      trip_id, parcel_id, weight_kg, volume_m3, state, loaded_at)
    VALUES
      ('${ids.oldTrip}', '${ids.confirmedParcel}', 2.25, 0.0030, 'LOADED', now()),
      ('${ids.oldTrip}', '${ids.escalatedParcel}', 3.25, 0.0040, 'LOADED', now());

    INSERT INTO vietride_trip.trip_seats (
      id, trip_id, seat_number, seat_type, status, booking_id)
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

  psql(
    'vietride_payment',
    `INSERT INTO vietride_payment.operator_ledger_entries (
       id, operator_id, trip_id, entry_type, amount, reference_type,
       reference_id, source_event_id, occurred_at, note, actor_type,
       actor_snapshot_resolved, created_at)
     VALUES (
       '${ids.paymentLedgerEntry}', '${ids.operator}', '${ids.oldTrip}',
       'BOOKING_REVENUE', 500000, 'BOOKING', '${ids.paymentReference}',
       '${ids.paymentSourceEvent}', now(), 'Day34 substitution E2E revenue',
       'SYSTEM', true, now());`,
  );

  psql(
    'vietride_parcel',
    `INSERT INTO vietride_parcel.parcels (
       id, parcel_code, sender_user_id, recipient_name, recipient_phone,
       operator_id, trip_id, size_category, estimated_size_category,
       estimated_length_cm, estimated_width_cm, estimated_height_cm,
       estimated_weight_kg, estimated_volume_m3, estimated_dim_weight_kg,
       estimated_chargeable_weight_kg, deposit_amount, status, loaded_at)
     VALUES
       ('${ids.confirmedParcel}', '${confirmedParcelCode}', '${ids.owner}',
        'Day 35 Confirm Recipient', '+84900003501', '${ids.operator}', '${ids.oldTrip}',
        'SMALL', 'SMALL', 30.00, 20.00, 5.00,
        2.25, 0.0030, 0.50, 2.25, 0, 'LOADED', now()),
       ('${ids.escalatedParcel}', '${escalatedParcelCode}', '${ids.owner}',
        'Day 35 Escalate Recipient', '+84900003502', '${ids.operator}', '${ids.oldTrip}',
        'MEDIUM', 'MEDIUM', 40.00, 20.00, 5.00,
        3.25, 0.0040, 0.67, 3.25, 0, 'IN_TRANSIT', now());`,
  );
}

async function verifyInsufficientSeatGuard() {
  const token = await issueToken(ids.admin, 'OPERATOR_ADMIN', ids.operator);
  const recoveryAt = new Date(Date.now() + 20 * 60 * 1000).toISOString();
  const before = psql(
    'vietride_trip',
    `SELECT
       (SELECT count(*) FROM vietride_trip.trips
        WHERE source = 'VEHICLE_SUBSTITUTION' AND vehicle_id = '${ids.replacementVehicle}')::text || ':' ||
       (SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id = '${ids.oldTrip}')::text || ':' ||
       (SELECT count(*) FROM vietride_trip.outbox_events
        WHERE payload->>'oldTripId' = '${ids.oldTrip}')::text || ':' ||
       (SELECT status::text FROM vietride_trip.trips WHERE id = '${ids.oldTrip}');`,
  );
  const result = await api(
    `/v1/operator/trips/${ids.oldTrip}/substitute-vehicle`,
    token,
    ids.capacityProbeKey,
    {
      replacementVehicleId: ids.replacementVehicle,
      estimatedRecoveryDepartureAt: recoveryAt,
      reason: 'Day 34 capacity guard probe',
      notifyPassengers: false,
    },
  );
  assertEqual(String(result.status), '409', 'insufficient-seat HTTP status');
  assertEqual(
    result.body?.error?.code,
    'REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS',
    'insufficient-seat error code',
  );
  const fields = Object.fromEntries(
    (result.body?.error?.fields ?? []).map(({ field, message }) => [field, message]),
  );
  assertEqual(fields.usableSeats, '4', 'usableSeats field');
  assertEqual(fields.passengersToTransfer, '5', 'passengersToTransfer field');
  assertEqual(fields.missingSeats, '1', 'missingSeats field');
  const after = psql(
    'vietride_trip',
    `SELECT
       (SELECT count(*) FROM vietride_trip.trips
        WHERE source = 'VEHICLE_SUBSTITUTION' AND vehicle_id = '${ids.replacementVehicle}')::text || ':' ||
       (SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id = '${ids.oldTrip}')::text || ':' ||
       (SELECT count(*) FROM vietride_trip.outbox_events
        WHERE payload->>'oldTripId' = '${ids.oldTrip}')::text || ':' ||
       (SELECT status::text FROM vietride_trip.trips WHERE id = '${ids.oldTrip}');`,
  );
  assertEqual(after, before, '409 side-effect snapshot');
  console.log(`PASS | insufficient-seat guard and zero side effects | ${after}`);
}

async function runNewman(folder, extraVariables = {}) {
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
    day34PassengerId1: confirmablePassengerIds[0],
    day34PassengerId2: confirmablePassengerIds[1],
    day34PassengerId3: confirmablePassengerIds[2],
    day34ConfirmKey1: ids.confirmationKeys[0],
    day34ConfirmKey2: ids.confirmationKeys[1],
    day34ConfirmKey3: ids.confirmationKeys[2],
    day35ConfirmedParcelId: ids.confirmedParcel,
    day35ConfirmedParcelCode: confirmedParcelCode,
    day35ConfirmKey: ids.parcelConfirmKey,
    ...(newTripId ? { day34NewTripId: newTripId } : {}),
    ...extraVariables,
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
    folder,
  ];
  for (const [key, value] of Object.entries(variables)) {
    args.push('--env-var', `${key}=${value}`);
  }
  try {
    execFileSync(process.execPath, args, { cwd: root, stdio: 'inherit' });
  } catch (error) {
    throw new Error(`${folder} Newman failed with exit ${error.status ?? 'unknown'}.`);
  }
}

async function verifyDay34() {
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
      'vietride_booking',
      `SELECT count(*) FROM vietride_booking.outbox_events
       WHERE event_type = 'booking.booking.seat_shortage_detected'
         AND payload->>'bookingId' = '${ids.booking}'
         AND payload->>'affectedPassengerCount' = '1';`,
    ),
    '1',
    'one shortage event per Booking',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) FROM vietride_booking.booking_pending_actions
       WHERE booking_id = '${ids.booking}';`,
    ),
    '0',
    'no pending seat-assignment action',
  );
  await poll(
    'Booking owner is notified despite notifyPassengers=false when a seat is missing',
    () =>
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications
         WHERE user_id = '${ids.owner}'
           AND data->>'bookingId' = '${ids.booking}'
           AND type = 'VEHICLE_SUBSTITUTED';`,
      ),
    (value) => value === '1',
  );
  await poll(
    'Active Operator Admin receives exactly one seat-shortage alert',
    () =>
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications
         WHERE user_id = '${ids.admin}'
           AND data->>'bookingId' = '${ids.booking}'
           AND type = 'VEHICLE_SUBSTITUTION_SEAT_SHORTAGE';`,
      ),
    (value) => value === '1',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) FILTER (
                WHERE original_seat_type = 'STANDARD'
                  AND new_seat_type = 'STANDARD'
                  AND is_seat_downgrade = false)::text || ':' ||
              count(*) FILTER (
                WHERE original_seat_type = 'STANDARD'
                  AND new_seat_type IS NULL
                  AND is_seat_downgrade = false)::text
       FROM vietride_booking.booking_transfers
       WHERE booking_id = '${ids.booking}';`,
    ),
    '4:1',
    'persisted seat-type and downgrade evidence',
  );
  assertEqual(
    psql(
      'vietride_trip',
      `SELECT (metadata->>'acknowledgedInsufficientSeats') || ':' ||
              (metadata->>'usableSeats') || ':' ||
              (metadata->>'passengersToTransfer') || ':' ||
              (metadata->>'missingSeats')
       FROM vietride_trip.trip_audit_logs
       WHERE trip_id = '${ids.oldTrip}'
       ORDER BY occurred_at DESC LIMIT 1;`,
    ),
    'true:4:5:1',
    'acknowledgement audit metadata',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT booking_code FROM vietride_booking.bookings WHERE id = '${ids.booking}';`,
    ),
    legacyBookingCode,
    'legacy booking code',
  );
  assertEqual(
    psql(
      'vietride_trip',
      `SELECT reserved_parcel_weight_kg::text || ':' ||
              reserved_parcel_volume_m3::text || ':' ||
              total_loaded_weight_kg::text || ':' ||
              total_loaded_volume_m3::text || ':' ||
              (SELECT count(*) FROM vietride_trip.trip_cargo_parcels
               WHERE trip_id = '${newTripId}' AND state <> 'RELEASED')::text
       FROM vietride_trip.trips WHERE id = '${newTripId}';`,
    ),
    '0.00:0.0000:0.00:0.0000:0',
    'replacement cargo starts empty',
  );
  console.log('PASS | persisted 5:3:2:1 flow, downgrade evidence, alerts, audit, Outbox identities');
}

async function verifyTransferEscalationAndLateConfirmation() {
  psql(
    'vietride_booking',
    `UPDATE vietride_booking.booking_transfers
     SET transferred_at = now() - interval '2 hours 1 minute'
     WHERE booking_id = '${ids.booking}'
       AND new_trip_id = '${newTripId}'
       AND confirmation_status = 'PENDING_CONFIRM';`,
  );
  await poll(
    'Expired pending transfer group escalates through the recurring job',
    () =>
      psql(
        'vietride_booking',
        `SELECT count(*) FROM vietride_booking.booking_transfers
         WHERE booking_id = '${ids.booking}'
           AND new_trip_id = '${newTripId}'
           AND confirmation_status = 'ESCALATED';`,
      ),
    (value) => value === '2',
    360_000,
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) FROM vietride_booking.outbox_events
       WHERE event_type = 'booking.booking.transfer_escalated'
         AND payload->>'bookingId' = '${ids.booking}'
         AND payload->>'pendingConfirmationCount' = '2';`,
    ),
    '1',
    'one escalation event per Booking and replacement Trip',
  );
  await poll(
    'Active Operator Admin receives one transfer escalation alert',
    () =>
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications
         WHERE user_id = '${ids.admin}'
           AND data->>'bookingId' = '${ids.booking}'
           AND type = 'BOOKING_TRANSFER_ESCALATED';`,
      ),
    (value) => value === '1',
  );

  const seatedPassengerId = psql(
    'vietride_booking',
    `SELECT passenger_id FROM vietride_booking.booking_transfers
     WHERE booking_id = '${ids.booking}'
       AND new_trip_id = '${newTripId}'
       AND confirmation_status = 'ESCALATED'
       AND new_seat_number IS NOT NULL
     ORDER BY passenger_id LIMIT 1;`,
  );
  const missingSeatPassengerId = psql(
    'vietride_booking',
    `SELECT passenger_id FROM vietride_booking.booking_transfers
     WHERE booking_id = '${ids.booking}'
       AND new_trip_id = '${newTripId}'
       AND confirmation_status = 'ESCALATED'
       AND new_seat_number IS NULL
     ORDER BY passenger_id LIMIT 1;`,
  );
  const driverToken = await issueToken(ids.driver, 'DRIVER', ids.operator);
  const lateConfirmation = await api(
    `/v1/bookings/trips/${newTripId}/transfers/passengers/${seatedPassengerId}/confirm`,
    driverToken,
    ids.lateConfirmationKey,
  );
  assertEqual(String(lateConfirmation.status), '200', 'late confirmation HTTP status');
  assertEqual(
    lateConfirmation.body?.data?.confirmationStatus,
    'CONFIRMED',
    'late confirmation status',
  );
  const missingSeatConfirmation = await api(
    `/v1/bookings/trips/${newTripId}/transfers/passengers/${missingSeatPassengerId}/confirm`,
    driverToken,
    ids.missingSeatConfirmationKey,
  );
  assertEqual(String(missingSeatConfirmation.status), '409', 'missing-seat confirmation HTTP status');
  assertEqual(
    missingSeatConfirmation.body?.error?.code,
    'BOOKING_TRANSFER_SEAT_PENDING',
    'missing-seat confirmation code',
  );
  assertEqual(
    psql(
      'vietride_booking',
      `SELECT count(*) FILTER (WHERE confirmation_status = 'CONFIRMED')::text || ':' ||
              count(*) FILTER (WHERE confirmation_status = 'ESCALATED')::text
       FROM vietride_booking.booking_transfers
       WHERE booking_id = '${ids.booking}' AND new_trip_id = '${newTripId}';`,
    ),
    '4:1',
    'late confirmation and missing-seat state',
  );
  console.log('PASS | escalation event/notification, late confirmation, and missing-seat conflict');
}

async function verifyPaymentSettlementMarkers() {
  await poll(
    'Original disrupted Trip retains its revenue settlement',
    () =>
      psql(
        'vietride_payment',
        `SELECT count(*) FROM vietride_payment.operator_trip_settlements
         WHERE operator_id = '${ids.operator}'
           AND trip_id = '${ids.oldTrip}'
           AND net_amount = 500000
           AND status = 'PENDING_HOLD';`,
      ),
    (value) => value === '1',
  );

  const completedAt = new Date().toISOString();
  publishRabbit('trip.trip.completed', ids.replacementCompletedEvent, {
    eventId: ids.replacementCompletedEvent,
    occurredAt: completedAt,
    tripId: newTripId,
    operatorId: ids.operator,
    terminalAt: completedAt,
    hasSubstitution: false,
    tripCode: 'TRIP-D34-REPLACEMENT',
    source: 'VEHICLE_SUBSTITUTION',
  });
  await poll(
    'Replacement Trip creates a zero settlement with substitution reason',
    () =>
      psql(
        'vietride_payment',
        `SELECT count(*) FROM vietride_payment.operator_trip_settlements
         WHERE operator_id = '${ids.operator}'
           AND trip_id = '${newTripId}'
           AND net_amount = 0
           AND status = 'CANCELLED'
           AND cancel_reason = 'VEHICLE_SUBSTITUTION_REVENUE_RETAINED_ON_ORIGINAL_TRIP'
           AND wallet_transaction_id IS NULL;`,
      ),
    (value) => value === '1',
  );
  assertEqual(
    psql(
      'vietride_payment',
      `SELECT count(*)::text || ':' || coalesce(sum(net_amount), 0)::text || ':' ||
              count(*) FILTER (WHERE wallet_transaction_id IS NOT NULL)::text
       FROM vietride_payment.operator_trip_settlements
       WHERE operator_id = '${ids.operator}'
         AND trip_id IN ('${ids.oldTrip}', '${newTripId}');`,
    ),
    '2:500000:0',
    'settlement count, total net, and wallet movements',
  );
  console.log('PASS | original revenue retained, replacement marker is zero, no wallet movement');
}

async function verifyDay35() {
  await poll(
    'Parcel consumer requested both physical transfers',
    () =>
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.parcels
         WHERE id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')
           AND status = 'PENDING_TRANSFER_CONFIRM'
           AND transfer_target_trip_id = '${newTripId}'
           AND transfer_requested_at IS NOT NULL;`,
      ),
    (value) => value === '2',
  );

  await runNewman('Day35 - Parcel cargo transfer conservation');

  assertEqual(
    psql(
      'vietride_trip',
      `SELECT
         (SELECT count(*) FROM vietride_trip.trip_cargo_parcels
          WHERE trip_id = '${ids.oldTrip}' AND parcel_id = '${ids.confirmedParcel}'
            AND state = 'RELEASED' AND released_at IS NOT NULL)::text || ':' ||
         (SELECT count(*) FROM vietride_trip.trip_cargo_parcels
          WHERE trip_id = '${newTripId}' AND parcel_id = '${ids.confirmedParcel}'
            AND state = 'LOADED' AND loaded_at IS NOT NULL)::text || ':' ||
         (SELECT count(*) FROM vietride_trip.trip_cargo_parcels
          WHERE parcel_id = '${ids.confirmedParcel}')::text || ':' ||
         (SELECT weight_kg::text || ':' || volume_m3::text
          FROM vietride_trip.trip_cargo_parcels
          WHERE trip_id = '${ids.oldTrip}' AND parcel_id = '${ids.confirmedParcel}') || ':' ||
         (SELECT weight_kg::text || ':' || volume_m3::text
          FROM vietride_trip.trip_cargo_parcels
          WHERE trip_id = '${newTripId}' AND parcel_id = '${ids.confirmedParcel}');`,
    ),
    '1:1:2:2.25:0.0030:2.25:0.0030',
    'confirmed cargo ledger topology and replay no-op',
  );
  assertEqual(
    psql(
      'vietride_trip',
      `SELECT
         (SELECT total_loaded_weight_kg FROM vietride_trip.trips WHERE id = '${ids.oldTrip}')::text || ':' ||
         (SELECT total_loaded_weight_kg FROM vietride_trip.trips WHERE id = '${newTripId}')::text || ':' ||
         (SELECT total_loaded_volume_m3 FROM vietride_trip.trips WHERE id = '${ids.oldTrip}')::text || ':' ||
         (SELECT total_loaded_volume_m3 FROM vietride_trip.trips WHERE id = '${newTripId}')::text || ':' ||
         ((SELECT total_loaded_weight_kg FROM vietride_trip.trips WHERE id = '${ids.oldTrip}') +
          (SELECT total_loaded_weight_kg FROM vietride_trip.trips WHERE id = '${newTripId}'))::text || ':' ||
         ((SELECT total_loaded_volume_m3 FROM vietride_trip.trips WHERE id = '${ids.oldTrip}') +
          (SELECT total_loaded_volume_m3 FROM vietride_trip.trips WHERE id = '${newTripId}'))::text;`,
    ),
    `3.25:2.25:0.0040:0.0030:${initialCargo.weightKg}:${initialCargo.volumeM3}`,
    'source plus target cargo conservation after confirmation',
  );

  psql(
    'vietride_parcel',
    `UPDATE vietride_parcel.parcels
     SET transfer_requested_at = now() - interval '31 minutes'
     WHERE id = '${ids.escalatedParcel}' AND status = 'PENDING_TRANSFER_CONFIRM';`,
  );
  await poll(
    'Timed-out transfer escalated without moving cargo',
    () =>
      psql(
        'vietride_parcel',
        `SELECT status::text FROM vietride_parcel.parcels WHERE id = '${ids.escalatedParcel}';`,
      ),
    (value) => value === 'TRANSFER_ESCALATED',
    360_000,
  );
  assertEqual(
    psql(
      'vietride_trip',
      `SELECT (SELECT count(*) FROM vietride_trip.trip_cargo_parcels
               WHERE trip_id = '${ids.oldTrip}' AND parcel_id = '${ids.escalatedParcel}'
                 AND state = 'LOADED' AND released_at IS NULL)::text || ':' ||
              (SELECT total_loaded_weight_kg FROM vietride_trip.trips
               WHERE id = '${ids.oldTrip}')::text || ':' ||
              (SELECT total_loaded_weight_kg FROM vietride_trip.trips
               WHERE id = '${newTripId}')::text
              || ':' ||
              (SELECT total_loaded_volume_m3 FROM vietride_trip.trips
               WHERE id = '${ids.oldTrip}')::text || ':' ||
              (SELECT total_loaded_volume_m3 FROM vietride_trip.trips
               WHERE id = '${newTripId}')::text || ':' ||
              (SELECT weight_kg FROM vietride_trip.trip_cargo_parcels
               WHERE trip_id = '${ids.oldTrip}' AND parcel_id = '${ids.escalatedParcel}'
                 AND state = 'LOADED' AND released_at IS NULL)::text || ':' ||
              (SELECT volume_m3 FROM vietride_trip.trip_cargo_parcels
               WHERE trip_id = '${ids.oldTrip}' AND parcel_id = '${ids.escalatedParcel}'
                 AND state = 'LOADED' AND released_at IS NULL)::text;`,
    ),
    '1:3.25:2.25:0.0040:0.0030:3.25:0.0040',
    'escalation retains source cargo',
  );
  console.log('PASS | Day35 confirmation, replay, conservation, and escalation verified');
}

function ownedOutboxPredicates() {
  return {
    vietride_trip: `payload->>'oldTripId' = '${ids.oldTrip}' OR payload->>'tripId' = '${ids.oldTrip}'`,
    vietride_booking: `payload->>'bookingId' = '${ids.booking}'`,
    vietride_parcel: `payload->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')`,
  };
}

function collectOwnedMessageIds() {
  const discovered = Object.entries(ownedOutboxPredicates()).flatMap(([database, predicate]) =>
    psql(database, `SELECT id FROM ${database}.outbox_events WHERE ${predicate};`)
      .split(/\r?\n/)
      .filter(Boolean),
  );
  ownedMessageIds = [...new Set([...ownedMessageIds, ...discovered])];
}

function ownedMessageSql() {
  return ownedMessageIds.map((id) => `'${id}'`).join(', ') || 'NULL';
}

function countPublishingOwnedOutbox() {
  return Object.entries(ownedOutboxPredicates()).reduce(
    (total, [database, predicate]) =>
      total +
      Number(
        psql(
          database,
          `SELECT count(*) FROM ${database}.outbox_events
           WHERE (${predicate}) AND status = 'PUBLISHING';`,
        ),
      ),
    0,
  );
}

function deleteOwnedMessagingArtifacts() {
  collectOwnedMessageIds();
  const messageSql = ownedMessageSql();
  for (const [database, predicate] of Object.entries(ownedOutboxPredicates())) {
    psql(
      database,
      `DELETE FROM ${database}.integration_inbox WHERE message_id IN (${messageSql});
       DELETE FROM ${database}.outbox_events WHERE ${predicate};`,
    );
  }
  psql(
    'vietride_notification',
    `DELETE FROM vietride_notification.notification_deliveries
     WHERE notification_id IN (
       SELECT id FROM vietride_notification.notifications
       WHERE data->>'bookingId' = '${ids.booking}'
          OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}'));
     DELETE FROM vietride_notification.email_deliveries
     WHERE notification_id IN (
       SELECT id FROM vietride_notification.notifications
       WHERE data->>'bookingId' = '${ids.booking}'
          OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}'));
     DELETE FROM vietride_notification.notifications
     WHERE data->>'bookingId' = '${ids.booking}'
        OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
     DELETE FROM vietride_notification.processed_messages WHERE message_id IN (${messageSql});`,
  );
  for (const messageId of ownedMessageIds) {
    const keys = execFileSync(
      'docker',
      [
        'exec',
        'vietride_redis',
        'redis-cli',
        '--scan',
        '--pattern',
        `notification:idem:*:${messageId}`,
      ],
      { cwd: root, encoding: 'utf8' },
    )
      .split(/\r?\n/)
      .filter(Boolean);
    if (keys.length > 0) {
      execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...keys], {
        cwd: root,
        stdio: 'ignore',
      });
    }
  }
}

function countOwnedMessagingArtifacts() {
  const messageSql = ownedMessageSql();
  const outboxCount = Object.entries(ownedOutboxPredicates()).reduce(
    (total, [database, predicate]) =>
      total +
      Number(psql(database, `SELECT count(*) FROM ${database}.outbox_events WHERE ${predicate};`)),
    0,
  );
  const inboxCount = ['vietride_trip', 'vietride_booking', 'vietride_parcel'].reduce(
    (total, database) =>
      total +
      Number(
        psql(
          database,
          `SELECT count(*) FROM ${database}.integration_inbox WHERE message_id IN (${messageSql});`,
        ),
      ),
    0,
  );
  const notificationCount = Number(
    psql(
      'vietride_notification',
      `SELECT
         (SELECT count(*) FROM vietride_notification.notifications
          WHERE data->>'bookingId' = '${ids.booking}'
             OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')) +
         (SELECT count(*) FROM vietride_notification.processed_messages
          WHERE message_id IN (${messageSql}));`,
    ),
  );
  let redisCount = 0;
  for (const messageId of ownedMessageIds) {
    redisCount += execFileSync(
      'docker',
      [
        'exec',
        'vietride_redis',
        'redis-cli',
        '--scan',
        '--pattern',
        `notification:idem:*:${messageId}`,
      ],
      { cwd: root, encoding: 'utf8' },
    )
      .split(/\r?\n/)
      .filter(Boolean).length;
  }
  return outboxCount + inboxCount + notificationCount + redisCount;
}

async function quiesceOwnedMessagingArtifacts() {
  let stablePasses = 0;
  for (let attempt = 1; attempt <= 10 && stablePasses < 3; attempt += 1) {
    deleteOwnedMessagingArtifacts();
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    collectOwnedMessageIds();
    stablePasses = countOwnedMessagingArtifacts() === 0 ? stablePasses + 1 : 0;
  }
  if (stablePasses !== 3) {
    throw new Error('Day34-35 messaging cleanup did not reach bounded quiescence');
  }
}

async function cleanup() {
  if (!newTripId) {
    newTripId = psql(
      'vietride_trip',
      `SELECT id FROM vietride_trip.trips
       WHERE vehicle_id = '${ids.replacementVehicle}'
         AND source = 'VEHICLE_SUBSTITUTION'
       ORDER BY created_at DESC LIMIT 1;`,
    );
  }
  if (!substitutionId) {
    substitutionId = psql(
      'vietride_trip',
      `SELECT id FROM vietride_trip.outbox_events
       WHERE event_type = 'trip.trip.vehicle_substituted'
         AND payload->>'oldTripId' = '${ids.oldTrip}'
       ORDER BY created_at DESC LIMIT 1;`,
    );
  }
  disruptedEventId = psql(
    'vietride_trip',
    `SELECT id FROM vietride_trip.outbox_events
     WHERE event_type = 'trip.trip.disrupted'
       AND payload->>'tripId' = '${ids.oldTrip}'
     ORDER BY created_at DESC LIMIT 1;`,
  );
  collectOwnedMessageIds();
  ownedMessageIds = [
    ...new Set([...ownedMessageIds, substitutionId, disruptedEventId].filter(Boolean)),
  ];
  await poll(
    'Day34-35 owned publishers left PUBLISHING state',
    countPublishingOwnedOutbox,
    (count) => count === 0,
    10_000,
  );
  const ownedMessageSql = ownedMessageIds.map((id) => `'${id}'`).join(', ');

  if (ownedMessageSql) {
    psql(
      'vietride_booking',
      `DELETE FROM vietride_booking.integration_inbox
       WHERE message_id IN (${ownedMessageSql});`,
    );
  }
  psql(
    'vietride_payment',
    `DELETE FROM vietride_payment.processed_integration_events
     WHERE event_id IN (${ownedMessageSql || 'NULL'}, '${ids.replacementCompletedEvent}');
     DELETE FROM vietride_payment.operator_trip_settlements
     WHERE trip_id IN ('${ids.oldTrip}'${newTripId ? `, '${newTripId}'` : ''});
     DELETE FROM vietride_payment.operator_ledger_entries
     WHERE id = '${ids.paymentLedgerEntry}';`,
  );
  psql(
    'vietride_parcel',
    `BEGIN;
     SET LOCAL session_replication_role = replica;
     DELETE FROM vietride_parcel.integration_inbox
     WHERE message_id IN (${ownedMessageSql || 'NULL'});
     DELETE FROM vietride_parcel.outbox_events
     WHERE payload->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
     DELETE FROM vietride_parcel.parcel_status_history
     WHERE parcel_id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
     DELETE FROM vietride_parcel.parcels
     WHERE id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
     COMMIT;`,
  );
  psql(
    'vietride_booking',
    `
    DELETE FROM vietride_booking.outbox_events
    WHERE payload->>'bookingId' = '${ids.booking}';
    DELETE FROM vietride_booking.booking_status_history WHERE booking_id = '${ids.booking}';
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
    DELETE FROM vietride_trip.trip_cargo_parcels
    WHERE trip_id IN (${tripIds})
       OR parcel_id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
    DELETE FROM vietride_trip.trip_seats WHERE trip_id IN (${tripIds});
    DELETE FROM vietride_trip.trip_stops WHERE trip_id IN (${tripIds});
    DELETE FROM vietride_trip.trips WHERE id IN (${tripIds});
    DELETE FROM vietride_trip.vehicles WHERE id = '${ids.replacementVehicle}';
  `,
  );
  psql(
    'vietride_notification',
    `DELETE FROM vietride_notification.notification_deliveries
     WHERE notification_id IN (
       SELECT id FROM vietride_notification.notifications
       WHERE data->>'bookingId' = '${ids.booking}'
          OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}'));
     DELETE FROM vietride_notification.email_deliveries
     WHERE notification_id IN (
       SELECT id FROM vietride_notification.notifications
       WHERE data->>'bookingId' = '${ids.booking}'
          OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}'));
     DELETE FROM vietride_notification.notifications
     WHERE data->>'bookingId' = '${ids.booking}'
        OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}');
     DELETE FROM vietride_notification.processed_messages
     WHERE message_id IN (${ownedMessageSql || 'NULL'});`,
  );
  execFileSync(
    'docker',
    [
      'exec',
      'vietride_redis',
      'redis-cli',
      'DEL',
      ...[
        ids.substitutionKey,
        ids.capacityProbeKey,
        ids.crossTenantKey,
        ...ids.confirmationKeys,
        ids.lateConfirmationKey,
        ids.missingSeatConfirmationKey,
        ids.parcelConfirmKey,
      ].flatMap((key) => [`trip:idem:${key}`, `idempotency:${key}`]),
    ],
    { cwd: root, stdio: 'ignore' },
  );
  for (const messageId of ownedMessageIds) {
    const keys = execFileSync(
      'docker',
      [
        'exec',
        'vietride_redis',
        'redis-cli',
        '--scan',
        '--pattern',
        `notification:idem:*:${messageId}`,
      ],
      { cwd: root, encoding: 'utf8' },
    )
      .split(/\r?\n/)
      .filter(Boolean);
    if (keys.length > 0) {
      execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...keys], {
        cwd: root,
        stdio: 'ignore',
      });
    }
  }
  await quiesceOwnedMessagingArtifacts();
  psql(
    'vietride_identity',
    `DELETE FROM vietride_identity.users WHERE id = '${ids.admin}';`,
  );
  console.log('PASS | Day34 audit fixtures cleaned');
}

function verifyCleanup() {
  const tripIds = newTripId ? `'${ids.oldTrip}', '${newTripId}'` : `'${ids.oldTrip}'`;
  const remaining =
    Number(
      psql('vietride_trip', `SELECT count(*) FROM vietride_trip.trips WHERE id IN (${tripIds})`),
    ) +
    Number(
      psql(
        'vietride_booking',
        `SELECT count(*) FROM vietride_booking.bookings WHERE id = '${ids.booking}'`,
      ),
    ) +
    Number(
      psql(
        'vietride_parcel',
        `SELECT
           (SELECT count(*) FROM vietride_parcel.parcels
            WHERE id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')) +
           (SELECT count(*) FROM vietride_parcel.parcel_status_history
            WHERE parcel_id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}'))`,
      ),
    ) +
    Number(
      psql(
        'vietride_trip',
        `SELECT count(*) FROM vietride_trip.trip_cargo_parcels
         WHERE parcel_id IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')`,
      ),
    ) +
    Number(
      psql(
        'vietride_parcel',
        `SELECT
           (SELECT count(*) FROM vietride_parcel.outbox_events
            WHERE payload->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')) +
           (SELECT count(*) FROM vietride_parcel.integration_inbox
            WHERE message_id IN (${ownedMessageIds.map((id) => `'${id}'`).join(', ') || 'NULL'}))`,
      ),
    ) +
    Number(
      psql(
        'vietride_notification',
        `SELECT
           (SELECT count(*) FROM vietride_notification.notifications
            WHERE data->>'bookingId' = '${ids.booking}'
               OR data->>'parcelId' IN ('${ids.confirmedParcel}', '${ids.escalatedParcel}')) +
           (SELECT count(*) FROM vietride_notification.processed_messages
            WHERE message_id IN (${ownedMessageIds.map((id) => `'${id}'`).join(', ') || 'NULL'}))`,
      ),
    ) +
    Number(
      psql(
        'vietride_payment',
        `SELECT
           (SELECT count(*) FROM vietride_payment.operator_trip_settlements
            WHERE trip_id IN (${tripIds})) +
           (SELECT count(*) FROM vietride_payment.operator_ledger_entries
            WHERE id = '${ids.paymentLedgerEntry}')`,
      ),
    ) +
    Number(
      psql(
        'vietride_identity',
        `SELECT count(*) FROM vietride_identity.users WHERE id = '${ids.admin}'`,
      ),
    ) +
    countOwnedMessagingArtifacts();
  let redisRemaining = 0;
  for (const messageId of ownedMessageIds) {
    redisRemaining += execFileSync(
      'docker',
      [
        'exec',
        'vietride_redis',
        'redis-cli',
        '--scan',
        '--pattern',
        `notification:idem:*:${messageId}`,
      ],
      { cwd: root, encoding: 'utf8' },
    )
      .split(/\r?\n/)
      .filter(Boolean).length;
  }
  assertEqual(String(remaining + redisRemaining), '0', 'Day34-35 fixture cleanup');
  assertEqual(
    psql(
      'vietride_parcel',
      `SELECT tgenabled FROM pg_trigger WHERE tgname = 'trg_parcel_status_history_immutable';`,
    ),
    'O',
    'Parcel status-history immutability trigger restored after Day34-35 cleanup',
  );
  console.log('PASS | Day34-35 fixture cleanup verified');
}

let runError;
try {
  seed();
  await verifyInsufficientSeatGuard();
  await runNewman('Day34');
  await verifyDay34();
  await verifyTransferEscalationAndLateConfirmation();
  await verifyPaymentSettlementMarkers();
  await verifyDay35();
} catch (error) {
  runError = error;
} finally {
  try {
    await cleanup();
    verifyCleanup();
  } catch (cleanupError) {
    runError = runError
      ? new AggregateError([runError, cleanupError], 'Day34-35 run and cleanup failed')
      : cleanupError;
  }
}

if (runError) throw runError;
