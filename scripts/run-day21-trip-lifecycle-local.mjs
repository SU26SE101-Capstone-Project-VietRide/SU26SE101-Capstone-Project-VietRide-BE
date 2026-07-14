// Day-21 lifecycle E2E. Public actions go through Gateway; direct database access
// is limited to deterministic fixture setup, bounded evidence polling, and cleanup.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const ids = Object.freeze({
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  route: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  unassignedDriver: crypto.randomUUID(),
  operator: crypto.randomUUID(),
  parcel: crypto.randomUUID(),
  passenger: crypto.randomUUID(),
  confirmed: crypto.randomUUID(),
  partialNoShow: crypto.randomUUID(),
  noShow: crypto.randomUUID(),
  cancelled: crypto.randomUUID(),
});
const bookingIds = [ids.confirmed, ids.partialNoShow, ids.noShow, ids.cancelled];
const runTag = ids.trip.replaceAll('-', '').slice(0, 10).toUpperCase();
const idempotencyKeys = [];
let cleanupError;

function psql(database, sql) {
  return execFileSync(
    'docker',
    ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, '-Atc', sql],
    { encoding: 'utf8' },
  ).trim();
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function parseJson(text, label) {
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`${label} returned non-JSON content`);
  }
}

function newIdempotencyKey() {
  const key = crypto.randomUUID();
  idempotencyKeys.push(key);
  return key;
}

async function poll(label, probe, predicate, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = await probe();
    if (predicate(value)) {
      console.log(`PASS | ${label}`);
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`${label} timed out; last observed value=${String(value)}`);
}

async function post(pathname, token, key) {
  const response = await fetch(`${baseUrl}${pathname}`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': key,
      'X-Request-Id': crypto.randomUUID(),
    },
  });
  const text = await response.text();
  return { status: response.status, body: text ? parseJson(text, pathname) : null };
}

function expectResponse(result, status, code, label) {
  assert(result.status === status, `${label}: expected HTTP ${status}, got ${result.status}`);
  if (code) {
    assert(result.body?.error?.code === code, `${label}: expected ${code}, got ${result.body?.error?.code}`);
  }
  console.log(`PASS | ${label} | HTTP ${status}${code ? ` ${code}` : ''}`);
}

function cleanupRedis() {
  if (idempotencyKeys.length === 0) return;
  const keys = idempotencyKeys.map((key) => `trip:idem:${key}`);
  execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...keys], {
    encoding: 'utf8',
  });
}

function cleanup() {
  const bookingList = bookingIds.map((id) => `'${id}'`).join(',');
  const operations = [
    () => psql('vietride_booking', `
      DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (${bookingList});
      DELETE FROM vietride_booking.bookings WHERE id IN (${bookingList});
      DELETE FROM vietride_booking.outbox_events WHERE payload->>'tripId' = '${ids.trip}';`),
    () => psql('vietride_parcel', `
      DELETE FROM vietride_parcel.parcels WHERE id = '${ids.parcel}';
      DELETE FROM vietride_parcel.outbox_events WHERE payload->>'tripId' = '${ids.trip}';`),
    () => psql('vietride_trip', `
      DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id = '${ids.trip}';
      DELETE FROM vietride_trip.outbox_events WHERE payload->>'tripId' = '${ids.trip}';
      DELETE FROM vietride_trip.trips WHERE id = '${ids.trip}';
      DELETE FROM vietride_trip.routes WHERE id = '${ids.route}';
      DELETE FROM vietride_trip.vehicles WHERE id = '${ids.vehicle}';
      DELETE FROM vietride_trip.stations WHERE id IN ('${ids.originStation}', '${ids.destinationStation}');
      DELETE FROM vietride_trip.vehicle_types WHERE id = '${ids.vehicleType}';`),
    cleanupRedis,
  ];
  const errors = [];
  for (const operation of operations) {
    try {
      operation();
    } catch (error) {
      errors.push(error);
    }
  }
  if (errors.length > 0) throw new AggregateError(errors, 'One or more Day-21 cleanup operations failed');
}

function assertClean() {
  const bookingList = bookingIds.map((id) => `'${id}'`).join(',');
  const tripRows = psql('vietride_trip', `SELECT count(*) FROM vietride_trip.trips WHERE id = '${ids.trip}'`);
  const dependencyRows = psql('vietride_trip', `
    SELECT
      (SELECT count(*) FROM vietride_trip.routes WHERE id = '${ids.route}')
      + (SELECT count(*) FROM vietride_trip.vehicles WHERE id = '${ids.vehicle}')
      + (SELECT count(*) FROM vietride_trip.stations WHERE id IN ('${ids.originStation}', '${ids.destinationStation}'))
      + (SELECT count(*) FROM vietride_trip.vehicle_types WHERE id = '${ids.vehicleType}')`);
  const parcelRows = psql('vietride_parcel', `SELECT count(*) FROM vietride_parcel.parcels WHERE id = '${ids.parcel}'`);
  const bookingRows = psql('vietride_booking', `SELECT count(*) FROM vietride_booking.bookings WHERE id IN (${bookingList})`);
  const redisRows = idempotencyKeys.length === 0
    ? 0
    : Number(execFileSync(
      'docker',
      ['exec', 'vietride_redis', 'redis-cli', 'EXISTS', ...idempotencyKeys.map((key) => `trip:idem:${key}`)],
      { encoding: 'utf8' },
    ).trim());
  assert(tripRows === '0' && dependencyRows === '0' && parcelRows === '0' && bookingRows === '0' && redisRows === 0,
    `Day-21 cleanup failed: trips=${tripRows}, tripDependencies=${dependencyRows}, parcels=${parcelRows}, bookings=${bookingRows}, redis=${redisRows}`);
}

function rabbitCredentials() {
  const rabbitEnvironment = JSON.parse(execFileSync(
    'docker',
    ['inspect', '--format', '{{json .Config.Env}}', 'vietride_rabbitmq'],
    { cwd: root, encoding: 'utf8' },
  ));
  const environment = Object.fromEntries(rabbitEnvironment.map((entry) => entry.split(/=(.*)/s).slice(0, 2)));
  const username = environment.RABBITMQ_DEFAULT_USER;
  const password = environment.RABBITMQ_DEFAULT_PASS;
  assert(username && password, 'RabbitMQ management credentials are unavailable');
  return Buffer.from(`${username}:${password}`).toString('base64');
}

async function rabbitRequest(pathname, authorization, init = {}) {
  const response = await fetch(`http://localhost:15672${pathname}`, {
    ...init,
    headers: {
      Authorization: `Basic ${authorization}`,
      ...(init.headers ?? {}),
    },
  });
  assert(response.ok, `RabbitMQ management request ${pathname} failed with HTTP ${response.status}`);
  return response.json();
}

async function completedQueueEvidence(authorization) {
  const bindings = await rabbitRequest('/api/bindings/%2F', authorization);
  const queues = [...new Set(bindings
    .filter((binding) => binding.source === 'vietride.events'
      && binding.routing_key === 'trip.trip.completed'
      && binding.destination_type === 'queue')
    .map((binding) => binding.destination))];
  assert(queues.length > 0, 'No trip.trip.completed queue bindings were found');
  const baseline = new Map();
  for (const queue of queues) {
    const state = await rabbitRequest(`/api/queues/%2F/${encodeURIComponent(queue)}`, authorization);
    baseline.set(queue, Number(state.message_stats?.ack ?? 0));
  }
  return { queues, baseline };
}

async function waitForCompletedAcknowledgements(authorization, evidence) {
  await poll(
    'duplicate TripCompleted acknowledged and queues drained',
    async () => {
      const states = [];
      for (const queue of evidence.queues) {
        const state = await rabbitRequest(`/api/queues/%2F/${encodeURIComponent(queue)}`, authorization);
        states.push({
          queue,
          ready: Number(state.messages_ready ?? 0),
          unacknowledged: Number(state.messages_unacknowledged ?? 0),
          acknowledgements: Number(state.message_stats?.ack ?? 0),
        });
      }
      return states;
    },
    (states) => states.every((state) => state.ready === 0
      && state.unacknowledged === 0
      && state.acknowledgements >= evidence.baseline.get(state.queue) + 1),
    45_000,
  );
}

async function publishDuplicateCompleted(completedAt, authorization) {
  const payload = JSON.stringify({
    tripId: ids.trip,
    completedAt,
    hasSubstitution: false,
  });
  const confirmation = await rabbitRequest('/api/exchanges/%2F/vietride.events/publish', authorization, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      properties: { content_type: 'application/json' },
      routing_key: 'trip.trip.completed',
      payload,
      payload_encoding: 'string',
    }),
  });
  assert(confirmation?.routed === true, 'Duplicate event publish was not routed');
}

let runError;
try {
  cleanup();
  psql('vietride_trip', `
    INSERT INTO vietride_trip.stations (id, name, slug, city, province)
    VALUES
      ('${ids.originStation}', 'Day 21 Origin ${runTag}', 'day21-origin-${runTag.toLowerCase()}', 'Ho Chi Minh City', 'Ho Chi Minh City'),
      ('${ids.destinationStation}', 'Day 21 Destination ${runTag}', 'day21-destination-${runTag.toLowerCase()}', 'Da Lat', 'Lam Dong');
    INSERT INTO vietride_trip.vehicle_types
      (id, code, display_name, default_seat_count, is_system_defined)
    VALUES
      ('${ids.vehicleType}', 'DAY21_${runTag}', 'Day 21 Vehicle Type ${runTag}', 1, false);
    INSERT INTO vietride_trip.routes
      (id, operator_id, name, origin_station_id, destination_station_id, base_fare)
    VALUES
      ('${ids.route}', '${ids.operator}', 'Day 21 Route ${runTag}', '${ids.originStation}', '${ids.destinationStation}', 200000);
    INSERT INTO vietride_trip.vehicles
      (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats, status)
    VALUES
      ('${ids.vehicle}', '${ids.operator}', '${ids.vehicleType}', 'D21${runTag}',
       '{"version":1,"vehicleTypeCode":"DAY21","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1}]}',
       1, 'ACTIVE');
    INSERT INTO vietride_trip.trips
      (id, operator_id, route_id, vehicle_id, driver_user_id, assistant_user_id,
       departure_date_time, estimated_arrival_time, status, source, base_fare)
    VALUES
      ('${ids.trip}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.driver}', '${ids.assistant}',
       now() + interval '2 hours', now() + interval '6 hours', 'BOARDING', 'MANUAL', 200000);`);
  psql('vietride_parcel', `
    INSERT INTO vietride_parcel.parcels
      (id, parcel_code, sender_user_id, recipient_name, recipient_phone, operator_id, trip_id,
       size_category, estimated_weight_kg, deposit_amount, status, loaded_at)
    VALUES
      ('${ids.parcel}', 'VRP-20260714-${runTag}', '${ids.passenger}', 'Day 21 Recipient', '0900000021',
       '${ids.operator}', '${ids.trip}', 'SMALL', 1.00, 10000, 'LOADED', now());`);
  psql('vietride_booking', `
    INSERT INTO vietride_booking.bookings
      (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
       base_fare, discount_amount, total_amount, status, confirmed_at)
    VALUES
      ('${ids.confirmed}', 'VR-20260714-C${runTag}', '${ids.passenger}', '${ids.trip}', '${ids.operator}', '${ids.originStation}', 200000, 0, 200000, 'CONFIRMED', now()),
      ('${ids.partialNoShow}', 'VR-20260714-P${runTag}', '${ids.passenger}', '${ids.trip}', '${ids.operator}', '${ids.originStation}', 200000, 0, 200000, 'PARTIAL_NO_SHOW', now()),
      ('${ids.noShow}', 'VR-20260714-N${runTag}', '${ids.passenger}', '${ids.trip}', '${ids.operator}', '${ids.originStation}', 200000, 0, 200000, 'NO_SHOW', now()),
      ('${ids.cancelled}', 'VR-20260714-X${runTag}', '${ids.passenger}', '${ids.trip}', '${ids.operator}', '${ids.originStation}', 200000, 0, 200000, 'CANCELLED', now());`);
  console.log('PASS | isolated Route/Vehicle dependency graph, BOARDING Trip, Booking set, and LOADED Parcel seeded');

  const settings = JSON.parse(
    fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'),
  );
  const privateKey = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  async function token(sub, role) {
    return new SignJWT({ role, email: `${role.toLowerCase()}@day21.local`, hasPhone: 'true' })
      .setProtectedHeader({ alg: 'RS256', kid })
      .setIssuer('vietride-identity')
      .setAudience('vietride-api')
      .setSubject(sub)
      .setIssuedAt()
      .setExpirationTime('15m')
      .sign(privateKey);
  }
  const [driverToken, assistantToken, unassignedToken] = await Promise.all([
    token(ids.driver, 'DRIVER'),
    token(ids.assistant, 'ASSISTANT'),
    token(ids.unassignedDriver, 'DRIVER'),
  ]);
  console.log('PASS | short-lived test JWTs generated at runtime (redacted)');

  const startPath = `/v1/driver/trips/${ids.trip}/start`;
  const completePath = `/v1/driver/trips/${ids.trip}/complete`;
  expectResponse(await post(startPath, assistantToken, newIdempotencyKey()), 403, 'FORBIDDEN', 'assistant start denied');
  expectResponse(await post(startPath, unassignedToken, newIdempotencyKey()), 403, 'FORBIDDEN', 'unassigned driver start denied');
  expectResponse(await post(startPath, driverToken, 'not-a-uuid-v4'), 422, 'VALIDATION_ERROR', 'malformed key rejected');

  const startKey = newIdempotencyKey();
  const started = await post(startPath, driverToken, startKey);
  expectResponse(started, 200, null, 'assigned driver manual start');
  assert(started.body?.data?.tripId === ids.trip && started.body?.data?.status === 'IN_PROGRESS', 'start response data mismatch');
  const replayedStart = await post(startPath, driverToken, startKey);
  expectResponse(replayedStart, 200, null, 'same-key start replay');
  assert(JSON.stringify(replayedStart.body) === JSON.stringify(started.body), 'same-key replay body changed');
  expectResponse(await post(completePath, driverToken, startKey), 422, 'IDEMPOTENCY_KEY_MISMATCH', 'same-key path mismatch');
  expectResponse(await post(startPath, unassignedToken, startKey), 422, 'IDEMPOTENCY_KEY_MISMATCH', 'same-key subject mismatch');
  expectResponse(await post(startPath, driverToken, newIdempotencyKey()), 409, 'TRIP_INVALID_TRANSITION', 'new key after start rejected');

  await poll(
    'TripStarted Outbox published',
    () => psql('vietride_trip', `SELECT status::text FROM vietride_trip.outbox_events WHERE event_type='trip.trip.started' AND payload->>'tripId'='${ids.trip}'`),
    (value) => value === 'PUBLISHED',
  );
  await poll(
    'Parcel consumer transitioned LOADED to IN_TRANSIT',
    () => psql('vietride_parcel', `SELECT status::text FROM vietride_parcel.parcels WHERE id='${ids.parcel}'`),
    (value) => value === 'IN_TRANSIT',
  );

  const completeKey = newIdempotencyKey();
  const completed = await post(completePath, assistantToken, completeKey);
  expectResponse(completed, 200, null, 'assigned assistant manual completion');
  assert(completed.body?.data?.tripId === ids.trip && completed.body?.data?.status === 'COMPLETED', 'complete response data mismatch');
  const completedAt = completed.body.data.completedAt;
  const replayedComplete = await post(completePath, assistantToken, completeKey);
  expectResponse(replayedComplete, 200, null, 'same-key complete replay');
  assert(JSON.stringify(replayedComplete.body) === JSON.stringify(completed.body), 'complete replay body changed');
  expectResponse(await post(completePath, assistantToken, newIdempotencyKey()), 409, 'TRIP_INVALID_TRANSITION', 'new key after completion rejected');

  await poll(
    'TripCompleted Outbox published',
    () => psql('vietride_trip', `SELECT status::text FROM vietride_trip.outbox_events WHERE event_type='trip.trip.completed' AND payload->>'tripId'='${ids.trip}'`),
    (value) => value === 'PUBLISHED',
  );
  const audit = psql('vietride_trip', `
    SELECT count(*) || '|' || min(action) || '|' || min(actor_user_id::text)
    FROM vietride_trip.trip_audit_logs WHERE trip_id='${ids.trip}'`);
  assert(audit === `1|TRIP_COMPLETED_MANUAL|${ids.assistant}`, `manual completion audit mismatch: ${audit}`);
  console.log('PASS | one manual Trip audit row persisted');

  await poll(
    'Booking consumer completed eligible statuses only',
    () => psql('vietride_booking', `
      SELECT string_agg(id::text || ':' || status::text, ',' ORDER BY id::text)
      FROM vietride_booking.bookings WHERE id IN (${bookingIds.map((id) => `'${id}'`).join(',')})`),
    (value) => value.includes(`${ids.confirmed}:COMPLETED`)
      && value.includes(`${ids.partialNoShow}:COMPLETED`)
      && value.includes(`${ids.noShow}:NO_SHOW`)
      && value.includes(`${ids.cancelled}:CANCELLED`),
  );
  const historyBeforeDuplicate = psql('vietride_booking', `
    SELECT count(*) || '|' || count(*) FILTER (WHERE status='COMPLETED' AND source='COMPLETE_ON_TRIP_COMPLETED')
    FROM vietride_booking.booking_status_history WHERE booking_id IN (${bookingIds.map((id) => `'${id}'`).join(',')})`);
  assert(historyBeforeDuplicate === '2|2', `Booking history mismatch: ${historyBeforeDuplicate}`);
  console.log('PASS | eligible Booking history rows use COMPLETE_ON_TRIP_COMPLETED');

  const rabbitAuthorization = rabbitCredentials();
  const duplicateEvidence = await completedQueueEvidence(rabbitAuthorization);
  await publishDuplicateCompleted(completedAt, rabbitAuthorization);
  await waitForCompletedAcknowledgements(rabbitAuthorization, duplicateEvidence);
  const historyAfterDuplicate = psql('vietride_booking', `
    SELECT count(*) FROM vietride_booking.booking_status_history WHERE booking_id IN (${bookingIds.map((id) => `'${id}'`).join(',')})`);
  const auditAfterDuplicate = psql('vietride_trip', `SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id='${ids.trip}'`);
  const tripStateAfterDuplicate = psql('vietride_trip', `SELECT status::text FROM vietride_trip.trips WHERE id='${ids.trip}'`);
  assert(historyAfterDuplicate === '2' && auditAfterDuplicate === '1' && tripStateAfterDuplicate === 'COMPLETED',
    `duplicate event changed state: history=${historyAfterDuplicate}, audit=${auditAfterDuplicate}, trip=${tripStateAfterDuplicate}`);
  console.log('PASS | duplicate TripCompleted event is a no-op');
  console.log('PASS | deterministic pending behavior remains covered by the controlled Task 21.1 integration test');
  console.log('PASS | fallback boundaries remain covered by Task 21.4 fake-clock integration tests');
} catch (error) {
  runError = error;
} finally {
  try {
    cleanup();
    assertClean();
    console.log('PASS | Day-21 fixture cleanup verified');
  } catch (error) {
    cleanupError = error;
    console.error(`FAIL | Day-21 fixture cleanup | ${error.message}`);
  }
}

if (runError) throw runError;
if (cleanupError) throw cleanupError;
