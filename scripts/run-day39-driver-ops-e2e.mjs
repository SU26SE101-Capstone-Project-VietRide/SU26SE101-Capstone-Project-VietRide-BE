import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import amqp from 'amqplib';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const useDev = process.env.DAY39_E2E_USE_DEV_STACK === '1';
const invocationId = `${process.pid}-${randomUUID().slice(0, 8)}`;
const composeProject = `day39-e2e-${invocationId}`;
const containerPrefix = composeProject;
const firebaseBucket = useDev
  ? process.env.FIREBASE_WEB_STORAGE_BUCKET
  : 'day39-e2e.firebasestorage.app';
const gateway =
  process.env.DAY39_GATEWAY_BASE_URL ||
  (useDev ? 'http://localhost:3000' : 'http://localhost:58300');
const serviceUrls = {
  identity:
    process.env.DAY39_IDENTITY_BASE_URL ||
    (useDev ? 'http://localhost:5001' : 'http://localhost:58001'),
  trip:
    process.env.DAY39_TRIP_BASE_URL ||
    (useDev ? 'http://localhost:5002' : 'http://localhost:58002'),
  parcel:
    process.env.DAY39_PARCEL_BASE_URL ||
    (useDev ? 'http://localhost:5005' : 'http://localhost:58005'),
  notification:
    process.env.DAY39_NOTIFICATION_BASE_URL ||
    (useDev ? 'http://localhost:3002' : 'http://localhost:58012'),
};
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day39-e2e.yml',
  '-p',
  composeProject,
];
const e2eEnv = useDev
  ? {}
  : {
      POSTGRES_USER: 'day39_e2e',
      POSTGRES_PASSWORD: 'day39_e2e_postgres_only',
      POSTGRES_PORT: '55439',
      REDIS_PORT: '56381',
      RABBITMQ_USER: 'day39_e2e',
      RABBITMQ_PASSWORD: 'day39_e2e_rabbit_only',
      RABBITMQ_PORT: '55692',
      RABBITMQ_MGMT_PORT: '55693',
      IDENTITY_PORT: '58001',
      TRIP_PORT: '58002',
      PARCEL_PORT: '58005',
      NOTIFICATION_PORT: '58012',
      GATEWAY_PORT: '58300',
      DAY39_COMPOSE_PROJECT: composeProject,
      DAY39_CONTAINER_PREFIX: containerPrefix,
      INTERNAL_JWT_SECRET: 'day39-e2e-internal-jwt-secret-32-bytes-minimum',
      GOOGLE_OAUTH_CLIENT_ID: '',
      GOOGLE_OAUTH_CLIENT_SECRET: '',
      SYSTEM_ADMIN_BOOTSTRAP_EMAIL: 'system@day39.test',
      SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: 'Day39-E2E-Only-Password-123!',
    };
const postgresUser = useDev ? process.env.POSTGRES_USER || 'vietride' : e2eEnv.POSTGRES_USER;
const postgresPassword = useDev
  ? process.env.POSTGRES_PASSWORD || 'vietride_dev'
  : e2eEnv.POSTGRES_PASSWORD;
const rabbitUser = useDev ? process.env.RABBITMQ_USER || 'vietride' : e2eEnv.RABBITMQ_USER;
const rabbitPassword = useDev
  ? process.env.RABBITMQ_PASSWORD || 'vietride_dev'
  : e2eEnv.RABBITMQ_PASSWORD;
const containers = {
  postgres: useDev ? 'vietride_postgres' : `${containerPrefix}-postgres`,
  redis: useDev ? 'vietride_redis' : `${containerPrefix}-redis`,
  rabbitmq: useDev ? 'vietride_rabbitmq' : `${containerPrefix}-rabbitmq`,
  identity: useDev ? 'vietride_identity' : `${containerPrefix}-identity`,
  trip: useDev ? 'vietride_trip' : `${containerPrefix}-trip`,
  parcel: useDev ? 'vietride_parcel' : `${containerPrefix}-parcel`,
  notification: useDev ? 'vietride_notification' : `${containerPrefix}-notification`,
  gateway: useDev ? 'vietride_gateway' : `${containerPrefix}-gateway`,
};
const id = (suffix) => `39000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
const ids = {
  operatorA: id(1),
  operatorB: id(2),
  adminA: id(11),
  staffA: id(12),
  inactiveAdminA: id(13),
  adminB: id(14),
  driver: id(21),
  assistant: id(22),
  unassignedDriver: id(23),
  unassignedAssistant: id(24),
  crossDriver: id(25),
  crossAssistant: id(26),
  passenger: id(27),
  origin: id(101),
  destination: id(102),
  stopStation: id(103),
  route: id(111),
  vehicleType: id(112),
  vehicle: id(113),
  stop: id(121),
  routeStop: id(122),
  incidentTrip: id(201),
  boardingTrip: id(202),
  scheduledTrip: id(203),
  terminalTrip: id(204),
  stopTrip: id(205),
  stopRaceTrip: id(206),
  destinationTrip: id(207),
  destinationRaceTrip: id(208),
  expressTrip: id(209),
  parcelStopTrip: id(210),
  parcelExpressTrip: id(211),
  autoCompletedTrip: id(212),
  assistantStopTrip: id(213),
  stopTripStop: id(301),
  stopRaceTripStop: id(302),
  boardingTripStop: id(303),
  terminalTripStop: id(304),
  parcelTripStop: id(305),
  skippedTripStop: id(306),
  assistantTripStop: id(307),
  stopParcel: id(401),
  terminalParcel: id(402),
  deliverParcel: id(403),
  autoCompletedParcel: id(404),
  stopCargo: id(501),
  terminalCargo: id(502),
};
const results = [];
const state = { tokens: {} };
let stackOwned = false;

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    maxBuffer: 32 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  }
  return result.stdout.trim();
}

function composeRun(args) {
  return run('docker', [...compose, ...args], { env: e2eEnv });
}

function sql(database, schema, statement) {
  return run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    postgresUser,
    '-d',
    database,
    '-qAtc',
    `SET search_path TO ${schema},public; ${statement}`,
  ]);
}

const identitySql = (statement) => sql('vietride_identity', 'vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', 'vietride_trip', statement);
const parcelSql = (statement) => sql('vietride_parcel', 'vietride_parcel', statement);
const notificationSql = (statement) =>
  sql('vietride_notification', 'vietride_notification', statement);
const redis = (...args) => run('docker', ['exec', containers.redis, 'redis-cli', ...args]);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function scalar(value) {
  return String(value).split(/\r?\n/).filter(Boolean).at(-1)?.trim() ?? '';
}

function count(value) {
  return Number(scalar(value));
}

function sqlLiteral(value) {
  if (value === null || value === undefined) return 'NULL';
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'number') return String(value);
  return `'${String(value).replaceAll("'", "''")}'`;
}

function existingColumns(database, schema, table) {
  return new Set(
    sql(
      database,
      schema,
      `SELECT column_name FROM information_schema.columns WHERE table_schema='${schema}' AND table_name='${table}' ORDER BY ordinal_position`,
    )
      .split(/\r?\n/)
      .filter(Boolean),
  );
}

function insertCompatible(database, schema, table, row) {
  const columns = existingColumns(database, schema, table);
  const entries = Object.entries(row).filter(([column]) => columns.has(column));
  assert(entries.length > 0, `No compatible columns for ${schema}.${table}`);
  sql(
    database,
    schema,
    `INSERT INTO ${table} (${entries.map(([column]) => column).join(',')}) VALUES (${entries.map(([, value]) => sqlLiteral(value)).join(',')}) ON CONFLICT DO NOTHING;`,
  );
}

function updateCompatible(database, schema, table, keyColumn, keyValue, row) {
  const columns = existingColumns(database, schema, table);
  const entries = Object.entries(row).filter(
    ([column]) => column !== keyColumn && columns.has(column),
  );
  if (entries.length === 0) return;
  sql(
    database,
    schema,
    `UPDATE ${table} SET ${entries.map(([column, value]) => `${column}=${sqlLiteral(value)}`).join(',')} WHERE ${keyColumn}=${sqlLiteral(keyValue)};`,
  );
}

async function poll(fn, message, timeoutMs = 90_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await fn();
      if (last) return last;
    } catch (error) {
      last = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${message}; last=${last instanceof Error ? last.message : String(last)}`);
}

async function waitFor(url, timeoutMs = 300_000) {
  await poll(
    async () => {
      try {
        return (await fetch(url)).ok;
      } catch {
        return false;
      }
    },
    `Timed out waiting for ${url}`,
    timeoutMs,
  );
}

async function scenario(number, name, fn) {
  const startedAt = Date.now();
  await fn();
  const result = {
    scenario: `E2E-${String(number).padStart(2, '0')}`,
    name,
    passed: true,
    durationMs: Date.now() - startedAt,
  };
  results.push(result);
  console.log(`PASS | ${result.scenario} | ${name} | ${result.durationMs}ms`);
}

async function userJwt(userId, role, operatorId) {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const key = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  return new SignJWT({
    role,
    ...(operatorId ? { operatorId, operator_id: operatorId } : {}),
    email: `${role.toLowerCase()}-${userId.slice(-4)}@day39.test`,
    hasPhone: true,
  })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(userId)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
}

async function api(method, pathname, { token, body, key } = {}) {
  const response = await fetch(`${gateway}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const json = await response.json().catch(() => null);
  return { status: response.status, json, headers: response.headers };
}

function errorCode(response) {
  return response.json?.errorCode ?? response.json?.error?.code ?? response.json?.code;
}

function expectError(response, status, code) {
  assert(
    response.status === status,
    `Expected HTTP ${status}, got ${response.status}: ${JSON.stringify(response.json)}`,
  );
  assert(
    errorCode(response) === code,
    `Expected ${code}, got ${errorCode(response)}: ${JSON.stringify(response.json)}`,
  );
}

function idemKey(label) {
  const hash = createHash('sha256').update(`day39:${label}`).digest('hex');
  return `${hash.slice(0, 8)}-${hash.slice(8, 12)}-4${hash.slice(13, 16)}-8${hash.slice(17, 20)}-${hash.slice(20, 32)}`;
}

function idemHash(key) {
  return createHash('sha256').update(key).digest('hex').toUpperCase();
}

function incidentPhotoUrl(reporterUserId, fileName) {
  assert(firebaseBucket, 'FIREBASE_WEB_STORAGE_BUCKET is required with DAY39_E2E_USE_DEV_STACK=1');
  const objectPath = encodeURIComponent(`incidents/${ids.operatorA}/${reporterUserId}/${fileName}`);
  return `https://firebasestorage.googleapis.com/v0/b/${firebaseBucket}/o/${objectPath}?alt=media`;
}

async function publish(routingKey, payload, transportId = randomUUID()) {
  return publishRaw(routingKey, JSON.stringify(payload), transportId);
}

async function publishRaw(routingKey, payloadJson, transportId = randomUUID()) {
  const connection = await amqp.connect({
    hostname: '127.0.0.1',
    port: useDev ? Number(process.env.RABBITMQ_PORT || 5672) : 55692,
    username: rabbitUser,
    password: rabbitPassword,
  });
  try {
    const channel = await connection.createConfirmChannel();
    await channel.assertExchange('vietride.events', 'topic', { durable: true });
    channel.publish('vietride.events', routingKey, Buffer.from(payloadJson), {
      persistent: true,
      contentType: 'application/json',
      messageId: transportId,
    });
    await channel.waitForConfirms();
    await channel.close();
  } finally {
    await connection.close();
  }
}

function seedPrerequisites() {
  const now = Date.now();
  const before = (minutes) => new Date(now - minutes * 60_000).toISOString();
  const after = (minutes) => new Date(now + minutes * 60_000).toISOString();

  identitySql(`
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
    VALUES
      ('${ids.operatorA}','Day 39 Operator A','D39-A-BRN','D39-A-TAX','operator-a@day39.test','+84910039001','APPROVED',now(),true),
      ('${ids.operatorB}','Day 39 Operator B','D39-B-BRN','D39-B-TAX','operator-b@day39.test','+84910039002','APPROVED',now(),true)
    ON CONFLICT (id) DO UPDATE SET registration_status='APPROVED',is_active=true,deleted_at=NULL;
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.adminA}','admin-a@day39.test','+84910039101','Day 39 Admin A','OPERATOR_ADMIN','ACTIVE','${ids.operatorA}'),
      ('${ids.staffA}','staff-a@day39.test','+84910039102','Day 39 Staff A','OPERATOR_STAFF','ACTIVE','${ids.operatorA}'),
      ('${ids.inactiveAdminA}','inactive-admin-a@day39.test','+84910039103','Day 39 Inactive Admin A','OPERATOR_ADMIN','LOCKED','${ids.operatorA}'),
      ('${ids.adminB}','admin-b@day39.test','+84910039104','Day 39 Admin B','OPERATOR_ADMIN','ACTIVE','${ids.operatorB}'),
      ('${ids.driver}','driver@day39.test','+84910039111','Day 39 Driver','DRIVER','ACTIVE','${ids.operatorA}'),
      ('${ids.assistant}','assistant@day39.test','+84910039112','Day 39 Assistant','ASSISTANT','ACTIVE','${ids.operatorA}'),
      ('${ids.unassignedDriver}','unassigned-driver@day39.test','+84910039113','Day 39 Unassigned Driver','DRIVER','ACTIVE','${ids.operatorA}'),
      ('${ids.unassignedAssistant}','unassigned-assistant@day39.test','+84910039114','Day 39 Unassigned Assistant','ASSISTANT','ACTIVE','${ids.operatorA}'),
      ('${ids.crossDriver}','cross-driver@day39.test','+84910039115','Day 39 Cross Driver','DRIVER','ACTIVE','${ids.operatorB}'),
      ('${ids.crossAssistant}','cross-assistant@day39.test','+84910039116','Day 39 Cross Assistant','ASSISTANT','ACTIVE','${ids.operatorB}'),
      ('${ids.passenger}','passenger@day39.test','+84910039117','Day 39 Passenger','PASSENGER','ACTIVE',NULL)
    ON CONFLICT (id) DO UPDATE SET role=EXCLUDED.role,status=EXCLUDED.status,operator_id=EXCLUDED.operator_id,deleted_at=NULL;
    INSERT INTO user_devices (id,user_id,fcm_token,platform,is_active)
    VALUES
      ('${id(31)}','${ids.adminA}','day39-admin-a-token','ANDROID',true),
      ('${id(32)}','${ids.staffA}','day39-staff-a-token','ANDROID',true),
      ('${id(33)}','${ids.inactiveAdminA}','day39-inactive-admin-a-token','ANDROID',true),
      ('${id(34)}','${ids.adminB}','day39-admin-b-token','ANDROID',true)
    ON CONFLICT (user_id,fcm_token) DO UPDATE SET is_active=true;
  `);

  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.origin,
    name: 'Day 39 Origin',
    slug: 'day39-origin',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.7701,
    longitude: 106.7001,
    supports_shuttle: false,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.destination,
    name: 'Day 39 Destination',
    slug: 'day39-destination',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.8001,
    longitude: 106.7501,
    supports_shuttle: false,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.stopStation,
    name: 'Day 39 Stop Station',
    slug: 'day39-stop-station',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.7851,
    longitude: 106.7251,
    supports_shuttle: false,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'stops', {
    id: ids.stop,
    operator_id: ids.operatorA,
    station_id: ids.stopStation,
    name: 'Day 39 Drop-off Stop',
    address: '39 Test Street',
    latitude: 10.7851,
    longitude: 106.7251,
    is_active: true,
  });
  for (const [index, stopId] of [
    ids.stopTripStop,
    ids.stopRaceTripStop,
    ids.boardingTripStop,
    ids.terminalTripStop,
    ids.parcelTripStop,
    ids.skippedTripStop,
    ids.assistantTripStop,
  ].entries()) {
    insertCompatible('vietride_trip', 'vietride_trip', 'stops', {
      id: stopId,
      operator_id: ids.operatorA,
      name: `Day 39 Scenario Stop ${index + 1}`,
      address: `${index + 1} Day 39 Test Street`,
      latitude: 10.7851 + index * 0.0001,
      longitude: 106.7251 + index * 0.0001,
      is_active: true,
    });
  }
  insertCompatible('vietride_trip', 'vietride_trip', 'routes', {
    id: ids.route,
    operator_id: ids.operatorA,
    name: 'Day 39 Route',
    origin_station_id: ids.origin,
    destination_station_id: ids.destination,
    base_fare: 100000,
    estimated_duration_minutes: 120,
    distance_km: 50,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'route_stops', {
    id: ids.routeStop,
    route_id: ids.route,
    stop_id: ids.stop,
    station_id: ids.stopStation,
    stop_order: 1,
    sequence: 1,
    order_index: 1,
    estimated_duration_from_origin_minutes: 60,
    distance_from_origin_km: 25,
    allow_pickup: true,
    allow_dropoff: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicle_types', {
    id: ids.vehicleType,
    code: 'DAY39_BUS',
    display_name: 'Day 39 Bus',
    default_seat_count: 20,
    max_cargo_weight_kg: 1000,
    max_cargo_volume_m3: 20,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicles', {
    id: ids.vehicle,
    operator_id: ids.operatorA,
    vehicle_type_id: ids.vehicleType,
    license_plate: '51B-390.39',
    seat_layout_json: '{}',
    total_seats: 20,
    status: 'ACTIVE',
    is_active: true,
  });

  const trips = [
    [ids.incidentTrip, 'IN_PROGRESS', before(60), after(60)],
    [ids.boardingTrip, 'BOARDING', after(10), after(130)],
    [ids.scheduledTrip, 'SCHEDULED', after(60), after(180)],
    [ids.terminalTrip, 'COMPLETED', before(180), before(60)],
    [ids.stopTrip, 'IN_PROGRESS', before(50), after(70)],
    [ids.stopRaceTrip, 'IN_PROGRESS', before(45), after(75)],
    [ids.destinationTrip, 'IN_PROGRESS', before(40), after(80)],
    [ids.destinationRaceTrip, 'IN_PROGRESS', before(35), after(85)],
    [ids.expressTrip, 'IN_PROGRESS', before(30), after(90)],
    [ids.parcelStopTrip, 'IN_PROGRESS', before(25), after(95)],
    [ids.parcelExpressTrip, 'IN_PROGRESS', before(20), after(100)],
    [ids.autoCompletedTrip, 'COMPLETED', before(240), before(120)],
    [ids.assistantStopTrip, 'IN_PROGRESS', before(15), after(105)],
  ];
  for (const [tripId, status, departure, arrival] of trips) {
    insertCompatible('vietride_trip', 'vietride_trip', 'trips', {
      id: tripId,
      operator_id: ids.operatorA,
      route_id: ids.route,
      vehicle_id: ids.vehicle,
      driver_user_id: ids.driver,
      assistant_user_id: ids.assistant,
      departure_date_time: departure,
      estimated_arrival_time: arrival,
      status,
      source: 'MANUAL',
      base_fare: 100000,
      completed_at: status === 'COMPLETED' ? arrival : null,
      completed_by_user_id: status === 'COMPLETED' ? ids.driver : null,
      destination_arrived_at: null,
      destination_arrived_by_user_id: null,
      total_reserved_weight_kg: 0,
      total_loaded_weight_kg: 0,
      total_reserved_volume_m3: 0,
      total_loaded_volume_m3: 0,
    });
  }

  const tripStops = [
    [ids.stopTripStop, ids.stopTrip, 'PENDING', 1],
    [ids.stopRaceTripStop, ids.stopRaceTrip, 'PENDING', 1],
    [ids.boardingTripStop, ids.boardingTrip, 'PENDING', 1],
    [ids.terminalTripStop, ids.terminalTrip, 'PENDING', 1],
    [ids.parcelTripStop, ids.parcelStopTrip, 'PENDING', 1],
    [ids.skippedTripStop, ids.stopTrip, 'SKIPPED', 2],
    [ids.assistantTripStop, ids.assistantStopTrip, 'PENDING', 1],
  ];
  for (const [tripStopId, tripId, status, orderIndex] of tripStops) {
    insertCompatible('vietride_trip', 'vietride_trip', 'trip_stops', {
      trip_id: tripId,
      stop_id: tripStopId,
      route_stop_id: ids.routeStop,
      station_id: ids.stopStation,
      stop_order: orderIndex,
      sequence: orderIndex,
      order_index: orderIndex,
      status,
      allow_pickup: true,
      allow_dropoff: true,
      scheduled_arrival_time: after(20),
      scheduled_departure_time: after(25),
      estimated_arrival_time: after(20),
      estimated_departure_time: after(25),
      actual_arrival_time: null,
      actual_departure_time: null,
      distance_from_origin_km: 20 + orderIndex,
    });
  }

  const parcelBase = {
    sender_user_id: ids.passenger,
    recipient_user_id: ids.adminA,
    recipient_name: 'Day 39 Recipient',
    recipient_phone: '+84910039999',
    recipient_email: 'recipient@day39.test',
    operator_id: ids.operatorA,
    size_category: 'MEDIUM',
    estimated_size_category: 'MEDIUM',
    estimated_length_cm: 100,
    estimated_width_cm: 50,
    estimated_height_cm: 10,
    estimated_weight_kg: 10,
    estimated_volume_m3: 0.05,
    estimated_dim_weight_kg: 8.33,
    estimated_chargeable_weight_kg: 10,
    delivery_method: 'TERMINAL_PICKUP',
    total_price_vnd: 100000,
    deposit_percent: 100,
    deposit_amount: 100000,
    original_deposit_amount: 100000,
    estimated_gross_price_vnd: 100000,
    final_gross_price_vnd: 100000,
    estimated_total_price_vnd: 100000,
    final_total_price_vnd: 100000,
    deposit_required_vnd: 100000,
    deposit_paid_vnd: 100000,
    total_amount: 100000,
    delivered_pending_confirm_at: null,
    confirmed_at: null,
    unloaded_at: null,
  };
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.stopParcel,
    parcel_code: 'VRP-D39001',
    trip_id: ids.parcelStopTrip,
    dropoff_stop_id: ids.parcelTripStop,
    status: 'IN_TRANSIT',
    loaded_at: before(20),
    loaded_by_user_id: ids.assistant,
  });
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.terminalParcel,
    parcel_code: 'VRP-D39002',
    trip_id: ids.parcelExpressTrip,
    dropoff_stop_id: null,
    status: 'IN_TRANSIT',
    loaded_at: before(20),
    loaded_by_user_id: ids.assistant,
  });
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.deliverParcel,
    parcel_code: 'VRP-D39003',
    trip_id: ids.parcelStopTrip,
    dropoff_stop_id: ids.parcelTripStop,
    status: 'UNLOADED',
    loaded_at: before(20),
    loaded_by_user_id: ids.assistant,
    unloaded_at: before(5),
  });
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.autoCompletedParcel,
    parcel_code: 'VRP-D39004',
    trip_id: ids.autoCompletedTrip,
    dropoff_stop_id: null,
    status: 'IN_TRANSIT',
    loaded_at: before(200),
    loaded_by_user_id: ids.assistant,
  });

  insertCompatible('vietride_trip', 'vietride_trip', 'trip_cargo_parcels', {
    id: ids.stopCargo,
    trip_id: ids.parcelStopTrip,
    parcel_id: ids.stopParcel,
    state: 'LOADED',
    status: 'LOADED',
    weight_kg: 10,
    volume_m3: 0.05,
    actual_weight_kg: 10,
    actual_volume_m3: 0.05,
    loaded_at: before(20),
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'trip_cargo_parcels', {
    id: ids.terminalCargo,
    trip_id: ids.parcelExpressTrip,
    parcel_id: ids.terminalParcel,
    state: 'LOADED',
    status: 'LOADED',
    weight_kg: 10,
    volume_m3: 0.05,
    actual_weight_kg: 10,
    actual_volume_m3: 0.05,
    loaded_at: before(20),
  });
  for (const tripId of [ids.parcelStopTrip, ids.parcelExpressTrip]) {
    updateCompatible('vietride_trip', 'vietride_trip', 'trips', 'id', tripId, {
      total_loaded_weight_kg: 10,
      total_loaded_volume_m3: 0.05,
    });
  }
}

async function runMigrationGate() {
  if (useDev) return;
  const user = postgresUser;
  const password = postgresPassword;
  const scratch = 'day39_migration_scratch';
  run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    user,
    '-d',
    'postgres',
    '-c',
    `DROP DATABASE IF EXISTS ${scratch};`,
  ]);
  run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    user,
    '-d',
    'postgres',
    '-c',
    `CREATE DATABASE ${scratch};`,
  ]);
  const connection = `Host=127.0.0.1;Port=55439;Database=${scratch};Username=${user};Password=${password}`;
  const ef = (target) =>
    run(
      'dotnet',
      [
        'ef',
        'database',
        'update',
        ...(target ? [target] : []),
        '--project',
        'apps/trip/src/VietRide.Trip.Infrastructure',
        '--configuration',
        'Release',
        '--no-build',
      ],
      { env: { TRIP_DESIGN_CONNECTION: connection } },
    );
  ef();
  const migrations = sql(
    scratch,
    'vietride_trip',
    'SELECT "MigrationId" FROM "__ef_migrations_history" ORDER BY "MigrationId";',
  )
    .split(/\r?\n/)
    .filter(Boolean);
  const incidentIndex = migrations.findIndex((migration) =>
    migration.endsWith('_AddTripIncidents'),
  );
  const destinationIndex = migrations.findIndex((migration) =>
    migration.endsWith('_AddTripDestinationArrival'),
  );
  assert(
    incidentIndex > 0 && destinationIndex > incidentIndex,
    `Day 39 migrations missing or out of order: ${migrations.join(',')}`,
  );
  ef(migrations[destinationIndex - 1]);
  assert(
    sql(
      scratch,
      'vietride_trip',
      "SELECT count(*) FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='trips' AND column_name='destination_arrived_at';",
    ) === '0',
    'Destination migration rollback left destination columns behind',
  );
  assert(
    sql(scratch, 'vietride_trip', "SELECT to_regclass('vietride_trip.incidents') IS NOT NULL;") ===
      't',
    'Destination rollback removed Incident migration',
  );
  ef(migrations[incidentIndex - 1]);
  assert(
    sql(scratch, 'vietride_trip', "SELECT to_regclass('vietride_trip.incidents') IS NULL;") === 't',
    'Incident migration rollback left incidents table behind',
  );
  ef();
  const schemaCheck = sql(
    scratch,
    'vietride_trip',
    `
    SELECT
      (to_regclass('vietride_trip.incidents') IS NOT NULL)::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='trips' AND column_name='destination_arrived_at'))::int || ':' ||
      (EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='vietride_trip' AND tablename='incidents'))::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='incidents' AND column_name='photo_urls' AND data_type='jsonb'))::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='incidents' AND column_name='category' AND data_type='USER-DEFINED'))::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE table_schema='vietride_trip' AND table_name='incidents' AND constraint_type='FOREIGN KEY'))::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='incidents' AND column_name='latitude' AND numeric_scale >= 6))::int || ':' ||
      (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='trips' AND column_name='destination_arrived_by_user_id'))::int || ':' ||
      ((SELECT count(*) FROM "__ef_migrations_history" WHERE "MigrationId" LIKE '%AddTripIncidents' OR "MigrationId" LIKE '%AddTripDestinationArrival')=2)::int;
  `,
  );
  assert(
    scalar(schemaCheck) === '1:1:1:1:1:1:1:1:1',
    `Migration reapply schema assertion failed: ${schemaCheck}`,
  );
}

async function runAcceptance() {
  if (!useDev) {
    stackOwned = true;
    composeRun(['--profile', 'infra', 'up', '-d', '--wait', 'postgres', 'redis', 'rabbitmq']);
    composeRun([
      '--profile',
      'app',
      'up',
      '-d',
      '--build',
      '--no-deps',
      'identity',
      'trip',
      'parcel',
      'notification',
      'gateway',
    ]);
  }
  await Promise.all([
    waitFor(`${gateway}/health`),
    waitFor(`${gateway}/ready`),
    waitFor(`${serviceUrls.identity}/health`),
    waitFor(`${serviceUrls.trip}/health`),
    waitFor(`${serviceUrls.parcel}/health`),
    waitFor(`${serviceUrls.notification}/health`),
  ]);
  seedPrerequisites();
  state.tokens = {
    admin: await userJwt(ids.adminA, 'OPERATOR_ADMIN', ids.operatorA),
    driver: await userJwt(ids.driver, 'DRIVER', ids.operatorA),
    assistant: await userJwt(ids.assistant, 'ASSISTANT', ids.operatorA),
    unassignedDriver: await userJwt(ids.unassignedDriver, 'DRIVER', ids.operatorA),
    unassignedAssistant: await userJwt(ids.unassignedAssistant, 'ASSISTANT', ids.operatorA),
    crossDriver: await userJwt(ids.crossDriver, 'DRIVER', ids.operatorB),
    crossAssistant: await userJwt(ids.crossAssistant, 'ASSISTANT', ids.operatorB),
    passenger: await userJwt(ids.passenger, 'PASSENGER'),
  };
  console.log('seed PASS');

  const incidentBody = {
    category: 'VEHICLE_BREAKDOWN',
    description: '  Engine temperature exceeded the operating limit.  ',
    photoUrls: [incidentPhotoUrl(ids.driver, 'engine.jpg')],
    latitude: 10.7765,
    longitude: 106.7009,
  };

  await scenario(1, 'Driver and Assistant report normalized incidents atomically', async () => {
    const tripBefore = scalar(tripSql(`SELECT status FROM trips WHERE id='${ids.incidentTrip}'`));
    const driver = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
      token: state.tokens.driver,
      key: idemKey('incident-driver'),
      body: incidentBody,
    });
    const assistant = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
      token: state.tokens.assistant,
      key: idemKey('incident-assistant'),
      body: {
        ...incidentBody,
        category: 'OTHER',
        description: 'Passenger requested operational support.',
        photoUrls: [incidentPhotoUrl(ids.assistant, 'support.jpg')],
      },
    });
    assert(
      driver.status === 201 && assistant.status === 201,
      `Incident success failed: ${JSON.stringify({ driver, assistant })}`,
    );
    assert(
      driver.json?.data?.description === incidentBody.description.trim(),
      'Incident description was not normalized',
    );
    assert(driver.json?.data?.tripId === ids.incidentTrip, 'Incident response tripId drifted');
    assert(
      scalar(tripSql(`SELECT status FROM trips WHERE id='${ids.incidentTrip}'`)) === tripBefore,
      'Incident changed Trip.status',
    );
    assert(
      count(tripSql(`SELECT count(*) FROM incidents WHERE trip_id='${ids.incidentTrip}'`)) === 2,
      'Incident row count is not exact',
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM outbox_events WHERE event_type='trip.incident.reported' AND payload->>'tripId'='${ids.incidentTrip}'`,
        ),
      ) === 2,
      'Incident Outbox count is not exact',
    );
    state.incident = driver.json.data;
  });

  await scenario(
    2,
    'Incident validation and missing idempotency key have zero side effect',
    async () => {
      const before = count(
        tripSql(`SELECT count(*) FROM incidents WHERE trip_id='${ids.incidentTrip}'`),
      );
      const invalidBodies = [
        { ...incidentBody, category: 'NOT_A_CATEGORY' },
        { ...incidentBody, description: 'x'.repeat(501) },
        {
          ...incidentBody,
          photoUrls: [
            'https://a.test/1',
            'https://a.test/2',
            'https://a.test/3',
            'https://a.test/4',
          ],
        },
        { ...incidentBody, photoUrls: ['http://assets.day39.test/insecure.jpg'] },
        { ...incidentBody, photoUrls: ['/relative.jpg'] },
        { ...incidentBody, latitude: 10.7, longitude: null },
        { ...incidentBody, latitude: 91, longitude: 106.7 },
      ];
      for (const [index, body] of invalidBodies.entries()) {
        const response = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
          token: state.tokens.driver,
          key: idemKey(`invalid-${index}`),
          body,
        });
        assert(
          response.status === 400 || response.status === 422,
          `Validation case ${index} unexpectedly returned ${response.status}`,
        );
      }
      const missing = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.driver,
        body: incidentBody,
      });
      expectError(missing, 422, 'IDEMPOTENCY_KEY_REQUIRED');
      assert(
        count(tripSql(`SELECT count(*) FROM incidents WHERE trip_id='${ids.incidentTrip}'`)) ===
          before,
        'Invalid Incident request persisted a row',
      );
    },
  );

  await scenario(3, 'Incident assignment, tenant, role, missing and state guards', async () => {
    for (const token of [
      state.tokens.unassignedDriver,
      state.tokens.crossDriver,
      state.tokens.passenger,
    ]) {
      const response = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token,
        key: randomUUID(),
        body: incidentBody,
      });
      assert(
        response.status === 403,
        `Incident authorization expected 403, got ${response.status}`,
      );
    }
    expectError(
      await api('POST', `/v1/driver/trips/${id(999999)}/incident`, {
        token: state.tokens.driver,
        key: randomUUID(),
        body: incidentBody,
      }),
      404,
      'TRIP_NOT_FOUND',
    );
    for (const tripId of [ids.boardingTrip, ids.scheduledTrip, ids.terminalTrip]) {
      expectError(
        await api('POST', `/v1/driver/trips/${tripId}/incident`, {
          token: state.tokens.driver,
          key: randomUUID(),
          body: incidentBody,
        }),
        422,
        'TRIP_NOT_IN_PROGRESS',
      );
    }
  });

  await scenario(4, 'Idempotency replay, mismatch, actor isolation and concurrency', async () => {
    const key = idemKey('incident-replay');
    const first = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
      token: state.tokens.driver,
      key,
      body: incidentBody,
    });
    const replay = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
      token: state.tokens.driver,
      key,
      body: incidentBody,
    });
    assert(
      first.status === 201 &&
        replay.status === 201 &&
        JSON.stringify(first.json) === JSON.stringify(replay.json),
      'Sequential replay changed response bytes/body',
    );
    expectError(
      await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.driver,
        key,
        body: { ...incidentBody, description: 'Different' },
      }),
      422,
      'IDEMPOTENCY_KEY_MISMATCH',
    );
    expectError(
      await api('POST', `/v1/driver/trips/${ids.stopTrip}/incident`, {
        token: state.tokens.driver,
        key,
        body: incidentBody,
      }),
      422,
      'IDEMPOTENCY_KEY_MISMATCH',
    );
    expectError(
      await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.assistant,
        key,
        body: incidentBody,
      }),
      422,
      'IDEMPOTENCY_KEY_MISMATCH',
    );
    const originalAgain = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
      token: state.tokens.driver,
      key,
      body: incidentBody,
    });
    assert(
      originalAgain.status === 201 &&
        JSON.stringify(originalAgain.json) === JSON.stringify(first.json),
      'Mismatch overwrote the original cached response',
    );
    const raceKey = idemKey('incident-concurrent');
    const before = count(
      tripSql(`SELECT count(*) FROM incidents WHERE trip_id='${ids.incidentTrip}'`),
    );
    const race = await Promise.all([
      api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.driver,
        key: raceKey,
        body: { ...incidentBody, description: 'Concurrent incident' },
      }),
      api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.driver,
        key: raceKey,
        body: { ...incidentBody, description: 'Concurrent incident' },
      }),
    ]);
    assert(
      race.some((response) => response.status === 201) &&
        race.every(
          (response) =>
            response.status === 201 ||
            (response.status === 409 && errorCode(response) === 'IDEMPOTENCY_REQUEST_PENDING'),
        ),
      `Concurrent idempotency result invalid: ${JSON.stringify(race)}`,
    );
    assert(
      count(tripSql(`SELECT count(*) FROM incidents WHERE trip_id='${ids.incidentTrip}'`)) ===
        before + 1,
      'Concurrent same-key request executed more than once',
    );
    const ttl = Number(redis('TTL', `trip:idem:v2:response:${idemHash(raceKey)}`));
    assert(ttl > 85_000 && ttl <= 86_400, `Trip response TTL is not near 24h: ${ttl}`);
    assert(
      Number(redis('EXISTS', `trip:idem:v2:processing:${idemHash(raceKey)}`)) === 0,
      'Trip processing lock was not cleared',
    );
  });

  await scenario(
    5,
    'Outbox publishes once and only the active same-operator admin is notified',
    async () => {
      await poll(
        () =>
          count(
            tripSql(
              `SELECT count(*) FROM outbox_events WHERE event_type='trip.incident.reported' AND payload->>'incidentId'='${state.incident.incidentId}' AND status='PUBLISHED'`,
            ),
          ) === 1,
        'Incident Outbox was not published',
        120_000,
      );
      await poll(
        () =>
          count(
            notificationSql(
              `SELECT count(*) FROM notifications WHERE type='INCIDENT_REPORTED' AND data->>'incidentId'='${state.incident.incidentId}' AND user_id='${ids.adminA}'`,
            ),
          ) === 1,
        'Operator admin incident notification missing',
        120_000,
      );
      assert(
        count(
          notificationSql(
            `SELECT count(*) FROM notifications WHERE type='INCIDENT_REPORTED' AND data->>'incidentId'='${state.incident.incidentId}' AND user_id IN ('${ids.staffA}','${ids.inactiveAdminA}','${ids.adminB}')`,
          ),
        ) === 0,
        'Excluded incident recipient received a notification',
      );
      assert(
        count(
          notificationSql(
            `SELECT count(*) FROM notification_deliveries d JOIN notifications n ON n.id=d.notification_id WHERE n.type='INCIDENT_REPORTED' AND n.data->>'incidentId'='${state.incident.incidentId}' AND d.status='SENT'`,
          ),
        ) === 1,
        'Incident push delivery is not exactly one SENT row',
      );
      const list = await api('GET', '/v1/notifications?page=1&pageSize=50', {
        token: state.tokens.admin,
      });
      assert(
        list.status === 200 && JSON.stringify(list.json).includes(state.incident.incidentId),
        'Operator notification API cannot see incident alert',
      );
    },
  );

  await scenario(6, 'Consumer dedupe uses payload eventId, not transport message ID', async () => {
    const payloadJson = scalar(
      tripSql(
        `SELECT payload::text FROM outbox_events WHERE event_type='trip.incident.reported' AND payload->>'incidentId'='${state.incident.incidentId}' LIMIT 1`,
      ),
    );
    const before = count(
      notificationSql(
        `SELECT count(*) FROM notifications WHERE type='INCIDENT_REPORTED' AND data->>'incidentId'='${state.incident.incidentId}'`,
      ),
    );
    await publishRaw('trip.incident.reported', payloadJson, randomUUID());
    await new Promise((resolve) => setTimeout(resolve, 2_000));
    assert(
      count(
        notificationSql(
          `SELECT count(*) FROM notifications WHERE type='INCIDENT_REPORTED' AND data->>'incidentId'='${state.incident.incidentId}'`,
        ),
      ) === before,
      'Republish duplicated Incident notification',
    );
  });

  await scenario(
    7,
    'Identity outage retries recipient resolution without loss or duplicates',
    async () => {
      if (useDev) {
        console.log(
          'SKIP operational stop/start on explicit development stack; consumer persistence remains asserted',
        );
        return;
      }
      run('docker', ['stop', containers.notification]);
      const response = await api('POST', `/v1/driver/trips/${ids.incidentTrip}/incident`, {
        token: state.tokens.driver,
        key: idemKey('identity-retry'),
        body: { ...incidentBody, description: 'Identity retry incident' },
      });
      assert(response.status === 201, `Retry fixture Incident failed: ${JSON.stringify(response)}`);
      await poll(
        () =>
          count(
            tripSql(
              `SELECT count(*) FROM outbox_events WHERE payload->>'incidentId'='${response.json.data.incidentId}' AND status='PUBLISHED'`,
            ),
          ) === 1,
        'Retry fixture Outbox was not published',
        120_000,
      );
      run('docker', ['stop', containers.identity]);
      composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'notification']);
      await waitFor(`${serviceUrls.notification}/health`);
      await new Promise((resolve) => setTimeout(resolve, 3_000));
      assert(
        count(
          notificationSql(
            `SELECT count(*) FROM notifications WHERE data->>'incidentId'='${response.json.data.incidentId}'`,
          ),
        ) === 0,
        'Identity outage was incorrectly acknowledged',
      );
      composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'identity']);
      await waitFor(`${serviceUrls.identity}/health`);
      await poll(
        () =>
          count(
            notificationSql(
              `SELECT count(*) FROM notifications WHERE type='INCIDENT_REPORTED' AND data->>'incidentId'='${response.json.data.incidentId}'`,
            ),
          ) === 1,
        'Identity retry did not recover exactly once',
        180_000,
      );
    },
  );

  await scenario(
    8,
    'Driver and Assistant arrive pending TripStops once without changing ETA',
    async () => {
      assert(
        scalar(
          tripSql(
            `SELECT driver_user_id||':'||assistant_user_id FROM trips WHERE id='${ids.stopTrip}'`,
          ),
        ) === `${ids.driver}:${ids.assistant}` &&
          scalar(
            tripSql(
              `SELECT driver_user_id||':'||assistant_user_id FROM trips WHERE id='${ids.assistantStopTrip}'`,
            ),
          ) === `${ids.driver}:${ids.assistant}`,
        'Arrival fixtures are not assigned to the expected DRIVER and ASSISTANT',
      );
      const eta = scalar(
        tripSql(
          `SELECT estimated_arrival_time FROM trip_stops WHERE trip_id='${ids.stopTrip}' AND stop_id='${ids.stopTripStop}'`,
        ),
      );
      const driver = await api(
        'POST',
        `/v1/driver/trips/${ids.stopTrip}/stops/${ids.stopTripStop}/arrive`,
        { token: state.tokens.driver, key: idemKey('stop-driver') },
      );
      const assistant = await api(
        'POST',
        `/v1/driver/trips/${ids.assistantStopTrip}/stops/${ids.assistantTripStop}/arrive`,
        { token: state.tokens.assistant, key: idemKey('stop-assistant') },
      );
      assert(
        driver.status === 200 && assistant.status === 200,
        `Stop arrival failed: ${JSON.stringify({ driver, assistant })}`,
      );
      assert(
        scalar(
          tripSql(
            `SELECT status FROM trip_stops WHERE trip_id='${ids.stopTrip}' AND stop_id='${ids.stopTripStop}'`,
          ),
        ) === 'ARRIVED',
        'TripStop did not become ARRIVED',
      );
      assert(
        scalar(
          tripSql(
            `SELECT estimated_arrival_time FROM trip_stops WHERE trip_id='${ids.stopTrip}' AND stop_id='${ids.stopTripStop}'`,
          ),
        ) === eta,
        'Static TripStop ETA changed',
      );
      assert(
        count(
          tripSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='trip.stop.arrived' AND payload->>'stopId'='${ids.stopTripStop}'`,
          ),
        ) === 1,
        'Stop arrival Outbox count is not one',
      );
      const replay = await api(
        'POST',
        `/v1/driver/trips/${ids.stopTrip}/stops/${ids.stopTripStop}/arrive`,
        { token: state.tokens.driver, key: idemKey('stop-driver') },
      );
      assert(
        replay.status === 200 && JSON.stringify(replay.json) === JSON.stringify(driver.json),
        'Stop arrival replay was not stable',
      );
    },
  );

  await scenario(
    9,
    'TripStop guards, removed operator route and two-key race are exact',
    async () => {
      expectError(
        await api(
          'POST',
          `/v1/driver/trips/${ids.boardingTrip}/stops/${ids.boardingTripStop}/arrive`,
          { token: state.tokens.driver, key: randomUUID() },
        ),
        422,
        'TRIP_NOT_IN_PROGRESS',
      );
      expectError(
        await api(
          'POST',
          `/v1/driver/trips/${ids.terminalTrip}/stops/${ids.terminalTripStop}/arrive`,
          { token: state.tokens.driver, key: randomUUID() },
        ),
        422,
        'TRIP_NOT_IN_PROGRESS',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${ids.stopTrip}/stops/${ids.skippedTripStop}/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        409,
        'TRIP_STOP_ALREADY_FINALIZED',
      );
      assert(
        (
          await api(
            'POST',
            `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`,
            { token: state.tokens.unassignedDriver, key: randomUUID() },
          )
        ).status === 403,
        'Unassigned stop arrival was not forbidden',
      );
      assert(
        (
          await api(
            'POST',
            `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`,
            { token: state.tokens.unassignedAssistant, key: randomUUID() },
          )
        ).status === 403,
        'Unassigned Assistant stop arrival was not forbidden',
      );
      assert(
        (
          await api(
            'POST',
            `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`,
            { token: state.tokens.crossDriver, key: randomUUID() },
          )
        ).status === 403,
        'Cross-tenant stop arrival was not forbidden',
      );
      assert(
        (
          await api(
            'POST',
            `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`,
            { token: state.tokens.crossAssistant, key: randomUUID() },
          )
        ).status === 403,
        'Cross-tenant Assistant stop arrival was not forbidden',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${id(999998)}/stops/${ids.stopRaceTripStop}/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        404,
        'TRIP_NOT_FOUND',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${ids.stopRaceTrip}/stops/${id(999997)}/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        404,
        'TRIP_STOP_NOT_FOUND',
      );
      assert(
        (
          await api(
            'POST',
            `/v1/operator/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`,
            { token: state.tokens.admin, key: randomUUID() },
          )
        ).status === 404,
        'Legacy Operator stop-arrival route still exists',
      );
      const race = await Promise.all([
        api('POST', `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`, {
          token: state.tokens.driver,
          key: idemKey('stop-race-a'),
        }),
        api('POST', `/v1/driver/trips/${ids.stopRaceTrip}/stops/${ids.stopRaceTripStop}/arrive`, {
          token: state.tokens.assistant,
          key: idemKey('stop-race-b'),
        }),
      ]);
      assert(
        race.filter((response) => response.status === 200).length === 1 &&
          race.filter(
            (response) =>
              response.status === 409 && errorCode(response) === 'TRIP_STOP_ALREADY_FINALIZED',
          ).length === 1,
        `Stop arrival race invalid: ${JSON.stringify(race)}`,
      );
      assert(
        count(
          tripSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='trip.stop.arrived' AND payload->>'stopId'='${ids.stopRaceTripStop}'`,
          ),
        ) === 1,
        'Stop race emitted duplicate Outbox',
      );
    },
  );

  await scenario(
    10,
    'Destination arrival supports normal and express trips with one-shot races',
    async () => {
      const normal = await api(
        'POST',
        `/v1/driver/trips/${ids.destinationTrip}/destination/arrive`,
        { token: state.tokens.driver, key: idemKey('destination-normal') },
      );
      const express = await api('POST', `/v1/driver/trips/${ids.expressTrip}/destination/arrive`, {
        token: state.tokens.assistant,
        key: idemKey('destination-express'),
      });
      assert(
        normal.status === 200 && express.status === 200,
        `Destination arrival failed: ${JSON.stringify({ normal, express })}`,
      );
      assert(
        scalar(tripSql(`SELECT status FROM trips WHERE id='${ids.destinationTrip}'`)) ===
          'IN_PROGRESS',
        'Destination arrival changed Trip status',
      );
      assert(
        scalar(
          tripSql(
            `SELECT destination_arrived_at IS NOT NULL FROM trips WHERE id='${ids.expressTrip}'`,
          ),
        ) === 't',
        'Express destination anchor missing',
      );
      const replay = await api(
        'POST',
        `/v1/driver/trips/${ids.destinationTrip}/destination/arrive`,
        { token: state.tokens.driver, key: idemKey('destination-normal') },
      );
      assert(
        replay.status === 200 && JSON.stringify(replay.json) === JSON.stringify(normal.json),
        'Destination replay was not stable',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${ids.destinationTrip}/destination/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        409,
        'TRIP_DESTINATION_ALREADY_ARRIVED',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${ids.boardingTrip}/destination/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        422,
        'TRIP_NOT_IN_PROGRESS',
      );
      assert(
        (
          await api('POST', `/v1/driver/trips/${ids.destinationRaceTrip}/destination/arrive`, {
            token: state.tokens.unassignedDriver,
            key: randomUUID(),
          })
        ).status === 403,
        'Unassigned destination arrival was not forbidden',
      );
      expectError(
        await api('POST', `/v1/driver/trips/${id(999996)}/destination/arrive`, {
          token: state.tokens.driver,
          key: randomUUID(),
        }),
        404,
        'TRIP_NOT_FOUND',
      );
      const race = await Promise.all([
        api('POST', `/v1/driver/trips/${ids.destinationRaceTrip}/destination/arrive`, {
          token: state.tokens.driver,
          key: idemKey('destination-race-a'),
        }),
        api('POST', `/v1/driver/trips/${ids.destinationRaceTrip}/destination/arrive`, {
          token: state.tokens.assistant,
          key: idemKey('destination-race-b'),
        }),
      ]);
      assert(
        race.filter((response) => response.status === 200).length === 1 &&
          race.filter(
            (response) =>
              response.status === 409 && errorCode(response) === 'TRIP_DESTINATION_ALREADY_ARRIVED',
          ).length === 1,
        `Destination race invalid: ${JSON.stringify(race)}`,
      );
      assert(
        count(
          tripSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='trip.destination.arrived' AND payload->>'tripId'='${ids.destinationRaceTrip}'`,
          ),
        ) === 1,
        'Destination race emitted duplicate Outbox',
      );
      assert(
        scalar(
          tripSql(
            `SELECT destination_arrived_at IS NULL FROM trips WHERE id='${ids.autoCompletedTrip}'`,
          ),
        ) === 't',
        'Auto-completed Trip incorrectly gained physical anchor',
      );
    },
  );

  await scenario(11, 'Parcel unload rejects missing stop and destination anchors', async () => {
    expectError(
      await api('POST', `/v1/assistant/parcels/${ids.terminalParcel}/unload`, {
        token: state.tokens.assistant,
        key: idemKey('terminal-before-anchor'),
      }),
      422,
      'DESTINATION_TERMINAL_NOT_ARRIVED',
    );
    expectError(
      await api('POST', `/v1/assistant/parcels/${ids.autoCompletedParcel}/unload`, {
        token: state.tokens.assistant,
        key: idemKey('auto-completed-before-anchor'),
      }),
      422,
      'DESTINATION_TERMINAL_NOT_ARRIVED',
    );
    expectError(
      await api('POST', `/v1/assistant/parcels/${ids.stopParcel}/unload`, {
        token: state.tokens.assistant,
        key: idemKey('stop-before-anchor'),
      }),
      422,
      'DROP_OFF_STOP_NOT_ARRIVED',
    );
  });

  await scenario(
    12,
    'Unload performs one IN_TRANSIT to UNLOADED transition and releases cargo once',
    async () => {
      const stopArrival = await api(
        'POST',
        `/v1/driver/trips/${ids.parcelStopTrip}/stops/${ids.parcelTripStop}/arrive`,
        { token: state.tokens.assistant, key: idemKey('parcel-stop-anchor') },
      );
      assert(
        stopArrival.status === 200,
        `Parcel stop anchor failed: ${JSON.stringify(stopArrival)}`,
      );
      const destination = await api(
        'POST',
        `/v1/driver/trips/${ids.parcelExpressTrip}/destination/arrive`,
        { token: state.tokens.assistant, key: idemKey('parcel-destination-anchor') },
      );
      assert(
        destination.status === 200,
        `Parcel destination anchor failed: ${JSON.stringify(destination)}`,
      );
      for (const [parcelId, key] of [
        [ids.stopParcel, 'unload-stop'],
        [ids.terminalParcel, 'unload-terminal'],
      ]) {
        const response = await api('POST', `/v1/assistant/parcels/${parcelId}/unload`, {
          token: state.tokens.assistant,
          key: idemKey(key),
        });
        assert(
          response.status === 200 && response.json?.data?.status === 'UNLOADED',
          `Unload failed: ${JSON.stringify(response)}`,
        );
        const row = scalar(
          parcelSql(
            `SELECT p.status||':'||(p.unloaded_at IS NOT NULL)::int||':'||(p.delivered_pending_confirm_at IS NULL)::int||':'||(SELECT count(*) FROM parcel_delivery_tokens t WHERE t.parcel_id=p.id AND t.revoked_at IS NULL) FROM parcels p WHERE p.id='${parcelId}'`,
          ),
        );
        assert(row === 'UNLOADED:1:1:0', `Unload persistence invalid: ${row}`);
        assert(
          count(
            parcelSql(
              `SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.unloaded' AND payload->>'parcelId'='${parcelId}'`,
            ),
          ) === 1,
          'Unload Outbox count is not one',
        );
        const replay = await api('POST', `/v1/assistant/parcels/${parcelId}/unload`, {
          token: state.tokens.assistant,
          key: idemKey(key),
        });
        assert(
          replay.status === 200 && JSON.stringify(replay.json) === JSON.stringify(response.json),
          'Unload same-key replay changed response',
        );
      }
      for (const [tripId, parcelId] of [
        [ids.parcelStopTrip, ids.stopParcel],
        [ids.parcelExpressTrip, ids.terminalParcel],
      ]) {
        const cargo = scalar(
          tripSql(
            `SELECT state::text FROM trip_cargo_parcels WHERE trip_id='${tripId}' AND parcel_id='${parcelId}'`,
          ),
        );
        assert(cargo === 'RELEASED', `Cargo ledger was not released: ${cargo}`);
        const [loadedWeight, loadedVolume] = tripSql(
          `SELECT total_loaded_weight_kg,total_loaded_volume_m3 FROM trips WHERE id='${tripId}'`,
        )
          .split('|')
          .map(Number);
        assert(
          loadedWeight === 0 && loadedVolume === 0,
          `Loaded counters were not decremented exactly once: weight=${loadedWeight}, volume=${loadedVolume}`,
        );
      }
    },
  );

  await scenario(
    13,
    'Deliver creates one hashed 48h token without releasing cargo again',
    async () => {
      const key = idemKey('deliver-existing-unloaded');
      const response = await api('POST', `/v1/assistant/parcels/${ids.deliverParcel}/deliver`, {
        token: state.tokens.assistant,
        key,
      });
      assert(
        response.status === 200 && response.json?.data?.status === 'DELIVERED_PENDING_CONFIRM',
        `Deliver failed: ${JSON.stringify(response)}`,
      );
      const persisted = parcelSql(
        `SELECT p.status||':'||(p.delivered_pending_confirm_at IS NOT NULL)::int||':'||(SELECT count(*) FROM parcel_delivery_tokens t WHERE t.parcel_id=p.id AND t.revoked_at IS NULL AND t.issue_reason='INITIAL_DELIVERY' AND t.expires_at BETWEEN now()+interval '47 hours' AND now()+interval '49 hours' AND t.token_hash ~ '^[0-9a-f]{64}$') FROM parcels p WHERE p.id='${ids.deliverParcel}'`,
      );
      assert(
        scalar(persisted) === 'DELIVERED_PENDING_CONFIRM:1:1',
        `Deliver persistence invalid: ${persisted}`,
      );
      assert(
        count(
          parcelSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.delivered_pending_confirm' AND payload->>'parcelId'='${ids.deliverParcel}'`,
          ),
        ) === 1,
        'Pending-confirm Outbox count is not one',
      );
      const replay = await api('POST', `/v1/assistant/parcels/${ids.deliverParcel}/deliver`, {
        token: state.tokens.assistant,
        key,
      });
      assert(
        replay.status === 200 && JSON.stringify(replay.json) === JSON.stringify(response.json),
        'Deliver replay changed response',
      );

      const raceParcelId = ids.stopParcel;
      const cargoBefore = count(
        tripSql(
          `SELECT count(*) FROM trip_cargo_parcels WHERE parcel_id='${raceParcelId}' AND state='RELEASED'`,
        ),
      );
      const race = await Promise.all([
        api('POST', `/v1/assistant/parcels/${raceParcelId}/deliver`, {
          token: state.tokens.assistant,
          key: idemKey('deliver-race-a'),
        }),
        api('POST', `/v1/assistant/parcels/${raceParcelId}/deliver`, {
          token: state.tokens.assistant,
          key: idemKey('deliver-race-b'),
        }),
      ]);
      assert(
        race.filter((item) => item.status === 200).length === 1 &&
          race.filter((item) => item.status === 409 && errorCode(item) === 'INVALID_STATUS')
            .length === 1,
        `Deliver race invalid: ${JSON.stringify(race)}`,
      );
      assert(
        count(
          parcelSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.delivered_pending_confirm' AND payload->>'parcelId'='${raceParcelId}'`,
          ),
        ) === 1,
        'Deliver race duplicated Outbox',
      );
      assert(
        count(
          tripSql(
            `SELECT count(*) FROM trip_cargo_parcels WHERE parcel_id='${raceParcelId}' AND state='RELEASED'`,
          ),
        ) === cargoBefore,
        'Deliver released cargo a second time',
      );

      await poll(
        () =>
          count(
            parcelSql(
              `SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.delivered_pending_confirm' AND payload->>'parcelId'='${ids.deliverParcel}' AND status='PUBLISHED'`,
            ),
          ) === 1,
        'Pending-confirm Outbox was not published',
        120_000,
      );
      await poll(
        () =>
          count(
            notificationSql(
              `SELECT count(*) FROM notifications WHERE data->>'parcelId'='${ids.deliverParcel}'`,
            ),
          ) >= 1,
        'Pending-confirm notification missing',
        120_000,
      );
      assert(
        count(
          notificationSql(
            `SELECT count(*) FROM notification_deliveries d JOIN notifications n ON n.id=d.notification_id WHERE n.data->>'parcelId'='${ids.deliverParcel}' AND d.status='SENT'`,
          ),
        ) === 1,
        'Pending-confirm delivery is not exactly one SENT row',
      );

      assert(
        count(
          parcelSql(
            `SELECT count(*) FROM parcel_delivery_tokens WHERE parcel_id='${ids.deliverParcel}' AND revoked_at IS NULL`,
          ),
        ) === 1,
        'Deliver replay changed the active hashed-token cardinality',
      );
    },
  );

  await scenario(14, 'Direct database, Redis, RabbitMQ and migration reconciliation', async () => {
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM incidents i JOIN outbox_events o ON o.payload->>'incidentId'=i.id::text WHERE o.event_type='trip.incident.reported'`,
        ),
      ) >= 4,
      'Incident/Outbox persistence reconciliation failed',
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM trip_stops WHERE status='ARRIVED' AND actual_arrival_time IS NOT NULL`,
        ),
      ) >= 3,
      'TripStop persistence assertions failed',
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM trips WHERE destination_arrived_at IS NOT NULL AND destination_arrived_by_user_id IS NOT NULL`,
        ),
      ) >= 4,
      'Destination persistence assertions failed',
    );
    assert(
      count(
        parcelSql(
          `SELECT count(*) FROM parcels WHERE status IN ('UNLOADED','DELIVERED_PENDING_CONFIRM','DELIVERY_CONFIRMED')`,
        ),
      ) >= 3,
      'Parcel two-step persistence assertions failed',
    );
    const consumerKeys = redis('--scan', '--pattern', 'notification:*')
      .split(/\r?\n/)
      .filter((keyName) => /processed|dedupe/i.test(keyName));
    assert(consumerKeys.length > 0, 'Notification consumer did not persist a processed/dedupe key');
    for (const keyName of consumerKeys) {
      assert(Number(redis('TTL', keyName)) > 0, `Consumer processed key has no TTL: ${keyName}`);
    }
    assert(
      redis('--scan', '--pattern', 'notification:*processing*').trim() === '',
      'Notification processing key was left behind',
    );
    const queues = run('docker', [
      'exec',
      containers.rabbitmq,
      'rabbitmqctl',
      'list_queues',
      '-q',
      'name',
      'messages_ready',
      'messages_unacknowledged',
    ]);
    for (const line of queues.split(/\r?\n/).filter((item) => /retry|dlq|dead/i.test(item))) {
      const [, ready = '0', unacked = '0'] = line.trim().split(/\s+/);
      assert(Number(ready) === 0 && Number(unacked) === 0, `Retry/DLQ not empty: ${line}`);
    }
    await runMigrationGate();
  });

  const gates = [
    ['idempotency', [4]],
    ['incident api/outbox', [1, 2, 3, 5, 6]],
    ['operator notification', [5, 6]],
    ['identity retry', [7]],
    ['stop/destination arrival race', [8, 9, 10]],
    ['parcel unload/deliver two-step', [11, 12, 13]],
    ['database assertions', [14]],
  ];
  for (const [gate, scenarios] of gates) {
    assert(
      scenarios.every((number) =>
        results.some(
          (result) => result.scenario === `E2E-${String(number).padStart(2, '0')}` && result.passed,
        ),
      ),
      `${gate} gate incomplete`,
    );
    console.log(`${gate} PASS`);
  }
}

let failed;
try {
  await runAcceptance();
} catch (error) {
  failed = error;
  results.push({
    scenario: 'HARNESS',
    name: error instanceof Error ? error.message : String(error),
    passed: false,
  });
  console.error(error instanceof Error ? error.stack : error);
  if (!useDev) {
    for (const service of ['identity', 'trip', 'parcel', 'notification', 'gateway']) {
      try {
        console.error(
          `DIAGNOSTIC ${service}\n${run('docker', ['logs', containers[service], '--tail', '120'])}`,
        );
      } catch {
        // Container creation may have failed; cleanup remains mandatory.
      }
    }
  }
} finally {
  if (!useDev) {
    try {
      if (stackOwned) {
        composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
      }
      console.log('cleanup PASS');
    } catch (error) {
      failed ??= error;
      results.push({ scenario: 'CLEANUP', name: String(error), passed: false });
    }
  } else {
    console.log('cleanup PASS (development stack preserved by explicit opt-in)');
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'day39-driver-ops-e2e',
      scenariosPassed: results.filter((result) => result.passed).length,
      results,
    },
    null,
    2,
  ),
);
process.exitCode =
  failed || results.length < 14 || results.some((result) => !result.passed) ? 1 : 0;
