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
      status, source, base_fare,
      total_loaded_weight_kg, total_loaded_volume_m3, created_at, updated_at)
    VALUES (
      '${ids.oldTrip}', '${ids.operator}',
      '40000000-0000-4000-8000-000000000411',
      '40000000-0000-4000-8000-000000000402',
      '${ids.driver}', now() - interval '30 minutes', now() + interval '3 hours',
      now() - interval '30 minutes', 'IN_PROGRESS', 'MANUAL', 100000,
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
    'vietride_parcel',
    `INSERT INTO vietride_parcel.parcels (
       id, parcel_code, sender_user_id, recipient_name, recipient_phone,
       operator_id, trip_id, size_category, estimated_size_category,
       estimated_length_cm, estimated_width_cm, estimated_height_cm,
       estimated_weight_kg, estimated_volume_m3, estimated_dim_weight_kg,
       estimated_chargeable_weight_kg, deposit_amount, status, loaded_at)
     VALUES
       ('${ids.confirmedParcel}', '${confirmedParcelCode}', '${ids.owner}',
        'Day 35 Confirm Recipient', '0900003501', '${ids.operator}', '${ids.oldTrip}',
        'SMALL', 'SMALL', 30.00, 20.00, 5.00,
        2.25, 0.0030, 0.50, 2.25, 0, 'LOADED', now()),
       ('${ids.escalatedParcel}', '${escalatedParcelCode}', '${ids.owner}',
        'Day 35 Escalate Recipient', '0900003502', '${ids.operator}', '${ids.oldTrip}',
        'MEDIUM', 'MEDIUM', 40.00, 20.00, 5.00,
        3.25, 0.0040, 0.67, 3.25, 0, 'IN_TRANSIT', now());`,
  );
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

function verifyDay34() {
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
  console.log('PASS | persisted 5:3:2:1 flow, Outbox identities, suppression, legacy code');
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
        ids.crossTenantKey,
        ...ids.confirmationKeys,
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
  await runNewman('Day34');
  verifyDay34();
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
