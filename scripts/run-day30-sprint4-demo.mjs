// Day-30 Sprint-4 demo harness. Configuration fixtures and evidence queries use
// generated IDs; every business lifecycle transition goes through Gateway :3000.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SignJWT, importPKCS8 } from 'jose';

export const POLLING = Object.freeze({
  scheduleGeneration: Object.freeze({ intervalMs: 500, timeoutMs: 30_000 }),
  autoBoarding: Object.freeze({ intervalMs: 500, timeoutMs: 960_000 }),
  eventConsumption: Object.freeze({ intervalMs: 500, timeoutMs: 45_000 }),
});
export const REQUIRED_OUTBOX = Object.freeze([
  'trip.trip.boarding_started',
  'trip.trip.started',
  'parcel.parcel.loaded',
  'trip.stop.arrived',
  'parcel.parcel.unloaded',
  'trip.trip.completed',
]);
export const TRIP_STATES = Object.freeze(['SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED']);
export const PARCEL_STATES = Object.freeze(['PENDING', 'LOADED', 'IN_TRANSIT', 'UNLOADED']);

const root = process.cwd();
const evidencePath = path.join(root, 'docs/handoff/day-30-sprint4-evidence.md');
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const ids = Object.freeze({
  operator: crypto.randomUUID(),
  operatorAdmin: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  sender: crypto.randomUUID(),
  recipient: crypto.randomUUID(),
  subscriptionPlan: crypto.randomUUID(),
  subscription: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  stop: crypto.randomUUID(),
  route: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  parcel: crypto.randomUUID(),
});
const runTag = ids.operator.replaceAll('-', '').slice(0, 10).toUpperCase();
const phoneSeed = BigInt('0x' + runTag) % 100_000_000n;
const fixturePhone = (offset) =>
  '+849' + ((phoneSeed + BigInt(offset)) % 100_000_000n).toString().padStart(8, '0');
const phones = Object.freeze({
  operator: fixturePhone(0),
  operatorAdmin: fixturePhone(1),
  driver: fixturePhone(2),
  assistant: fixturePhone(3),
  sender: fixturePhone(4),
  recipient: fixturePhone(5),
});
const parcelCode = `VRP-${formatIctDate(new Date()).replaceAll('-', '')}-D30${runTag}`.slice(0, 30);
const issuedIdempotency = [];
let generatedScheduleId;
let generatedTripId;
const generatedTripIds = new Set();
let assertionCount = 0;
let preservedOutboxCounts;

export function assert(condition, message) {
  if (!condition) throw new Error(message);
  assertionCount += 1;
}

export function formatIctDate(value) {
  const shifted = new Date(value.getTime() + 7 * 60 * 60 * 1000);
  return [
    shifted.getUTCFullYear(),
    String(shifted.getUTCMonth() + 1).padStart(2, '0'),
    String(shifted.getUTCDate()).padStart(2, '0'),
  ].join('-');
}

export function chooseTargetSchedule(fixtureNow) {
  const shifted = new Date(fixtureNow.getTime() + 7 * 60 * 60 * 1000);
  const targetLocal = new Date(
    Date.UTC(shifted.getUTCFullYear(), shifted.getUTCMonth(), shifted.getUTCDate() + 1, 12),
  );
  const targetDate = [
    targetLocal.getUTCFullYear(),
    String(targetLocal.getUTCMonth() + 1).padStart(2, '0'),
    String(targetLocal.getUTCDate()).padStart(2, '0'),
  ].join('-');
  const jsDay = targetLocal.getUTCDay();
  const departureTime = '12:00:00';
  const departureDateTime = new Date(`${targetDate}T${departureTime}+07:00`);
  return {
    targetDate,
    dayOfWeek: jsDay === 0 ? 7 : jsDay,
    departureTime,
    departureDateTime,
  };
}

export function buildTimeAdvanceSql(tripId, scheduleId, fixtureNow) {
  const advanced = new Date(fixtureNow.getTime() + 29 * 60 * 1000).toISOString();
  return [
    'UPDATE vietride_trip.trips',
    `SET departure_date_time='${advanced}'`,
    `WHERE id='${tripId}'`,
    `  AND driver_schedule_id='${scheduleId}'`,
    "  AND status='SCHEDULED'",
    "  AND source='AUTO_FROM_SCHEDULE'",
    "RETURNING status::text || '|' || source::text || '|' || driver_schedule_id::text || '|' || departure_date_time::text;",
  ].join('\n');
}

export function idempotencyRedisKeys(prefix, key) {
  const hash = crypto.createHash('sha256').update(key).digest('hex').toUpperCase();
  return [`${prefix}:idem:v2:response:${hash}`, `${prefix}:idem:v2:processing:${hash}`];
}

function sqlLiteral(value) {
  return "'" + String(value).replaceAll("'", "''") + "'";
}

function sqlIn(values) {
  return '(' + values.map(sqlLiteral).join(',') + ')';
}

function knownTripIds() {
  return [...new Set([...generatedTripIds, generatedTripId].filter(Boolean))];
}

export function buildTripIdPredicate(column, tripIds) {
  return tripIds.length ? column + ' IN ' + sqlIn(tripIds) : 'FALSE';
}

function tripIdPredicate(column) {
  return buildTripIdPredicate(column, knownTripIds());
}

function schedulePredicate(column) {
  return generatedScheduleId ? column + '=' + sqlLiteral(generatedScheduleId) : 'FALSE';
}

function tripPayloadPredicate() {
  return tripIdPredicate("payload->>'tripId'");
}

export function buildRedactedSummary({
  failureInjection,
  cleanupResidue,
  outboxCounts,
  duplicateCounts,
  replayCount,
  duplicateTransitionCount,
  preAdvanceBeyondThirtyMinutes,
}) {
  const duplicateOutboxCount = Object.values(duplicateCounts).reduce(
    (total, value) => total + value,
    0,
  );
  return {
    redacted: true,
    result: failureInjection ? 'EXPECTED_FAILURE' : 'PASS',
    failureInjection,
    autoFromSchedule: true,
    preAdvanceBeyondThirtyMinutes,
    tripStates: [...TRIP_STATES],
    parcelStates: [...PARCEL_STATES],
    polling: POLLING,
    outboxCounts,
    duplicateCounts,
    replayCount,
    duplicateTransitionCount,
    duplicateOutboxCount,
    cleanupResidue,
  };
}

function redact(text) {
  return String(text)
    .replace(/Bearer\s+\S+/gi, 'Bearer [REDACTED]')
    .replace(/-----BEGIN[\s\S]*?PRIVATE KEY-----/gi, '[PRIVATE KEY REDACTED]')
    .replace(/Idempotency-Key\s*[:=]\s*\S+/gi, 'Idempotency-Key=[REDACTED]');
}

function psql(database, sql, label) {
  try {
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
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
    ).trim();
  } catch (error) {
    const detail = redact(error.stderr || '');
    throw new Error(`${label} failed${detail ? `: ${detail.trim()}` : ''}`);
  }
}

function redis(...args) {
  try {
    return execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', '--raw', ...args], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    }).trim();
  } catch {
    throw new Error('Redis fixture cleanup operation failed');
  }
}

function parseJson(value, label) {
  try {
    return JSON.parse(value);
  } catch {
    throw new Error(`${label} returned invalid JSON`);
  }
}

function queryJson(database, sql, label) {
  const value = psql(database, sql, label);
  assert(value.length > 0, `${label} returned no row`);
  return parseJson(value, label);
}

async function poll(label, probe, predicate, { intervalMs, timeoutMs }) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() <= deadline) {
    last = await probe();
    if (predicate(last)) {
      console.log(`PASS | ${label}`);
      return last;
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error(`${label} timed out`);
}

function readJwtOptions() {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const privateKey = process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt?.PrivateKey;
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt?.Kid;
  if (!privateKey || !kid) {
    throw new Error('Identity JWT development signing configuration is unavailable');
  }
  return { privateKey, kid };
}

async function issueToken(sub, role, operatorId = ids.operator) {
  const options = readJwtOptions();
  const key = await importPKCS8(options.privateKey, 'RS256');
  return new SignJWT({
    role,
    operatorId,
    email: `${role.toLowerCase()}@day30.local`,
    hasPhone: 'true',
  })
    .setProtectedHeader({ alg: 'RS256', kid: options.kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

async function post(pathname, jwt, { key, body } = {}) {
  const headers = {
    Authorization: `Bearer ${jwt}`,
    'X-Request-Id': crypto.randomUUID(),
  };
  if (key) headers['Idempotency-Key'] = key;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${baseUrl}${pathname}`, {
    method: 'POST',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const raw = await response.text();
  return {
    status: response.status,
    raw,
    body: raw ? parseJson(raw, pathname) : null,
  };
}

function newKey(prefix) {
  const key = crypto.randomUUID();
  issuedIdempotency.push({ prefix, key });
  return key;
}

function expectApi(result, expectedStatus, label) {
  assert(
    result.status === expectedStatus,
    `${label}: expected HTTP ${expectedStatus}, got ${result.status}`,
  );
  assert(result.body?.success === true, `${label}: success envelope missing`);
  assert(result.body?.statusCode === expectedStatus, `${label}: envelope status mismatch`);
  assert(typeof result.body?.meta?.traceId === 'string', `${label}: traceId missing`);
  assert(Number.isFinite(Date.parse(result.body?.meta?.timestamp)), `${label}: timestamp missing`);
  assert(result.body?.data && typeof result.body.data === 'object', `${label}: data missing`);
  console.log(`PASS | ${label} | HTTP ${expectedStatus}`);
  return result.body.data;
}

function seedPrerequisites() {
  psql(
    'vietride_identity',
    `
BEGIN;
INSERT INTO vietride_identity.operators
  (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,is_active,approved_at)
VALUES
  ('${ids.operator}','Day30 Operator ${runTag}','D30${runTag}','T30${runTag}','operator-${runTag}@day30.local','${phones.operator}','APPROVED',true,now());
INSERT INTO vietride_identity.users
  (id,email,phone,display_name,role,status,operator_id)
VALUES
  ('${ids.operatorAdmin}','admin-${runTag}@day30.local','${phones.operatorAdmin}','Day30 Operator Admin','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
  ('${ids.driver}','driver-${runTag}@day30.local','${phones.driver}','Day30 Driver','DRIVER','ACTIVE','${ids.operator}'),
  ('${ids.assistant}','assistant-${runTag}@day30.local','${phones.assistant}','Day30 Assistant','ASSISTANT','ACTIVE','${ids.operator}'),
  ('${ids.sender}','sender-${runTag}@day30.local','${phones.sender}','Day30 Sender','PASSENGER','ACTIVE',NULL),
  ('${ids.recipient}','recipient-${runTag}@day30.local','${phones.recipient}','Day30 Recipient','PASSENGER','ACTIVE',NULL);
INSERT INTO vietride_identity.subscription_plans
  (id,name,description,price_per_month,price_per_year,max_vehicles,max_drivers,max_assistants,max_operator_users,max_routes,max_trips_per_month,is_active)
VALUES
  ('${ids.subscriptionPlan}','Day30 fixture plan','Generated-ID demo quota fixture',0,0,20,20,20,20,20,20,true);
INSERT INTO vietride_identity.operator_subscriptions
  (id,operator_id,plan_id,status,started_at,expires_at)
VALUES
  ('${ids.subscription}','${ids.operator}','${ids.subscriptionPlan}','ACTIVE',now()-interval '1 day',now()+interval '30 days');
COMMIT;
`,
    'seed Identity prerequisites',
  );
  psql(
    'vietride_trip',
    `
BEGIN;
INSERT INTO vietride_trip.stations (id,name,slug,city,province) VALUES
  ('${ids.originStation}','Day30 Origin ${runTag}','day30-origin-${runTag.toLowerCase()}','Ho Chi Minh City','Ho Chi Minh City'),
  ('${ids.destinationStation}','Day30 Destination ${runTag}','day30-destination-${runTag.toLowerCase()}','Da Lat','Lam Dong');
INSERT INTO vietride_trip.stops (id,operator_id,name,address,latitude,longitude) VALUES
  ('${ids.stop}','${ids.operator}','Day30 Dropoff ${runTag}','Day30 generated fixture stop',10.77,106.70);
INSERT INTO vietride_trip.vehicle_types
  (id,code,display_name,estimated_passenger_luggage_kg_per_seat,default_seat_count,is_system_defined,is_active)
VALUES
  ('${ids.vehicleType}','DAY30_${runTag}','Day30 Fixture Vehicle',0,2,false,true);
INSERT INTO vietride_trip.routes
  (id,operator_id,name,origin_station_id,destination_station_id,base_fare,total_distance_km,estimated_duration_minutes,is_active)
VALUES
  ('${ids.route}','${ids.operator}','Day30 Route ${runTag}','${ids.originStation}','${ids.destinationStation}',200000,100,180,true);
INSERT INTO vietride_trip.route_stops
  (route_id,stop_id,order_index,estimated_duration_from_origin_minutes,distance_from_origin_km,allow_pickup,allow_dropoff)
VALUES
  ('${ids.route}','${ids.stop}',1,60,50,true,true);
INSERT INTO vietride_trip.vehicles
  (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active,max_cargo_weight_kg,max_cargo_volume_m3)
VALUES
  (
    '${ids.vehicle}','${ids.operator}','${ids.vehicleType}','D30${runTag}',
    '{"Version":1,"VehicleTypeCode":"DAY30","TotalSeats":2,"Rows":1,"Cols":2,"Decks":1,"Aisles":[],"Seats":[{"SeatNumber":"A01","Row":1,"Col":1,"Deck":1,"Type":"STANDARD","IsWindow":true,"IsAisle":false,"Disabled":false},{"SeatNumber":"A02","Row":1,"Col":2,"Deck":1,"Type":"STANDARD","IsWindow":false,"IsAisle":true,"Disabled":false}]}'::jsonb,
    2,'ACTIVE',true,20,1
  );
COMMIT;
`,
    'seed Trip configuration prerequisites',
  );
  console.log('PASS | generated-ID Identity and Trip configuration prerequisites seeded');
}

async function createScheduleAndAwaitGeneratedTrip(tokens, fixtureNow) {
  const target = chooseTargetSchedule(fixtureNow);
  assert(
    target.targetDate > formatIctDate(fixtureNow),
    'targetDate must be at least tomorrow in ICT',
  );
  assert(
    target.departureDateTime.getTime() <= fixtureNow.getTime() + 14 * 24 * 60 * 60 * 1000,
    'targetDate exceeds the 14-day generation horizon',
  );
  assert(
    target.departureDateTime.getTime() > fixtureNow.getTime() + 30 * 60 * 1000,
    'generated departure fixture must start outside auto-boarding threshold',
  );
  const created = await post('/v1/operator/driver-schedules', tokens.operatorAdmin, {
    body: {
      routeId: ids.route,
      vehicleId: ids.vehicle,
      driverUserId: ids.driver,
      assistantUserId: ids.assistant,
      dayOfWeek: [target.dayOfWeek],
      departureTime: target.departureTime,
      validFrom: target.targetDate,
      validUntil: target.targetDate,
      isActive: true,
    },
  });
  const schedule = expectApi(created, 201, 'operator creates active one-occurrence DriverSchedule');
  assert(typeof schedule.id === 'string', 'DriverSchedule response id missing');
  generatedScheduleId = schedule.id;
  assert(schedule.operatorId === ids.operator, 'DriverSchedule operator mismatch');
  assert(schedule.routeId === ids.route, 'DriverSchedule route mismatch');
  assert(schedule.vehicleId === ids.vehicle, 'DriverSchedule vehicle mismatch');
  assert(schedule.driverUserId === ids.driver, 'DriverSchedule driver mismatch');
  assert(schedule.assistantUserId === ids.assistant, 'DriverSchedule assistant mismatch');
  assert(
    JSON.stringify(schedule.dayOfWeek) === JSON.stringify([target.dayOfWeek]),
    'DriverSchedule weekday mismatch',
  );
  assert(schedule.validFrom === target.targetDate, 'DriverSchedule validFrom mismatch');
  assert(schedule.validUntil === target.targetDate, 'DriverSchedule validUntil mismatch');
  const tripJson = await poll(
    'exactly one AUTO_FROM_SCHEDULE Trip generated from created schedule',
    () =>
      psql(
        'vietride_trip',
        `SELECT coalesce(json_agg(json_build_object(
          'id',id,
          'operatorId',operator_id,
          'routeId',route_id,
          'vehicleId',vehicle_id,
          'driverUserId',driver_user_id,
          'assistantUserId',assistant_user_id,
          'driverScheduleId',driver_schedule_id,
          'departureDate',to_char(departure_date_time AT TIME ZONE 'Asia/Bangkok','YYYY-MM-DD'),
          'departureDateTime',departure_date_time,
          'status',status,
          'source',source
        ) ORDER BY created_at),'[]'::json)::text
        FROM vietride_trip.trips WHERE driver_schedule_id='${generatedScheduleId}'`,
        'poll generated Trip',
      ),
    (value) => parseJson(value, 'generated Trip poll').length === 1,
    POLLING.scheduleGeneration,
  );
  const trips = parseJson(tripJson, 'generated Trip evidence');
  for (const trip of trips) {
    if (typeof trip.id === 'string') generatedTripIds.add(trip.id);
  }
  assert(trips.length === 1, 'schedule must generate exactly one Trip');
  const trip = trips[0];
  generatedTripId = trip.id;
  assert(trip.source === 'AUTO_FROM_SCHEDULE', 'generated Trip source mismatch');
  assert(trip.status === 'SCHEDULED', 'generated Trip initial status mismatch');
  assert(trip.driverScheduleId === generatedScheduleId, 'generated Trip schedule link mismatch');
  assert(trip.departureDate === target.targetDate, 'generated Trip ICT date mismatch');
  assert(trip.operatorId === ids.operator, 'generated Trip operator mismatch');
  assert(trip.routeId === ids.route, 'generated Trip route mismatch');
  assert(trip.vehicleId === ids.vehicle, 'generated Trip vehicle mismatch');
  assert(trip.driverUserId === ids.driver, 'generated Trip driver mismatch');
  assert(trip.assistantUserId === ids.assistant, 'generated Trip assistant mismatch');
  const departure = new Date(trip.departureDateTime);
  const leadMs = departure.getTime() - fixtureNow.getTime();
  assert(leadMs > 30 * 60 * 1000, 'generated Trip raced auto-boarding before fixture helper');
  const graph = psql(
    'vietride_trip',
    `SELECT
      (SELECT count(*) FROM vietride_trip.trip_stops WHERE trip_id='${generatedTripId}' AND stop_id='${ids.stop}' AND status='PENDING') || '|' ||
      (SELECT count(*) FROM vietride_trip.trip_seats WHERE trip_id='${generatedTripId}' AND seat_number IN ('A01','A02') AND status='AVAILABLE')`,
    'generated Trip graph evidence',
  );
  assert(graph === '1|2', `generated Trip graph mismatch: ${graph}`);
  console.log(
    'PASS | generated Trip linkage, SCHEDULED proof, one TripStop, and generated seats verified',
  );
  return { target, preAdvanceBeyondThirtyMinutes: leadMs > 30 * 60 * 1000 };
}

async function awaitAutoBoarding(fixtureNow) {
  const updated = psql(
    'vietride_trip',
    buildTimeAdvanceSql(generatedTripId, generatedScheduleId, fixtureNow),
    'Fixture-only time advance',
  );
  const fields = updated.split('|');
  assert(
    fields.length === 4,
    'Fixture-only time advance did not update exactly one generated Trip',
  );
  assert(fields[0] === 'SCHEDULED', 'Fixture helper changed Trip status');
  assert(fields[1] === 'AUTO_FROM_SCHEDULE', 'Fixture helper lost generated source proof');
  assert(fields[2] === generatedScheduleId, 'Fixture helper lost DriverSchedule proof');
  console.log('PASS | Fixture-only time advance set departure to fixtureNow + 29 minutes');
  await poll(
    'production AutoBoardingJob reaches BOARDING with exactly one boarding Outbox row',
    () =>
      psql(
        'vietride_trip',
        `SELECT
          (SELECT status::text FROM vietride_trip.trips WHERE id='${generatedTripId}') || '|' ||
          (SELECT count(*) FROM vietride_trip.outbox_events WHERE event_type='trip.trip.boarding_started' AND payload->>'tripId'='${generatedTripId}')`,
        'poll AutoBoarding',
      ),
    (value) => value === 'BOARDING|1',
    POLLING.autoBoarding,
  );
}

function attachPendingParcel() {
  psql(
    'vietride_parcel',
    `INSERT INTO vietride_parcel.parcels
      (id,parcel_code,sender_user_id,recipient_user_id,recipient_name,recipient_phone,operator_id,trip_id,dropoff_stop_id,size_category,estimated_length_cm,estimated_width_cm,estimated_height_cm,estimated_weight_kg,estimated_volume_m3,estimated_dim_weight_kg,estimated_chargeable_weight_kg,total_price_vnd,deposit_amount,original_deposit_amount,status)
    VALUES
      ('${ids.parcel}','${parcelCode}','${ids.sender}','${ids.recipient}','Day30 Recipient','${phones.recipient}','${ids.operator}','${generatedTripId}','${ids.stop}','SMALL',1,1,1,1,0.001,0.01,0.01,10000,10000,10000,'PENDING')`,
    'attach PENDING Parcel fixture',
  );
  psql(
    'vietride_trip',
    `BEGIN;
    UPDATE vietride_trip.trips
      SET reserved_parcel_weight_kg=1,reserved_parcel_volume_m3=0.001
      WHERE id='${generatedTripId}' AND status='BOARDING' AND driver_schedule_id='${generatedScheduleId}' AND source='AUTO_FROM_SCHEDULE';
    INSERT INTO vietride_trip.trip_cargo_parcels
      (trip_id,parcel_id,weight_kg,volume_m3,state)
    VALUES
      ('${generatedTripId}','${ids.parcel}',1,0.001,'RESERVED');
    COMMIT;`,
    'attach Parcel cargo fixture',
  );
  const proof = psql(
    'vietride_trip',
    `SELECT
      (SELECT count(*) FROM vietride_trip.trips WHERE id='${generatedTripId}' AND status='BOARDING' AND reserved_parcel_weight_kg=1) || '|' ||
      (SELECT count(*) FROM vietride_trip.trip_cargo_parcels WHERE trip_id='${generatedTripId}' AND parcel_id='${ids.parcel}' AND state='RESERVED')`,
    'PENDING Parcel fixture proof',
  );
  assert(proof === '1|1', 'PENDING Parcel fixture/cargo reservation mismatch');
  console.log('PASS | one isolated PENDING Parcel attached to generated Trip');
}

async function runLifecycle(tokens) {
  attachPendingParcel();
  const loadKey = newKey('parcel');
  const loaded = await post(`/v1/assistant/parcels/${ids.parcel}/load`, tokens.assistant, {
    key: loadKey,
    body: { tripId: generatedTripId, parcelCode },
  });
  const loadData = expectApi(loaded, 200, 'assigned Assistant loads Parcel');
  assert(loadData.parcelId === ids.parcel, 'load Parcel id mismatch');
  assert(loadData.parcelCode === parcelCode, 'load Parcel code mismatch');
  assert(loadData.status === 'LOADED', 'load Parcel status mismatch');
  const loadedProof = psql(
    'vietride_parcel',
    `SELECT count(*) FROM vietride_parcel.parcels WHERE id='${ids.parcel}' AND status='LOADED' AND loaded_at IS NOT NULL`,
    'LOADED persistence proof',
  );
  assert(loadedProof === '1', 'LOADED persistence proof missing');

  const started = await post(`/v1/driver/trips/${generatedTripId}/start`, tokens.driver, {
    key: newKey('trip'),
  });
  const startData = expectApi(started, 200, 'assigned Driver starts BOARDING Trip');
  assert(startData.tripId === generatedTripId, 'start Trip id mismatch');
  assert(startData.status === 'IN_PROGRESS', 'start Trip status mismatch');
  assert(Number.isFinite(Date.parse(startData.actualDepartureTime)), 'start timestamp missing');
  await poll(
    'TripStarted event consumption moves Parcel to IN_TRANSIT',
    () =>
      psql(
        'vietride_parcel',
        `SELECT status::text FROM vietride_parcel.parcels WHERE id='${ids.parcel}'`,
        'poll Parcel IN_TRANSIT',
      ),
    (value) => value === 'IN_TRANSIT',
    POLLING.eventConsumption,
  );

  const arrived = await post(
    `/v1/driver/trips/${generatedTripId}/stops/${ids.stop}/arrive`,
    tokens.driver,
    { key: newKey('trip') },
  );
  const arriveData = expectApi(arrived, 200, 'assigned Driver arrives at Parcel drop-off TripStop');
  assert(arriveData.tripId === generatedTripId, 'arrival Trip id mismatch');
  assert(arriveData.stopId === ids.stop, 'arrival stop id mismatch');
  assert(arriveData.status === 'ARRIVED', 'arrival status mismatch');
  assert(Number.isFinite(Date.parse(arriveData.actualArrivalTime)), 'arrival timestamp missing');

  const unloaded = await post(`/v1/assistant/parcels/${ids.parcel}/unload`, tokens.assistant, {
    key: newKey('parcel'),
  });
  const unloadData = expectApi(unloaded, 200, 'assigned Assistant unloads Parcel');
  assert(unloadData.parcelId === ids.parcel, 'unload Parcel id mismatch');
  assert(unloadData.parcelCode === parcelCode, 'unload Parcel code mismatch');
  assert(unloadData.status === 'UNLOADED', 'unload Parcel status mismatch');
  await poll(
    'Parcel unload releases generated Trip cargo counters',
    () =>
      psql(
        'vietride_trip',
        `SELECT reserved_parcel_weight_kg || '|' || total_loaded_weight_kg || '|' ||
          (SELECT state FROM vietride_trip.trip_cargo_parcels WHERE trip_id='${generatedTripId}' AND parcel_id='${ids.parcel}')
        FROM vietride_trip.trips WHERE id='${generatedTripId}'`,
        'poll cargo release',
      ),
    (value) => value === '0.00|0.00|RELEASED',
    POLLING.eventConsumption,
  );

  const completionKey = newKey('trip');
  const completed = await post(`/v1/driver/trips/${generatedTripId}/complete`, tokens.driver, {
    key: completionKey,
  });
  const completeData = expectApi(completed, 200, 'assigned Driver completes Trip');
  assert(completeData.tripId === generatedTripId, 'completion Trip id mismatch');
  assert(completeData.status === 'COMPLETED', 'completion status mismatch');
  assert(completeData.completedByUserId === ids.driver, 'completion actor mismatch');
  assert(Number.isFinite(Date.parse(completeData.completedAt)), 'completion timestamp missing');
  const replay = await post(`/v1/driver/trips/${generatedTripId}/complete`, tokens.driver, {
    key: completionKey,
  });
  expectApi(replay, 200, 'same-key Trip completion replay');
  assert(replay.raw === completed.raw, 'completion replay response was not byte-identical');

  await poll(
    'all required Day-30 Outbox rows are persisted exactly once',
    () => collectOutboxCounts(),
    (counts) => REQUIRED_OUTBOX.every((eventType) => counts[eventType] === 1),
    POLLING.eventConsumption,
  );
  const completionAuditCount = Number(
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id='${generatedTripId}' AND action='TRIP_COMPLETED_MANUAL'`,
      'completion audit count',
    ),
  );
  assert(completionAuditCount === 1, 'completion replay duplicated transition audit');
  const finalParcel = psql(
    'vietride_parcel',
    `SELECT status::text || '|' || (unloaded_at IS NOT NULL)::text FROM vietride_parcel.parcels WHERE id='${ids.parcel}'`,
    'final Parcel evidence',
  );
  assert(finalParcel === 'UNLOADED|true', 'final Parcel state/timestamp mismatch');
  console.log('PASS | completion replay verified; lifecycle and cargo evidence correlated');
  preservedOutboxCounts = collectOutboxCounts();
  return { replayCount: 1, duplicateTransitionCount: completionAuditCount - 1 };
}

function collectOutboxCounts() {
  const counts = Object.fromEntries(REQUIRED_OUTBOX.map((eventType) => [eventType, 0]));
  const tripRows = parseJson(
    psql(
      'vietride_trip',
      `SELECT coalesce(json_object_agg(event_type,total),'{}'::json)::text FROM (
        SELECT event_type,count(*)::int AS total
        FROM vietride_trip.outbox_events
        WHERE payload->>'tripId'='${generatedTripId}'
        GROUP BY event_type
      ) rows`,
      'Trip Outbox counts',
    ),
    'Trip Outbox counts',
  );
  const parcelRows = parseJson(
    psql(
      'vietride_parcel',
      `SELECT coalesce(json_object_agg(event_type,total),'{}'::json)::text FROM (
        SELECT event_type,count(*)::int AS total
        FROM vietride_parcel.outbox_events
        WHERE payload->>'tripId'='${generatedTripId}' OR payload->>'parcelId'='${ids.parcel}'
        GROUP BY event_type
      ) rows`,
      'Parcel Outbox counts',
    ),
    'Parcel Outbox counts',
  );
  for (const eventType of REQUIRED_OUTBOX) {
    counts[eventType] = Number(tripRows[eventType] || 0) + Number(parcelRows[eventType] || 0);
  }
  return counts;
}

function collectEventRows() {
  const trip = parseJson(
    psql(
      'vietride_trip',
      `SELECT coalesce(json_agg(json_build_object('id',id,'type',event_type)),'[]'::json)::text
      FROM vietride_trip.outbox_events
      WHERE ${tripPayloadPredicate()} OR ${schedulePredicate("payload->>'driverScheduleId'")}`,
      'collect Trip event cleanup identities',
    ) || '[]',
    'Trip event cleanup identities',
  );
  const parcel = parseJson(
    psql(
      'vietride_parcel',
      `SELECT coalesce(json_agg(json_build_object('id',id,'type',event_type)),'[]'::json)::text
      FROM vietride_parcel.outbox_events
      WHERE ${tripPayloadPredicate()} OR payload->>'parcelId'='${ids.parcel}'`,
      'collect Parcel event cleanup identities',
    ) || '[]',
    'Parcel event cleanup identities',
  );
  return [...trip, ...parcel];
}

function discoverGeneratedTripIds() {
  if (!generatedScheduleId) return;
  const tripIds = psql(
    'vietride_trip',
    `SELECT coalesce(json_agg(id ORDER BY created_at),'[]'::json)::text
     FROM vietride_trip.trips WHERE ${schedulePredicate('driver_schedule_id')}`,
    'discover generated Trip cleanup identities',
  );
  for (const tripId of parseJson(tripIds, 'generated Trip cleanup identities')) {
    if (typeof tripId === 'string') generatedTripIds.add(tripId);
  }
}

function redisCleanupKeys(eventRows) {
  const keys = issuedIdempotency.flatMap(({ prefix, key }) => idempotencyRedisKeys(prefix, key));
  for (const row of eventRows) {
    keys.push(`notification:idem:processed:${row.type}:${row.id}`);
    keys.push(`notification:idem:processing:${row.type}:${row.id}`);
  }
  return [...new Set(keys)];
}

function cleanupPass(eventRows) {
  discoverGeneratedTripIds();
  psql(
    'vietride_notification',
    `DELETE FROM vietride_notification.notification_deliveries
      WHERE notification_id IN (
        SELECT id FROM vietride_notification.notifications
        WHERE ${tripIdPredicate("data->>'tripId'")} OR data->>'parcelId'='${ids.parcel}'
      );
    DELETE FROM vietride_notification.email_deliveries
      WHERE notification_id IN (
        SELECT id FROM vietride_notification.notifications
        WHERE ${tripIdPredicate("data->>'tripId'")} OR data->>'parcelId'='${ids.parcel}'
      );
    DELETE FROM vietride_notification.notifications
      WHERE ${tripIdPredicate("data->>'tripId'")} OR data->>'parcelId'='${ids.parcel}';`,
    'cleanup Notification artifacts',
  );
  psql(
    'vietride_payment',
    `DELETE FROM vietride_payment.operator_trip_settlements WHERE ${tripIdPredicate('trip_id')};`,
    'cleanup Payment settlement artifact',
  );
  psql(
    'vietride_parcel',
    `DELETE FROM vietride_parcel.outbox_events
      WHERE ${tripPayloadPredicate()} OR payload->>'parcelId'='${ids.parcel}';
    DELETE FROM vietride_parcel.parcels WHERE id='${ids.parcel}';`,
    'cleanup Parcel artifacts',
  );
  psql(
    'vietride_trip',
    `DELETE FROM vietride_trip.trip_cargo_parcels WHERE ${tripIdPredicate('trip_id')} OR parcel_id='${ids.parcel}';
    DELETE FROM vietride_trip.trip_stops WHERE ${tripIdPredicate('trip_id')};
    DELETE FROM vietride_trip.trip_seats WHERE ${tripIdPredicate('trip_id')};
    DELETE FROM vietride_trip.trip_stop_fares WHERE ${tripIdPredicate('trip_id')};
    DELETE FROM vietride_trip.trip_audit_logs WHERE ${tripIdPredicate('trip_id')};
    DELETE FROM vietride_trip.outbox_events
      WHERE ${tripPayloadPredicate()} OR ${schedulePredicate("payload->>'driverScheduleId'")};
    DELETE FROM vietride_trip.trips WHERE ${tripIdPredicate('id')} OR ${schedulePredicate('driver_schedule_id')};
    DELETE FROM vietride_trip.trip_generation_skip_logs WHERE ${schedulePredicate('driver_schedule_id')};
    DELETE FROM vietride_trip.driver_schedule_audit_logs WHERE ${schedulePredicate('driver_schedule_id')};
    DELETE FROM vietride_trip.driver_schedules WHERE ${schedulePredicate('id')};
    DELETE FROM vietride_trip.route_stops WHERE route_id='${ids.route}';
    DELETE FROM vietride_trip.routes WHERE id='${ids.route}';
    DELETE FROM vietride_trip.stops WHERE id='${ids.stop}';
    DELETE FROM vietride_trip.vehicles WHERE id='${ids.vehicle}';
    DELETE FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}';
    DELETE FROM vietride_trip.stations WHERE id IN ('${ids.originStation}','${ids.destinationStation}');`,
    'cleanup Trip artifacts',
  );
  psql(
    'vietride_identity',
    `DELETE FROM vietride_identity.subscription_quota_allocations WHERE operator_id='${ids.operator}';
    DELETE FROM vietride_identity.operator_subscriptions WHERE operator_id='${ids.operator}';
    DELETE FROM vietride_identity.subscription_plans WHERE id='${ids.subscriptionPlan}';
    DELETE FROM vietride_identity.users
      WHERE id IN ('${ids.operatorAdmin}','${ids.driver}','${ids.assistant}','${ids.sender}','${ids.recipient}');
    DELETE FROM vietride_identity.operators WHERE id='${ids.operator}';`,
    'cleanup Identity artifacts',
  );
  const redisKeys = redisCleanupKeys(eventRows);
  if (redisKeys.length) redis('DEL', ...redisKeys);
}

async function cleanupAndVerify() {
  discoverGeneratedTripIds();
  const eventRows = collectEventRows();
  cleanupPass(eventRows);
  await new Promise((resolve) => setTimeout(resolve, 1_000));
  cleanupPass(eventRows);
  const residue = [
    Number(
      psql(
        'vietride_identity',
        `SELECT
          (SELECT count(*) FROM vietride_identity.operators WHERE id='${ids.operator}') +
          (SELECT count(*) FROM vietride_identity.users WHERE id IN ('${ids.operatorAdmin}','${ids.driver}','${ids.assistant}','${ids.sender}','${ids.recipient}')) +
          (SELECT count(*) FROM vietride_identity.operator_subscriptions WHERE operator_id='${ids.operator}') +
          (SELECT count(*) FROM vietride_identity.subscription_quota_allocations WHERE operator_id='${ids.operator}') +
          (SELECT count(*) FROM vietride_identity.subscription_plans WHERE id='${ids.subscriptionPlan}')`,
        'verify Identity cleanup',
      ),
    ),
    Number(
      psql(
        'vietride_trip',
        `SELECT
          (SELECT count(*) FROM vietride_trip.trips WHERE ${tripIdPredicate('id')} OR ${schedulePredicate('driver_schedule_id')}) +
          (SELECT count(*) FROM vietride_trip.driver_schedules WHERE ${schedulePredicate('id')}) +
          (SELECT count(*) FROM vietride_trip.driver_schedule_audit_logs WHERE ${schedulePredicate('driver_schedule_id')}) +
          (SELECT count(*) FROM vietride_trip.trip_generation_skip_logs WHERE ${schedulePredicate('driver_schedule_id')}) +
          (SELECT count(*) FROM vietride_trip.route_stops WHERE route_id='${ids.route}') +
          (SELECT count(*) FROM vietride_trip.routes WHERE id='${ids.route}') +
          (SELECT count(*) FROM vietride_trip.vehicles WHERE id='${ids.vehicle}') +
          (SELECT count(*) FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}') +
          (SELECT count(*) FROM vietride_trip.stations WHERE id IN ('${ids.originStation}','${ids.destinationStation}')) +
          (SELECT count(*) FROM vietride_trip.stops WHERE id='${ids.stop}') +
          (SELECT count(*) FROM vietride_trip.outbox_events WHERE ${tripPayloadPredicate()} OR ${schedulePredicate("payload->>'driverScheduleId'")})`,
        'verify Trip cleanup',
      ),
    ),
    Number(
      psql(
        'vietride_parcel',
        `SELECT
          (SELECT count(*) FROM vietride_parcel.parcels WHERE id='${ids.parcel}') +
          (SELECT count(*) FROM vietride_parcel.outbox_events WHERE ${tripPayloadPredicate()} OR payload->>'parcelId'='${ids.parcel}')`,
        'verify Parcel cleanup',
      ),
    ),
    Number(
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications
          WHERE ${tripIdPredicate("data->>'tripId'")} OR data->>'parcelId'='${ids.parcel}'`,
        'verify Notification cleanup',
      ),
    ),
    Number(
      psql(
        'vietride_payment',
        `SELECT count(*) FROM vietride_payment.operator_trip_settlements WHERE ${tripIdPredicate('trip_id')}`,
        'verify Payment cleanup',
      ),
    ),
  ].reduce((total, value) => total + value, 0);
  const redisKeys = redisCleanupKeys(eventRows);
  const redisResidue = redisKeys.length ? Number(redis('EXISTS', ...redisKeys)) : 0;
  const total = residue + redisResidue;
  assert(total === 0, `Cleanup verified residue must be zero, got ${total}`);
  console.log('PASS | Cleanup verified for exact generated IDs and event/idempotency artifacts');
  return total;
}

function writeEvidence(summary, failureInjection) {
  if (failureInjection) {
    const content = [
      '# Day-30 Sprint-4 demo evidence',
      '',
      'AUTO_FROM_SCHEDULE generated-Trip proof completed before fixture adjustment.',
      'Fixture-only time advance changed only the generated Trip departure timestamp.',
      'trip.trip.boarding_started and trip.trip.completed were each correlated exactly once.',
      'completion replay verified with the same runtime UUID-v4 key.',
      'Cleanup verified after injected failure.',
      'DAY30_FAILURE_INJECTION=EXECUTED',
      `Failure-injection summary JSON: ${JSON.stringify(summary)}`,
      '',
      `Assertions passed: ${assertionCount}`,
      'Final result: FAIL',
      '',
    ].join('\n');
    fs.writeFileSync(evidencePath, content, 'utf8');
    return;
  }
  const previous = fs.existsSync(evidencePath) ? fs.readFileSync(evidencePath, 'utf8') : '';
  const failureLine = previous
    .split(/\r?\n/)
    .find((line) => line.startsWith('Failure-injection summary JSON: '));
  if (!failureLine) {
    throw new Error('Failure-injection evidence must be generated before the normal run');
  }
  const content = [
    '# Day-30 Sprint-4 demo evidence',
    '',
    'AUTO_FROM_SCHEDULE generated-Trip proof completed before fixture adjustment.',
    'Fixture-only time advance changed only the generated Trip departure timestamp.',
    'Trip state evidence: SCHEDULED -> BOARDING -> IN_PROGRESS -> COMPLETED.',
    'Parcel state evidence: PENDING -> LOADED -> IN_TRANSIT -> UNLOADED.',
    'Required Outbox evidence: trip.trip.boarding_started, trip.trip.started, parcel.parcel.loaded, trip.stop.arrived, parcel.parcel.unloaded, trip.trip.completed.',
    'completion replay verified with the same runtime UUID-v4 key.',
    'Cleanup verified after both paths; credentials and raw idempotency keys are excluded.',
    'DAY30_FAILURE_INJECTION=EXECUTED',
    'DAY30_RUN=PASS',
    failureLine,
    `Normal run summary JSON: ${JSON.stringify(summary)}`,
    '',
    `Assertions passed: ${assertionCount}`,
    'Final result: PASS',
    '',
  ].join('\n');
  fs.writeFileSync(evidencePath, content, 'utf8');
}

async function runDemo(failureInjection) {
  const fixtureNow = new Date();
  let journey;
  let lifecycle;
  let runError;
  let cleanupError;
  let injected = false;
  try {
    seedPrerequisites();
    const [operatorAdmin, driver, assistant] = await Promise.all([
      issueToken(ids.operatorAdmin, 'OPERATOR_ADMIN'),
      issueToken(ids.driver, 'DRIVER'),
      issueToken(ids.assistant, 'ASSISTANT'),
    ]);
    const tokens = { operatorAdmin, driver, assistant };
    journey = await createScheduleAndAwaitGeneratedTrip(tokens, fixtureNow);
    await awaitAutoBoarding(fixtureNow);
    lifecycle = await runLifecycle(tokens);
    if (failureInjection) {
      injected = true;
      throw new Error('DAY30_EXPECTED_FAILURE_INJECTION');
    }
  } catch (error) {
    if (!(injected && error.message === 'DAY30_EXPECTED_FAILURE_INJECTION')) {
      runError = error;
    }
  } finally {
    try {
      var cleanupResidue = await cleanupAndVerify();
    } catch (error) {
      cleanupError = error;
    }
  }
  if (runError || cleanupError) {
    const failure = runError || cleanupError;
    console.error(`DAY30_RUN=FAIL | ${redact(failure.message)}`);
    throw failure;
  }
  const outboxCounts = collectSummaryCountsFromJourney();
  const duplicateCounts = Object.fromEntries(
    REQUIRED_OUTBOX.map((eventType) => [eventType, Math.max(0, outboxCounts[eventType] - 1)]),
  );
  const summary = buildRedactedSummary({
    failureInjection,
    cleanupResidue,
    outboxCounts,
    duplicateCounts,
    replayCount: lifecycle.replayCount,
    duplicateTransitionCount: lifecycle.duplicateTransitionCount,
    preAdvanceBeyondThirtyMinutes: journey.preAdvanceBeyondThirtyMinutes,
  });
  for (const eventType of REQUIRED_OUTBOX) {
    assert(summary.outboxCounts[eventType] === 1, `summary Outbox count mismatch: ${eventType}`);
    assert(
      summary.duplicateCounts[eventType] === 0,
      `summary duplicate count mismatch: ${eventType}`,
    );
  }
  assert(summary.duplicateTransitionCount === 0, 'summary duplicate transition count mismatch');
  assert(summary.cleanupResidue === 0, 'summary cleanup residue mismatch');
  writeEvidence(summary, failureInjection);
  console.log(failureInjection ? 'DAY30_FAILURE_INJECTION=EXECUTED' : 'DAY30_RUN=PASS');
  console.log(`DAY30_REDACTED_SUMMARY=${JSON.stringify(summary)}`);
}

function collectSummaryCountsFromJourney() {
  assert(preservedOutboxCounts, 'pre-cleanup Outbox summary was not preserved');
  return preservedOutboxCounts;
}

export async function main() {
  const failureInjection = process.argv.slice(2).includes('--verify-cleanup-failure');
  await runDemo(failureInjection);
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))
) {
  await main();
}
