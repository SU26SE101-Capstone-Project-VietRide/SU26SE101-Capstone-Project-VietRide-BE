// Day-29 Sprint-4 lifecycle E2E. Fixture setup/cleanup is bounded to generated IDs;
// every lifecycle mutation after setup goes through the public Gateway.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const ids = Object.freeze({
  operator: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  stop: crypto.randomUUID(),
  secondStop: crypto.randomUUID(),
  route: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  unauthorizedDriver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  outsider: crypto.randomUUID(),
  operatorAdmin: crypto.randomUUID(),
  foreignOperator: crypto.randomUUID(),
  foreignAssistant: crypto.randomUUID(),
  sender: crypto.randomUUID(),
  recipient: crypto.randomUUID(),
  parcels: [crypto.randomUUID(), crypto.randomUUID(), crypto.randomUUID()],
});
const runTag = ids.trip.replaceAll('-', '').slice(0, 10).toUpperCase();
const cargoProbeQueue = `day29-cargo-${runTag.toLowerCase()}`;
const parcelCodes = ids.parcels.map((_, index) =>
  `VRP-20260722-D29${runTag}${index + 1}`.slice(0, 30),
);
let assertionCount = 0;
let runError;
let cleanupError;
let cargoProbeCreated = false;

export function assert(condition, message) {
  if (!condition) throw new Error(message);
  assertionCount += 1;
}
export function assertIsoTimestamp(value, label) {
  assert(
    typeof value === 'string' && value.length > 0 && Number.isFinite(Date.parse(value)),
    `${label}: expected ISO-8601 timestamp`,
  );
}
export function assertExactObjectKeys(value, expectedKeys, label) {
  assert(
    value !== null && typeof value === 'object' && !Array.isArray(value),
    `${label}: object missing`,
  );
  const actualKeys = Object.keys(value).sort();
  const sortedExpected = [...expectedKeys].sort();
  assert(
    JSON.stringify(actualKeys) === JSON.stringify(sortedExpected),
    `${label}: expected keys ${sortedExpected.join(',')}, got ${actualKeys.join(',')}`,
  );
}
export function assertApiEnvelope(result, expectedStatus, label, errorCode = null) {
  assert(
    result.status === expectedStatus,
    `${label}: expected HTTP ${expectedStatus}, got ${result.status}`,
  );
  const body = result.body;
  assert(body !== null && typeof body === 'object', `${label}: response body missing`);
  assert(body.statusCode === expectedStatus, `${label}: envelope statusCode mismatch`);
  assert(body.success === (errorCode === null), `${label}: envelope success mismatch`);
  assert(
    typeof body.meta?.traceId === 'string' && body.meta.traceId.length > 0,
    `${label}: meta.traceId missing`,
  );
  assertIsoTimestamp(body.meta?.timestamp, `${label} meta.timestamp`);
  if (errorCode === null) {
    assert(body.data !== null && typeof body.data === 'object', `${label}: success data missing`);
  } else {
    assert(
      body.error?.code === errorCode,
      `${label}: expected ${errorCode}, got ${body.error?.code}`,
    );
    assert(
      typeof body.error.message === 'string' && body.error.message.trim().length > 0,
      `${label}: error.message missing`,
    );
  }
  return body;
}
export function assertCargoEvent(payload, expected) {
  assertExactObjectKeys(
    payload,
    [
      'eventId',
      'occurredAt',
      'tripId',
      'operatorId',
      'loadedWeightKg',
      'maxCargoWeightKg',
      'percentFull',
    ],
    'cargo threshold payload',
  );
  assert(
    typeof payload.eventId === 'string' && /^[0-9a-f-]{36}$/i.test(payload.eventId),
    'cargo eventId must be UUID',
  );
  assertIsoTimestamp(payload.occurredAt, 'cargo occurredAt');
  assert(payload.tripId === expected.tripId, 'cargo tripId mismatch');
  assert(payload.operatorId === expected.operatorId, 'cargo operatorId mismatch');
  for (const field of ['loadedWeightKg', 'maxCargoWeightKg', 'percentFull'])
    assert(
      typeof payload[field] === 'number' && Number.isFinite(payload[field]),
      `cargo ${field} must be numeric`,
    );
  assert(payload.loadedWeightKg === expected.loadedWeightKg, 'cargo loadedWeightKg mismatch');
  assert(payload.maxCargoWeightKg === expected.maxCargoWeightKg, 'cargo maxCargoWeightKg mismatch');
  assert(payload.percentFull === expected.percentFull, 'cargo percentFull mismatch');
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
    { encoding: 'utf8' },
  ).trim();
}
function rabbitAdmin(...args) {
  try {
    return execFileSync(
      'docker',
      [
        'exec',
        '-e',
        'DAY29_RABBITMQ_USER',
        '-e',
        'DAY29_RABBITMQ_PASSWORD',
        'vietride_rabbitmq',
        'sh',
        '-c',
        'exec rabbitmqadmin -u "$DAY29_RABBITMQ_USER" -p "$DAY29_RABBITMQ_PASSWORD" --format=raw_json "$@"',
        'rabbitmqadmin',
        ...args,
      ],
      {
        encoding: 'utf8',
        env: {
          ...process.env,
          DAY29_RABBITMQ_USER: process.env.RABBITMQ_USER || 'vietride',
          DAY29_RABBITMQ_PASSWORD: process.env.RABBITMQ_PASSWORD || 'vietride_dev',
        },
        stdio: ['ignore', 'pipe', 'pipe'],
      },
    ).trim();
  } catch {
    throw new Error('RabbitMQ management operation failed');
  }
}
function cargoProbeExists() {
  try {
    const queues = execFileSync(
      'docker',
      ['exec', 'vietride_rabbitmq', 'rabbitmqctl', 'list_queues', 'name', '--silent'],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
    );
    return queues
      .split(/\r?\n/)
      .map((value) => value.trim())
      .includes(cargoProbeQueue);
  } catch {
    throw new Error('RabbitMQ cargo probe verification failed');
  }
}
function deleteCargoProbe() {
  if (!cargoProbeCreated) return;
  try {
    rabbitAdmin('delete', 'queue', `name=${cargoProbeQueue}`);
  } catch (error) {
    if (cargoProbeExists()) throw error;
  }
  assert(!cargoProbeExists(), 'RabbitMQ cargo probe queue cleanup failed');
  if (!cargoProbeExists()) {
    cargoProbeCreated = false;
  }
}
function queryJson(database, sql, label) {
  const value = psql(database, sql);
  assert(value.length > 0, `${label}: query returned no row`);
  return parseJson(value, label);
}
function parseJson(text, label) {
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`${label} returned non-JSON content`);
  }
}
function uuidV4() {
  return crypto.randomUUID();
}
async function token(sub, role, operatorId = ids.operator) {
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
    email: `${role.toLowerCase()}@day29.local`,
    hasPhone: 'true',
  })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(privateKey);
}
async function post(pathname, jwt, key, body) {
  const headers = {
    Authorization: `Bearer ${jwt}`,
    'Idempotency-Key': key,
    'X-Request-Id': crypto.randomUUID(),
  };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${baseUrl}${pathname}`, {
    method: 'POST',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: text ? parseJson(text, pathname) : null };
}
function expect(result, status, code, label) {
  assertApiEnvelope(result, status, label, code);
  console.log(`PASS | ${label} | HTTP ${status}${code ? ` ${code}` : ''}`);
}
function assertNoResourceDisclosure(result, label) {
  assert(
    result.body?.data === undefined || result.body.data === null,
    `${label}: response disclosed data`,
  );
  assert(
    result.body?.error?.fields === undefined,
    `${label}: response disclosed validation fields`,
  );
}
async function poll(label, probe, predicate, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = await probe();
    if (predicate(value)) {
      console.log(`PASS | ${label}`);
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error(`${label} timed out; last=${String(value)}`);
}
function cleanup() {
  const parcelList = ids.parcels.map((id) => `'${id}'`).join(',');
  const operations = [
    () =>
      psql(
        'vietride_notification',
        `DELETE FROM vietride_notification.notification_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE data->>'tripId'='${ids.trip}' OR data->>'parcelId' IN (${parcelList})); DELETE FROM vietride_notification.email_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE data->>'tripId'='${ids.trip}' OR data->>'parcelId' IN (${parcelList})); DELETE FROM vietride_notification.notifications WHERE data->>'tripId'='${ids.trip}' OR data->>'parcelId' IN (${parcelList});`,
      ),
    () =>
      psql(
        'vietride_identity',
        `DELETE FROM vietride_identity.users WHERE id IN ('${ids.driver}','${ids.unauthorizedDriver}','${ids.assistant}','${ids.outsider}','${ids.operatorAdmin}','${ids.foreignAssistant}','${ids.sender}','${ids.recipient}'); DELETE FROM vietride_identity.operators WHERE id IN ('${ids.operator}','${ids.foreignOperator}');`,
      ),
    () =>
      psql(
        'vietride_parcel',
        `DELETE FROM vietride_parcel.parcels WHERE id IN (${parcelList}); DELETE FROM vietride_parcel.outbox_events WHERE payload->>'tripId'='${ids.trip}';`,
      ),
    () =>
      psql(
        'vietride_trip',
        `DELETE FROM vietride_trip.trip_cargo_parcels WHERE trip_id='${ids.trip}'; DELETE FROM vietride_trip.trip_stops WHERE trip_id='${ids.trip}'; DELETE FROM vietride_trip.trip_seats WHERE trip_id='${ids.trip}'; DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id='${ids.trip}'; DELETE FROM vietride_trip.outbox_events WHERE payload->>'tripId'='${ids.trip}'; DELETE FROM vietride_trip.trips WHERE id='${ids.trip}'; DELETE FROM vietride_trip.route_stops WHERE route_id='${ids.route}'; DELETE FROM vietride_trip.routes WHERE id='${ids.route}'; DELETE FROM vietride_trip.stops WHERE id IN ('${ids.stop}','${ids.secondStop}'); DELETE FROM vietride_trip.stations WHERE id IN ('${ids.originStation}','${ids.destinationStation}'); DELETE FROM vietride_trip.vehicles WHERE id='${ids.vehicle}'; DELETE FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}';`,
      ),
    () => deleteCargoProbe(),
  ];
  const errors = [];
  for (const operation of operations) {
    try {
      operation();
    } catch (error) {
      errors.push(error);
    }
  }
  if (errors.length) throw new AggregateError(errors, 'Day-29 cleanup failed');
}
function assertClean() {
  const parcelList = ids.parcels.map((id) => `'${id}'`).join(',');
  const parcels = psql(
    'vietride_parcel',
    `SELECT count(*) FROM vietride_parcel.parcels WHERE id IN (${parcelList})`,
  );
  const trip = psql(
    'vietride_trip',
    `SELECT count(*) FROM vietride_trip.trips WHERE id='${ids.trip}'`,
  );
  const identity = psql(
    'vietride_identity',
    `SELECT count(*) FROM vietride_identity.users WHERE id IN ('${ids.driver}','${ids.unauthorizedDriver}','${ids.assistant}','${ids.outsider}','${ids.operatorAdmin}','${ids.foreignAssistant}','${ids.sender}','${ids.recipient}')`,
  );
  const notifications = psql(
    'vietride_notification',
    `SELECT count(*) FROM vietride_notification.notifications WHERE data->>'tripId'='${ids.trip}' OR data->>'parcelId' IN (${parcelList})`,
  );
  assert(
    parcels === '0' && trip === '0' && identity === '0' && notifications === '0',
    `cleanup verification failed parcels=${parcels} trip=${trip} identity=${identity} notifications=${notifications}`,
  );
}
function createCargoProbe() {
  rabbitAdmin('declare', 'queue', `name=${cargoProbeQueue}`, 'durable=false', 'auto_delete=false');
  cargoProbeCreated = true;
  rabbitAdmin(
    'declare',
    'binding',
    'source=vietride.events',
    'destination_type=queue',
    `destination=${cargoProbeQueue}`,
    'routing_key=trip.cargo.threshold_crossed',
  );
}
function seedFixture() {
  cleanup();
  createCargoProbe();
  psql(
    'vietride_identity',
    `
    INSERT INTO vietride_identity.operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at) VALUES
      ('${ids.operator}','Day29 Operator ${runTag}','D29${runTag}','TAX${runTag}','operator-${runTag}@day29.local','+84900000290','APPROVED',now()),
      ('${ids.foreignOperator}','Day29 Foreign Operator ${runTag}','D29F${runTag}','FTAX${runTag}','foreign-operator-${runTag}@day29.local','+84900000300','APPROVED',now());
    INSERT INTO vietride_identity.users (id,email,phone,display_name,role,status,operator_id) VALUES
      ('${ids.driver}','driver-${runTag}@day29.local','+84900000291','Day29 Driver','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.unauthorizedDriver}','unauthorized-driver-${runTag}@day29.local','+84900000296','Day29 Unauthorized Driver','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.assistant}','assistant-${runTag}@day29.local','+84900000292','Day29 Assistant','ASSISTANT','ACTIVE','${ids.operator}'),
      ('${ids.outsider}','outsider-${runTag}@day29.local','+84900000293','Day29 Outsider','ASSISTANT','ACTIVE','${ids.operator}'),
      ('${ids.operatorAdmin}','operator-admin-${runTag}@day29.local','+84900000297','Day29 Operator Admin','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
      ('${ids.foreignAssistant}','foreign-assistant-${runTag}@day29.local','+84900000301','Day29 Foreign Assistant','ASSISTANT','ACTIVE','${ids.foreignOperator}'),
      ('${ids.sender}','sender-${runTag}@day29.local','+84900000294','Day29 Sender','PASSENGER','ACTIVE',NULL),
      ('${ids.recipient}','recipient-${runTag}@day29.local','+84900000295','Day29 Recipient','PASSENGER','ACTIVE',NULL);`,
  );
  psql(
    'vietride_trip',
    `
    INSERT INTO vietride_trip.stations (id,name,slug,city,province) VALUES
      ('${ids.originStation}','Day29 Origin ${runTag}','day29-origin-${runTag.toLowerCase()}','Ho Chi Minh City','Ho Chi Minh City'),
      ('${ids.destinationStation}','Day29 Destination ${runTag}','day29-destination-${runTag.toLowerCase()}','Da Lat','Lam Dong');
    INSERT INTO vietride_trip.stops (id,operator_id,name,address,latitude,longitude) VALUES
      ('${ids.stop}','${ids.operator}','Day29 Dropoff ${runTag}','Day29 fixture stop',10.77,106.70),
      ('${ids.secondStop}','${ids.operator}','Day29 Wrong Stop ${runTag}','Day29 wrong-stop fixture',10.78,106.71);
    INSERT INTO vietride_trip.vehicle_types (id,code,display_name,default_seat_count,is_system_defined) VALUES ('${ids.vehicleType}','DAY29_${runTag}','Day29 Fixture Vehicle',4,false);
    INSERT INTO vietride_trip.routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare) VALUES ('${ids.route}','${ids.operator}','Day29 Route ${runTag}','${ids.originStation}','${ids.destinationStation}',200000);
    INSERT INTO vietride_trip.route_stops (route_id,stop_id,order_index,estimated_duration_from_origin_minutes,distance_from_origin_km,allow_pickup,allow_dropoff) VALUES
      ('${ids.route}','${ids.stop}',1,30,30,true,true),
      ('${ids.route}','${ids.secondStop}',2,60,60,true,true);
    INSERT INTO vietride_trip.vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,max_cargo_weight_kg) VALUES ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','D29${runTag}','{"version":1,"totalSeats":4,"rows":2,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1}]}',4,'ACTIVE',7);
    INSERT INTO vietride_trip.trips (id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare,max_cargo_weight_kg,estimated_passenger_luggage_kg,reserved_parcel_weight_kg,total_loaded_weight_kg) VALUES ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}','${ids.assistant}',now()-interval '1 minute',now()+interval '4 hours','BOARDING','MANUAL',200000,7,0,6,0);
    INSERT INTO vietride_trip.trip_stops (trip_id,stop_id,order_index,estimated_arrival_time,status,allow_pickup,allow_dropoff,distance_from_origin_km) VALUES
      ('${ids.trip}','${ids.stop}',1,now()+interval '30 minutes','PENDING',true,true,30),
      ('${ids.trip}','${ids.secondStop}',2,now()+interval '60 minutes','PENDING',true,true,60);
    INSERT INTO vietride_trip.trip_seats (trip_id,seat_number,status) VALUES ('${ids.trip}','A01','AVAILABLE');`,
  );
  const parcelValues = ids.parcels
    .map(
      (id, index) =>
        `('${id}','${parcelCodes[index]}','${ids.sender}','${ids.recipient}','Day29 Recipient','+84900000299','${ids.operator}','${ids.trip}','${ids.stop}', 'SMALL',1,1,1,1,0.001,0.01,0.01,10000,10000,10000,'PENDING')`,
    )
    .join(',');
  psql(
    'vietride_parcel',
    `INSERT INTO vietride_parcel.parcels (id,parcel_code,sender_user_id,recipient_user_id,recipient_name,recipient_phone,operator_id,trip_id,dropoff_stop_id,size_category,estimated_length_cm,estimated_width_cm,estimated_height_cm,estimated_weight_kg,estimated_volume_m3,estimated_dim_weight_kg,estimated_chargeable_weight_kg,total_price_vnd,deposit_amount,original_deposit_amount,status) VALUES ${parcelValues};`,
  );
  psql(
    'vietride_trip',
    `INSERT INTO vietride_trip.trip_cargo_parcels (trip_id,parcel_id,weight_kg,volume_m3,state) VALUES ${ids.parcels.map((id) => `('${ids.trip}','${id}',2,0.001,'RESERVED')`).join(',')};`,
  );
  console.log('PASS | isolated operator-owned Trip graph and exactly three PENDING parcels seeded');
}
async function runJourney() {
  seedFixture();
  const [driver, unauthorizedDriver, assistant, outsider, foreignAssistant] = await Promise.all([
    token(ids.driver, 'DRIVER'),
    token(ids.unauthorizedDriver, 'DRIVER'),
    token(ids.assistant, 'ASSISTANT'),
    token(ids.outsider, 'ASSISTANT'),
    token(ids.foreignAssistant, 'ASSISTANT', ids.foreignOperator),
  ]);
  const loadPath = (id) => `/v1/assistant/parcels/${id}/load`;
  const unassignedResult = await post(loadPath(ids.parcels[0]), outsider, uuidV4(), {
    tripId: ids.trip,
    parcelCode: parcelCodes[0],
  });
  expect(unassignedResult, 403, 'FORBIDDEN', 'unassigned assistant denied');
  assertNoResourceDisclosure(unassignedResult, 'unassigned assistant denial');
  const foreignResult = await post(loadPath(ids.parcels[0]), foreignAssistant, uuidV4(), {
    tripId: ids.trip,
    parcelCode: parcelCodes[0],
  });
  expect(foreignResult, 403, 'FORBIDDEN', 'foreign-tenant assistant denied');
  assertNoResourceDisclosure(foreignResult, 'foreign-tenant assistant denial');
  expect(
    await post(loadPath(ids.parcels[0]), assistant, 'not-a-uuid-v4', {
      tripId: ids.trip,
      parcelCode: parcelCodes[0],
    }),
    422,
    'VALIDATION_ERROR',
    'malformed idempotency key rejected',
  );
  expect(
    await post(loadPath(ids.parcels[0]), assistant, uuidV4(), {
      tripId: ids.trip,
      parcelCode: 'WRONG-CODE',
    }),
    404,
    'PARCEL_NOT_FOUND',
    'hidden parcel-code mismatch rejected',
  );
  const loadKeys = [];
  for (let index = 0; index < ids.parcels.length; index += 1) {
    const key = uuidV4();
    loadKeys.push(key);
    const result = await post(loadPath(ids.parcels[index]), assistant, key, {
      tripId: ids.trip,
      parcelCode: parcelCodes[index],
    });
    expect(result, 200, null, `assistant loads parcel ${index + 1}`);
    assertExactObjectKeys(
      result.body.data,
      ['parcelId', 'parcelCode', 'status'],
      'load response data',
    );
    assert(
      result.body?.data?.parcelId === ids.parcels[index] &&
        result.body?.data?.parcelCode === parcelCodes[index] &&
        result.body?.data?.status === 'LOADED',
      'load response data mismatch',
    );
    const replay = await post(loadPath(ids.parcels[index]), assistant, key, {
      tripId: ids.trip,
      parcelCode: parcelCodes[index],
    });
    expect(replay, 200, null, `same-key load replay ${index + 1}`);
    assert(
      JSON.stringify(replay.body) === JSON.stringify(result.body),
      'load replay changed response',
    );
  }
  expect(
    await post(loadPath(ids.parcels[0]), assistant, uuidV4(), {
      tripId: ids.trip,
      parcelCode: parcelCodes[0],
    }),
    409,
    'INVALID_STATUS',
    'new-key repeated load rejected',
  );
  await poll(
    'all three parcels LOADED and cargo counters updated',
    () =>
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.parcels WHERE id IN (${ids.parcels.map((id) => `'${id}'`).join(',')}) AND status='LOADED'`,
      ),
    (value) => value === '3',
  );
  const cargo = psql(
    'vietride_trip',
    `SELECT reserved_parcel_weight_kg || '|' || total_loaded_weight_kg FROM vietride_trip.trips WHERE id='${ids.trip}'`,
  );
  assert(cargo === '0.00|6.00', `cargo counters mismatch: ${cargo}`);
  await poll(
    'loaded and cargo threshold Outbox rows committed',
    () =>
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.outbox_events WHERE event_type='parcel.parcel.loaded' AND payload->>'tripId'='${ids.trip}'`,
      ) +
      '|' +
      psql(
        'vietride_trip',
        `SELECT count(*) FROM vietride_trip.outbox_events WHERE event_type='trip.cargo.threshold_crossed' AND payload->>'tripId'='${ids.trip}'`,
      ),
    (value) => value === '3|1',
  );
  const loadedOutbox = queryJson(
    'vietride_parcel',
    `SELECT coalesce(json_agg(json_build_object('id',id,'payload',payload) ORDER BY payload->>'parcelId'),'[]'::json)::text FROM vietride_parcel.outbox_events WHERE event_type='parcel.parcel.loaded' AND payload->>'tripId'='${ids.trip}'`,
    'loaded Outbox rows',
  );
  assert(
    Array.isArray(loadedOutbox) && loadedOutbox.length === 3,
    'expected three loaded Outbox rows',
  );
  assert(
    JSON.stringify(loadedOutbox.map((row) => row.payload.parcelId).sort()) ===
      JSON.stringify([...ids.parcels].sort()),
    'loaded Outbox parcelIds must match the three fixture parcels exactly once',
  );
  for (const row of loadedOutbox) {
    const payload = row.payload;
    assertExactObjectKeys(
      payload,
      ['eventId', 'occurredAt', 'parcelId', 'tripId', 'actualWeightKg', 'userIds'],
      'parcel loaded payload',
    );
    assert(row.id === payload.eventId, 'parcel loaded Outbox id/eventId mismatch');
    assertIsoTimestamp(payload.occurredAt, 'parcel loaded occurredAt');
    assert(ids.parcels.includes(payload.parcelId), 'parcel loaded parcelId mismatch');
    assert(payload.tripId === ids.trip, 'parcel loaded tripId mismatch');
    assert(payload.actualWeightKg === 1, 'parcel loaded actualWeightKg mismatch');
    assert(
      JSON.stringify([...payload.userIds].sort()) ===
        JSON.stringify([ids.sender, ids.recipient].sort()),
      'parcel loaded recipients mismatch',
    );
  }
  await poll(
    'loaded notifications correlate by Outbox identity and direct recipient',
    () =>
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications WHERE type='PARCEL_LOADED' AND data->>'tripId'='${ids.trip}' AND user_id IN ('${ids.sender}','${ids.recipient}') AND dedupe_key IN (${loadedOutbox.flatMap((row) => [ids.sender, ids.recipient].map((userId) => `'parcel.parcel.loaded:${row.id}:${userId}:PARCEL_LOADED'`)).join(',')})`,
      ),
    (value) => value === '6',
  );
  const cargoOutbox = queryJson(
    'vietride_trip',
    `SELECT json_build_object('id',id,'payload',payload)::text FROM vietride_trip.outbox_events WHERE event_type='trip.cargo.threshold_crossed' AND payload->>'tripId'='${ids.trip}'`,
    'cargo threshold Outbox row',
  );
  assertCargoEvent(cargoOutbox.payload, {
    tripId: ids.trip,
    operatorId: ids.operator,
    loadedWeightKg: 6,
    maxCargoWeightKg: 7,
    percentFull: 85.71,
  });
  assert(cargoOutbox.id === cargoOutbox.payload.eventId, 'cargo Outbox id/eventId mismatch');
  const cargoNotification = await poll(
    'one cargo notification for the active operator recipient with canonical dedupe',
    () =>
      psql(
        'vietride_notification',
        `SELECT coalesce(json_agg(json_build_object('id',id,'userId',user_id,'dedupeKey',dedupe_key,'data',data)),'[]'::json)::text FROM vietride_notification.notifications WHERE type='CARGO_NEAR_FULL_ALERT' AND data->>'eventId'='${cargoOutbox.id}'`,
      ),
    (value) => parseJson(value, 'cargo notifications').length === 1,
  );
  const cargoNotifications = parseJson(cargoNotification, 'cargo notifications');
  assert(
    cargoNotifications[0].userId === ids.operatorAdmin,
    'cargo notification recipient mismatch',
  );
  assert(
    cargoNotifications[0].dedupeKey ===
      `trip.cargo.threshold_crossed:${cargoOutbox.id}:${ids.operatorAdmin}:CARGO_NEAR_FULL_ALERT`,
    'cargo notification dedupe mismatch',
  );
  assertCargoEvent(cargoNotifications[0].data, {
    tripId: ids.trip,
    operatorId: ids.operator,
    loadedWeightKg: 6,
    maxCargoWeightKg: 7,
    percentFull: 85.71,
  });
  const rabbitMessages = await poll(
    'RabbitMQ cargo probe captures canonical MessageId',
    () =>
      parseJson(
        rabbitAdmin(
          'get',
          `queue=${cargoProbeQueue}`,
          'count=1',
          'ackmode=ack_requeue_false',
          'encoding=auto',
        ),
        'RabbitMQ cargo probe',
      ),
    (value) => Array.isArray(value) && value.length === 1,
  );
  const rabbitCargo = rabbitMessages[0];
  const rabbitMessageId = rabbitCargo.properties?.message_id ?? rabbitCargo.properties?.messageId;
  assert(rabbitCargo.exchange === 'vietride.events', 'RabbitMQ cargo exchange mismatch');
  assert(
    rabbitCargo.routing_key === 'trip.cargo.threshold_crossed',
    'RabbitMQ cargo routing key mismatch',
  );
  assert(rabbitMessageId === cargoOutbox.id, 'RabbitMQ MessageId/Outbox id mismatch');
  const rabbitPayload = parseJson(rabbitCargo.payload, 'RabbitMQ cargo payload');
  assert(
    JSON.stringify(rabbitPayload) === JSON.stringify(cargoOutbox.payload),
    'RabbitMQ cargo payload mismatch',
  );
  expect(
    await post(`/v1/driver/trips/${ids.trip}/start`, assistant, uuidV4()),
    403,
    'FORBIDDEN',
    'assistant cannot start trip',
  );
  expect(
    await post(`/v1/driver/trips/${ids.trip}/start`, unauthorizedDriver, uuidV4()),
    403,
    'FORBIDDEN',
    'unassigned driver cannot start trip',
  );
  const started = await post(`/v1/driver/trips/${ids.trip}/start`, driver, uuidV4());
  expect(started, 200, null, 'assigned driver starts BOARDING trip');
  assertExactObjectKeys(
    started.body.data,
    ['tripId', 'status', 'actualDepartureTime'],
    'start response data',
  );
  assert(started.body.data.tripId === ids.trip, 'start tripId mismatch');
  assert(started.body.data.status === 'IN_PROGRESS', 'start status mismatch');
  assertIsoTimestamp(started.body.data.actualDepartureTime, 'start actualDepartureTime');
  const startOutbox = queryJson(
    'vietride_trip',
    `SELECT json_build_object('id',id,'payload',payload)::text FROM vietride_trip.outbox_events WHERE event_type='trip.trip.started' AND payload->>'tripId'='${ids.trip}'`,
    'Trip started Outbox row',
  );
  assertExactObjectKeys(
    startOutbox.payload,
    ['tripId', 'actualDepartureTime'],
    'Trip started payload',
  );
  assert(startOutbox.payload.tripId === ids.trip, 'Trip started Outbox tripId mismatch');
  assertIsoTimestamp(startOutbox.payload.actualDepartureTime, 'Trip started occurred time');
  assert(
    Date.parse(startOutbox.payload.actualDepartureTime) ===
      Date.parse(started.body.data.actualDepartureTime),
    'Trip started Outbox/API timestamp correlation mismatch',
  );
  await poll(
    'TripStarted consumer transitions all parcels IN_TRANSIT',
    () =>
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.parcels WHERE id IN (${ids.parcels.map((id) => `'${id}'`).join(',')}) AND status='IN_TRANSIT'`,
      ),
    (value) => value === '3',
  );
  expect(
    await post(`/v1/assistant/parcels/${ids.parcels[0]}/unload`, assistant, uuidV4()),
    422,
    'DROP_OFF_STOP_NOT_ARRIVED',
    'unload before selected stop arrival rejected',
  );
  const wrongStopArrived = await post(
    `/v1/driver/trips/${ids.trip}/stops/${ids.secondStop}/arrive`,
    driver,
    uuidV4(),
  );
  expect(wrongStopArrived, 200, null, 'driver arrives at non-drop-off stop');
  assertExactObjectKeys(
    wrongStopArrived.body.data,
    ['tripId', 'stopId', 'status', 'actualArrivalTime'],
    'wrong-stop arrival response data',
  );
  assert(wrongStopArrived.body.data.tripId === ids.trip, 'wrong-stop arrival tripId mismatch');
  assert(
    wrongStopArrived.body.data.stopId === ids.secondStop,
    'wrong-stop arrival stopId mismatch',
  );
  assert(wrongStopArrived.body.data.status === 'ARRIVED', 'wrong-stop arrival status mismatch');
  assertIsoTimestamp(
    wrongStopArrived.body.data.actualArrivalTime,
    'wrong-stop arrival actualArrivalTime',
  );
  expect(
    await post(`/v1/assistant/parcels/${ids.parcels[0]}/unload`, assistant, uuidV4()),
    422,
    'DROP_OFF_STOP_NOT_ARRIVED',
    'wrong-stop unload rejected',
  );
  const arrived = await post(
    `/v1/driver/trips/${ids.trip}/stops/${ids.stop}/arrive`,
    driver,
    uuidV4(),
  );
  expect(arrived, 200, null, 'driver arrives at selected drop-off stop');
  assertExactObjectKeys(
    arrived.body.data,
    ['tripId', 'stopId', 'status', 'actualArrivalTime'],
    'selected-stop arrival response data',
  );
  assert(arrived.body.data.tripId === ids.trip, 'selected-stop arrival tripId mismatch');
  assert(arrived.body.data.stopId === ids.stop, 'selected-stop arrival stopId mismatch');
  assert(arrived.body.data.status === 'ARRIVED', 'selected-stop arrival status mismatch');
  assertIsoTimestamp(
    arrived.body.data.actualArrivalTime,
    'selected-stop arrival actualArrivalTime',
  );
  const unloadKey = uuidV4();
  const unloaded = await post(
    `/v1/assistant/parcels/${ids.parcels[0]}/unload`,
    assistant,
    unloadKey,
  );
  expect(unloaded, 200, null, 'assigned assistant unloads exactly one parcel');
  assertExactObjectKeys(
    unloaded.body.data,
    ['parcelId', 'parcelCode', 'status'],
    'unload response data',
  );
  assert(unloaded.body.data.parcelId === ids.parcels[0], 'unload parcelId mismatch');
  assert(unloaded.body.data.parcelCode === parcelCodes[0], 'unload parcelCode mismatch');
  assert(unloaded.body.data.status === 'UNLOADED', 'unload status mismatch');
  const unloadReplay = await post(
    `/v1/assistant/parcels/${ids.parcels[0]}/unload`,
    assistant,
    unloadKey,
  );
  expect(unloadReplay, 200, null, 'same-key unload replay');
  assert(
    JSON.stringify(unloadReplay.body) === JSON.stringify(unloaded.body),
    'unload replay changed response',
  );
  const states = psql(
    'vietride_parcel',
    `SELECT string_agg(status::text,',' ORDER BY parcel_code) FROM vietride_parcel.parcels WHERE id IN (${ids.parcels.map((id) => `'${id}'`).join(',')})`,
  );
  assert(
    states === 'UNLOADED,IN_TRANSIT,IN_TRANSIT',
    `selected-stop unload state mismatch: ${states}`,
  );
  await poll(
    'one unload Outbox and two direct-recipient notifications',
    () =>
      psql(
        'vietride_parcel',
        `SELECT count(*) FROM vietride_parcel.outbox_events WHERE event_type='parcel.parcel.unloaded' AND payload->>'parcelId'='${ids.parcels[0]}'`,
      ) +
      '|' +
      psql(
        'vietride_notification',
        `SELECT count(*) FROM vietride_notification.notifications WHERE type='PARCEL_IN_TRANSIT' AND data->>'parcelId'='${ids.parcels[0]}' AND dedupe_key LIKE 'parcel.parcel.unloaded:%'`,
      ),
    (value) => value === '1|2',
  );
  const unloadOutbox = queryJson(
    'vietride_parcel',
    `SELECT json_build_object('id',id,'payload',payload)::text FROM vietride_parcel.outbox_events WHERE event_type='parcel.parcel.unloaded' AND payload->>'parcelId'='${ids.parcels[0]}'`,
    'parcel unloaded Outbox row',
  );
  assertExactObjectKeys(
    unloadOutbox.payload,
    ['parcelId', 'tripId', 'userIds'],
    'parcel unloaded payload',
  );
  assert(unloadOutbox.payload.parcelId === ids.parcels[0], 'unload Outbox parcelId mismatch');
  assert(unloadOutbox.payload.tripId === ids.trip, 'unload Outbox tripId mismatch');
  assert(
    JSON.stringify([...unloadOutbox.payload.userIds].sort()) ===
      JSON.stringify([ids.sender, ids.recipient].sort()),
    'unload Outbox recipients mismatch',
  );
  const unloadNotificationDedupe = psql(
    'vietride_notification',
    `SELECT string_agg(dedupe_key,',' ORDER BY user_id) FROM vietride_notification.notifications WHERE type='PARCEL_IN_TRANSIT' AND data->>'parcelId'='${ids.parcels[0]}' AND dedupe_key LIKE 'parcel.parcel.unloaded:%'`,
  )
    .split(',')
    .filter(Boolean);
  assert(unloadNotificationDedupe.length === 2, 'unload notification count mismatch');
  for (const userId of [ids.sender, ids.recipient])
    assert(
      unloadNotificationDedupe.includes(
        `parcel.parcel.unloaded:${unloadOutbox.id}:${userId}:PARCEL_IN_TRANSIT`,
      ),
      `unload notification dedupe mismatch for ${userId}`,
    );
  const completed = await post(`/v1/driver/trips/${ids.trip}/complete`, assistant, uuidV4());
  expect(completed, 200, null, 'assigned assistant completes Trip');
  assertExactObjectKeys(
    completed.body.data,
    ['tripId', 'status', 'completedAt', 'completedByUserId'],
    'completion response data',
  );
  assert(completed.body.data.tripId === ids.trip, 'completion tripId mismatch');
  assert(completed.body.data.status === 'COMPLETED', 'completion status mismatch');
  assertIsoTimestamp(completed.body.data.completedAt, 'completion completedAt');
  assert(
    completed.body.data.completedByUserId === ids.assistant,
    'completion completedByUserId mismatch',
  );
  await poll(
    'Trip completion Outbox published',
    () =>
      psql(
        'vietride_trip',
        `SELECT count(*) FROM vietride_trip.outbox_events WHERE event_type='trip.trip.completed' AND payload->>'tripId'='${ids.trip}'`,
      ),
    (value) => value === '1',
  );
  const evidence = `# Day-29 Sprint-4 E2E evidence\n\n- Fixture trip: \`${ids.trip}\` (isolated generated IDs only).\n- Loaded Outbox ids: ${loadedOutbox.map((row) => `\`${row.id}\``).join(', ')}.\n- Trip-start Outbox id: \`${startOutbox.id}\`; API/Outbox actual-departure timestamps matched.\n- Cargo Outbox/event/RabbitMQ MessageId: \`${cargoOutbox.id}\`; exact seven-field payload matched at Outbox, broker probe, and Notification.\n- Cargo Notification id: \`${cargoNotifications[0].id}\`; recipient \`${ids.operatorAdmin}\`; canonical dedupe matched.\n- Unload Outbox id: \`${unloadOutbox.id}\`; both direct-recipient Notification dedupe keys matched.\n- Lifecycle: 3 parcels loaded, Trip started, wrong-stop unload rejected, selected stop arrived, exactly 1 parcel unloaded, Trip completed by \`${ids.assistant}\`.\n- Authorization: unassigned assistant, foreign-tenant assistant, and unassigned driver were denied without resource data disclosure.\n- Credentials/tokens are never written to this file or stdout.\n\nAssertions passed: ${assertionCount}\n`;
  fs.writeFileSync(path.join(root, 'docs/handoff/day-29-sprint4-evidence.md'), evidence, 'utf8');
}
export async function main() {
  try {
    await runJourney();
  } catch (error) {
    runError = error;
  } finally {
    try {
      cleanup();
      assertClean();
      console.log('PASS | Day-29 generated fixture cleanup verified');
    } catch (error) {
      cleanupError = error;
      console.error(`FAIL | Day-29 cleanup | ${error.message}`);
    }
  }
  if (runError) throw runError;
  if (cleanupError) throw cleanupError;
}
if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))
)
  await main();
