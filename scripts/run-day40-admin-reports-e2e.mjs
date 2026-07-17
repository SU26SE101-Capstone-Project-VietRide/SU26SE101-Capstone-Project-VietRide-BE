import { spawn, spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import amqp from 'amqplib';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const useDev = process.env.DAY40_E2E_USE_DEV_STACK === '1';
const urls = {
  gateway:
    process.env.DAY40_GATEWAY_BASE_URL ||
    (useDev ? 'http://localhost:3000' : 'http://localhost:59300'),
  identity:
    process.env.DAY40_IDENTITY_BASE_URL ||
    (useDev ? 'http://localhost:5001' : 'http://localhost:59001'),
  trip:
    process.env.DAY40_TRIP_BASE_URL ||
    (useDev ? 'http://localhost:5002' : 'http://localhost:59002'),
  booking:
    process.env.DAY40_BOOKING_BASE_URL ||
    (useDev ? 'http://localhost:5003' : 'http://localhost:59003'),
  payment:
    process.env.DAY40_PAYMENT_BASE_URL ||
    (useDev ? 'http://localhost:5004' : 'http://localhost:59004'),
  parcel:
    process.env.DAY40_PARCEL_BASE_URL ||
    (useDev ? 'http://localhost:5005' : 'http://localhost:59005'),
};
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day40-e2e.yml',
];
const e2eEnv = useDev
  ? {}
  : {
      POSTGRES_USER: 'day40_e2e',
      POSTGRES_PASSWORD: 'day40_e2e_postgres_only',
      POSTGRES_PORT: '55440',
      REDIS_PORT: '56382',
      RABBITMQ_USER: 'day40_e2e',
      RABBITMQ_PASSWORD: 'day40_e2e_rabbit_only',
      RABBITMQ_PORT: '55702',
      RABBITMQ_MGMT_PORT: '55703',
      IDENTITY_PORT: '59001',
      TRIP_PORT: '59002',
      BOOKING_PORT: '59003',
      PAYMENT_PORT: '59004',
      PARCEL_PORT: '59005',
      GATEWAY_PORT: '59300',
      INTERNAL_JWT_SECRET: 'day40-e2e-internal-jwt-secret-32-bytes-minimum',
      GOOGLE_OAUTH_CLIENT_ID: '',
      GOOGLE_OAUTH_CLIENT_SECRET: '',
      SYSTEM_ADMIN_BOOTSTRAP_EMAIL: 'bootstrap@day40.test',
      SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: 'Day40-E2E-Only-Password-123!',
      SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME: 'Day 40 Bootstrap Admin',
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
  postgres: useDev ? 'vietride_postgres' : 'day40-e2e-postgres',
  redis: useDev ? 'vietride_redis' : 'day40-e2e-redis',
  rabbitmq: useDev ? 'vietride_rabbitmq' : 'day40-e2e-rabbitmq',
  identity: useDev ? 'vietride_identity' : 'day40-e2e-identity',
  trip: useDev ? 'vietride_trip' : 'day40-e2e-trip',
  booking: useDev ? 'vietride_booking' : 'day40-e2e-booking',
  payment: useDev ? 'vietride_payment' : 'day40-e2e-payment',
  parcel: useDev ? 'vietride_parcel' : 'day40-e2e-parcel',
  gateway: useDev ? 'vietride_gateway' : 'day40-e2e-gateway',
};

const id = (suffix) => `40000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
const ids = {
  operatorA: id(1),
  operatorB: id(2),
  deletedOperator: id(3),
  missingOperator: id(4),
  systemAdmin: id(11),
  operatorAdmin: id(12),
  operatorAdminB: id(13),
  driver: id(14),
  passenger: id(15),
  target: id(102),
  pending: id(103),
  deletedUser: id(104),
  lockedActive: id(105),
  lockedPending: id(106),
  primaryStation: id(301),
  duplicateStation: id(302),
  priorRedirect: id(303),
  deletedStation: id(304),
  destinationStation: id(305),
  graphA: id(311),
  graphB: id(312),
  graphC: id(313),
  conflictPrimary: id(321),
  conflictDuplicate: id(322),
  conflictDestination: id(323),
  vehicleType: id(401),
  vehicle: id(402),
  routeFromDuplicate: id(411),
  routeToDuplicate: id(412),
  conflictRoute: id(413),
  alternativeRoute: id(414),
  mainTrip: id(501),
  liveTrip: id(502),
  completedTripA: id(503),
  completedTripB: id(504),
  nonCompletedTrip: id(505),
  shuttleTrip: id(506),
  completedTripMissing: id(507),
  activeBooking: id(601),
  historicalBooking: id(602),
  reportBookingA: id(603),
  reportBookingB: id(604),
  boundaryBooking: id(605),
  nonCompletedBooking: id(606),
  parcelA: id(701),
  parcelNegative: id(702),
  parcelBoundary: id(703),
  parcelNonTerminal: id(704),
};
const state = { tokens: {}, liveBookingId: null };
const results = [];
const summary = new Set();

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

function runResult(command, args, options = {}) {
  return spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    maxBuffer: 32 * 1024 * 1024,
  });
}

function runAsync(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: root,
      env: { ...process.env, ...options.env },
      windowsHide: true,
    });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => {
      stdout += chunk;
    });
    child.stderr.on('data', (chunk) => {
      stderr += chunk;
    });
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) resolve(stdout.trim());
      else reject(new Error(`${command} ${args.join(' ')} failed: ${stderr || stdout}`));
    });
  });
}

function composeRun(args) {
  return run('docker', [...compose, ...args], { env: e2eEnv });
}

function sqlArgs(database, schema, statement) {
  return [
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
  ];
}

function sql(database, schema, statement) {
  return run('docker', sqlArgs(database, schema, statement));
}

const identitySql = (statement) => sql('vietride_identity', 'vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', 'vietride_trip', statement);
const bookingSql = (statement) => sql('vietride_booking', 'vietride_booking', statement);
const paymentSql = (statement) => sql('vietride_payment', 'vietride_payment', statement);
const parcelSql = (statement) => sql('vietride_parcel', 'vietride_parcel', statement);
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

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
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
    await sleep(500);
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

function mark(name) {
  summary.add(name);
  console.log(`${name} PASS`);
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
    email: `${role.toLowerCase()}-${userId.slice(-4)}@day40.test`,
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

async function internalJwt() {
  const secret = process.env.INTERNAL_JWT_SECRET || e2eEnv.INTERNAL_JWT_SECRET;
  assert(secret, 'INTERNAL_JWT_SECRET is required for internal API acceptance');
  return new SignJWT({ role: 'SYSTEM_ADMIN' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setSubject(ids.systemAdmin)
    .setJti(randomUUID())
    .setIssuedAt()
    .setNotBefore('0s')
    .setExpirationTime('2m')
    .sign(new TextEncoder().encode(secret));
}

async function api(method, pathname, { token, body, key } = {}) {
  const response = await fetch(`${urls.gateway}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {
    // Assertions below report the raw response when JSON is required.
  }
  return { status: response.status, json, text, headers: response.headers };
}

async function tripInternalApi(pathname) {
  const token = await internalJwt();
  const response = await fetch(`${urls.trip}${pathname}`, {
    headers: { 'X-Internal-Auth': `Bearer ${token}` },
  });
  const text = await response.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {
    // Assertions below report the raw response when JSON is required.
  }
  return { status: response.status, json, text, headers: response.headers };
}

function errorCode(response) {
  return response.json?.errorCode ?? response.json?.error?.code ?? response.json?.code;
}

function expectError(response, status, code) {
  assert(
    response.status === status,
    `Expected HTTP ${status}, got ${response.status}: ${response.text}`,
  );
  assert(errorCode(response) === code, `Expected ${code}, got ${errorCode(response)}`);
}

function idemKey(label) {
  const hash = createHash('sha256').update(`day40:${label}`).digest('hex');
  return `${hash.slice(0, 8)}-${hash.slice(8, 12)}-4${hash.slice(13, 16)}-8${hash.slice(17, 20)}-${hash.slice(20, 32)}`;
}

function idemHash(key) {
  return createHash('sha256').update(key).digest('hex').toUpperCase();
}

async function publish(routingKey, payload, transportId = randomUUID()) {
  const connection = await amqp.connect({
    hostname: '127.0.0.1',
    port: useDev ? Number(process.env.RABBITMQ_PORT || 5672) : 55702,
    username: rabbitUser,
    password: rabbitPassword,
  });
  try {
    const channel = await connection.createConfirmChannel();
    await channel.assertExchange('vietride.events', 'topic', { durable: true });
    channel.publish('vietride.events', routingKey, Buffer.from(JSON.stringify(payload)), {
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

function stationSnapshot(stationId, name, slug, city = 'HCM', province = 'HCM') {
  return {
    id: stationId,
    name,
    slug,
    addressStreet: `${name} address`,
    locationId: null,
    city,
    province,
    latitude: 10.77,
    longitude: 106.7,
    contactPhone: '+84910040001',
    contactEmail: null,
    operatingHours: null,
    facilities: null,
    supportsShuttle: false,
    isActive: true,
  };
}

function stationMergedPayload(eventId, primaryStationId, duplicateStationId) {
  const primary = stationSnapshot(primaryStationId, `Primary ${primaryStationId.slice(-4)}`, `p-${primaryStationId.slice(-4)}`);
  const duplicate = stationSnapshot(duplicateStationId, `Duplicate ${duplicateStationId.slice(-4)}`, `d-${duplicateStationId.slice(-4)}`);
  return {
    eventId,
    occurredAt: new Date().toISOString(),
    actorUserId: ids.systemAdmin,
    ipAddress: '127.0.0.1',
    userAgent: 'day40-e2e',
    primaryStationId,
    duplicateStationId,
    primaryBefore: primary,
    duplicateBefore: duplicate,
    primaryAfter: primary,
    relinkedCounts: {
      operatorMappings: 0,
      collapsedOperatorMappings: 0,
      routeOrigins: 0,
      routeDestinations: 0,
      alternativeRoutes: 0,
      shuttleTrips: 0,
      flattenedRedirects: 0,
    },
  };
}

function seedPrerequisites() {
  identitySql(`
    INSERT INTO operators
      (id,name,business_registration_number,tax_code,contact_email,contact_phone,
       registration_status,approved_at,is_active,deleted_at)
    VALUES
      ('${ids.operatorA}','Day 40 Operator A','D40-A-BRN','D40-A-TAX','a@day40.test','+84910040991','APPROVED',now(),true,NULL),
      ('${ids.operatorB}','Day 40 Operator B','D40-B-BRN','D40-B-TAX','b@day40.test','+84910040992','APPROVED',now(),true,NULL),
      ('${ids.deletedOperator}','Day 40 Deleted Operator','D40-D-BRN','D40-D-TAX','deleted@day40.test','+84910040993','APPROVED',now(),false,now())
    ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,registration_status='APPROVED';

    INSERT INTO users
      (id,email,phone,password_hash,display_name,role,status,locked_from_status,operator_id,
       failed_login_attempts,deleted_at)
    SELECT '${ids.systemAdmin}','system-admin@day40.test',NULL,password_hash,
           'Day 40 System Admin','SYSTEM_ADMIN','ACTIVE',NULL,NULL,0,NULL
    FROM users WHERE role='SYSTEM_ADMIN' AND password_hash IS NOT NULL LIMIT 1
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO users
      (id,email,phone,password_hash,display_name,role,status,locked_from_status,operator_id,
       failed_login_attempts,deleted_at)
    SELECT fixture.id::uuid,fixture.email,fixture.phone,password_hash,fixture.display_name,
           fixture.role::user_role,fixture.status::user_status,
           fixture.locked_from_status::user_status,fixture.operator_id::uuid,
           fixture.failed_login_attempts,fixture.deleted_at
    FROM users bootstrap
    CROSS JOIN (VALUES
      ('${ids.operatorAdmin}','operator-admin@day40.test','+84910040012','Day 40 Operator Admin','OPERATOR_ADMIN','ACTIVE',NULL,'${ids.operatorA}',0,NULL::timestamptz),
      ('${ids.operatorAdminB}','operator-admin-b@day40.test','+84910040013','Day 40 Operator Admin B','OPERATOR_ADMIN','ACTIVE',NULL,'${ids.operatorB}',0,NULL::timestamptz),
      ('${ids.driver}','driver@day40.test','+84910040014','Day 40 Driver','DRIVER','ACTIVE',NULL,'${ids.operatorA}',0,NULL::timestamptz),
      ('${ids.passenger}','passenger@day40.test','+84910040015','Day 40 Passenger','PASSENGER','ACTIVE',NULL,NULL,0,NULL::timestamptz),
      ('${ids.target}','target@day40.test','+84910040102','Day 40 Target','PASSENGER','ACTIVE',NULL,NULL,0,NULL::timestamptz),
      ('${ids.pending}','pending@day40.test','+84910040103','Day 40 Pending','PASSENGER','PENDING_EMAIL_VERIFICATION',NULL,NULL,0,NULL::timestamptz),
      ('${ids.deletedUser}','deleted-user@day40.test','+84910040104','Day 40 Deleted User','PASSENGER','DELETED',NULL,NULL,0,now()),
      ('${ids.lockedActive}','locked-active@day40.test','+84910040105','Day 40 Locked Active','PASSENGER','LOCKED','ACTIVE',NULL,5,NULL::timestamptz),
      ('${ids.lockedPending}','locked-pending@day40.test','+84910040106','Day 40 Locked Pending','PASSENGER','LOCKED','PENDING_EMAIL_VERIFICATION',NULL,5,NULL::timestamptz)
    ) fixture(id,email,phone,display_name,role,status,locked_from_status,operator_id,failed_login_attempts,deleted_at)
    WHERE bootstrap.role='SYSTEM_ADMIN' AND bootstrap.password_hash IS NOT NULL
    LIMIT 9
    ON CONFLICT (id) DO NOTHING;
  `);

  tripSql(`
    INSERT INTO stations
      (id,name,slug,address_street,city,province,latitude,longitude,supports_shuttle,
       is_active,deleted_at,merged_into_station_id)
    VALUES
      ('${ids.primaryStation}','Day 40 Primary','day40-primary','Primary address','HCM','HCM',10.7700000,106.7000000,false,true,NULL,NULL),
      ('${ids.duplicateStation}','Day 40 Duplicate','day40-duplicate','Duplicate address','HCM','HCM',10.7710000,106.7010000,false,true,NULL,NULL),
      ('${ids.priorRedirect}','Day 40 Prior Redirect','day40-prior','Prior address','HCM','HCM',10.7720000,106.7020000,false,false,now(),'${ids.duplicateStation}'),
      ('${ids.deletedStation}','Day 40 Deleted','day40-deleted','Deleted address','HCM','HCM',10.7730000,106.7030000,false,false,now(),NULL),
      ('${ids.destinationStation}','Day 40 Destination','day40-destination','Destination address','Da Nang','Da Nang',16.0500000,108.2000000,false,true,NULL,NULL),
      ('${ids.graphA}','Day 40 Graph A','day40-graph-a','A','HCM','HCM',10.7400000,106.6700000,false,true,NULL,NULL),
      ('${ids.graphB}','Day 40 Graph B','day40-graph-b','B','HCM','HCM',10.7410000,106.6710000,false,true,NULL,NULL),
      ('${ids.graphC}','Day 40 Graph C','day40-graph-c','C','HCM','HCM',10.7420000,106.6720000,false,true,NULL,NULL),
      ('${ids.conflictPrimary}','Day 40 Conflict Primary','day40-conflict-primary','CP','HCM','HCM',10.7500000,106.6800000,false,true,NULL,NULL),
      ('${ids.conflictDuplicate}','Day 40 Conflict Duplicate','day40-conflict-duplicate','CD','HCM','HCM',10.7510000,106.6810000,false,true,NULL,NULL),
      ('${ids.conflictDestination}','Day 40 Conflict Destination','day40-conflict-destination','CX','HCM','HCM',10.7520000,106.6820000,false,true,NULL,NULL)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO operator_stations (id,operator_id,station_id,is_active)
    VALUES
      ('${id(430)}','${ids.operatorA}','${ids.primaryStation}',true),
      ('${id(431)}','${ids.operatorA}','${ids.duplicateStation}',true),
      ('${id(432)}','${ids.operatorB}','${ids.duplicateStation}',true)
    ON CONFLICT DO NOTHING;

    INSERT INTO routes
      (id,operator_id,name,origin_station_id,destination_station_id,base_fare,is_active)
    VALUES
      ('${ids.routeFromDuplicate}','${ids.operatorA}','Day 40 From Duplicate','${ids.duplicateStation}','${ids.destinationStation}',100000,true),
      ('${ids.routeToDuplicate}','${ids.operatorA}','Day 40 To Duplicate','${ids.destinationStation}','${ids.duplicateStation}',120000,true),
      ('${ids.conflictRoute}','${ids.operatorA}','Day 40 Conflict Route','${ids.conflictDuplicate}','${ids.conflictPrimary}',90000,true)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO alternative_routes
      (id,route_id,name,destination_station_id,is_active)
    VALUES ('${ids.alternativeRoute}','${ids.routeFromDuplicate}','Day 40 Alternative','${ids.duplicateStation}',true)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_active)
    VALUES ('${ids.vehicleType}','DAY40_BUS','Day 40 Bus',20,true)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO vehicles
      (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active)
    VALUES ('${ids.vehicle}','${ids.operatorA}','${ids.vehicleType}','51B-400.40','{}',20,'ACTIVE',true)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,
       estimated_arrival_time,completed_at,status,source,base_fare)
    VALUES
      ('${ids.mainTrip}','${ids.operatorA}','${ids.routeFromDuplicate}','${ids.vehicle}','${ids.driver}',now()+interval '6 hours',now()+interval '8 hours',NULL,'SCHEDULED','MANUAL',100000),
      ('${ids.liveTrip}','${ids.operatorA}','${ids.routeFromDuplicate}','${ids.vehicle}','${ids.driver}',now()+interval '7 hours',now()+interval '9 hours',NULL,'SCHEDULED','MANUAL',100000),
      ('${ids.completedTripA}','${ids.operatorA}','${ids.routeFromDuplicate}','${ids.vehicle}','${ids.driver}',now()-interval '3 hours',now()-interval '2 hours',now()-interval '1 hour','COMPLETED','MANUAL',100000),
      ('${ids.completedTripB}','${ids.deletedOperator}','${ids.routeToDuplicate}','${ids.vehicle}','${ids.driver}',now()-interval '4 hours',now()-interval '3 hours',now()-interval '30 minutes','COMPLETED','MANUAL',120000),
      ('${ids.nonCompletedTrip}','${ids.operatorA}','${ids.routeFromDuplicate}','${ids.vehicle}','${ids.driver}',now()-interval '5 hours',now()-interval '4 hours',NULL,'CANCELLED','MANUAL',100000),
      ('${ids.completedTripMissing}','${ids.missingOperator}','${ids.routeFromDuplicate}','${ids.vehicle}','${ids.driver}',now()-interval '6 hours',now()-interval '5 hours',now()-interval '10 minutes','COMPLETED','MANUAL',100000)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    VALUES ('${id(520)}','${ids.liveTrip}','D01','STANDARD','AVAILABLE')
    ON CONFLICT (trip_id,seat_number) DO UPDATE SET status='AVAILABLE',disabled_reason=NULL;

    INSERT INTO shuttle_trips
      (id,operator_id,main_trip_id,station_id,direction,driver_user_id,vehicle_id,status,
       scheduled_departure_time,scheduled_end_time)
    VALUES ('${ids.shuttleTrip}','${ids.operatorA}','${ids.mainTrip}','${ids.duplicateStation}',
            'INBOUND_TO_STATION','${ids.driver}','${ids.vehicle}','SCHEDULED',
            now()+interval '4 hours',now()+interval '5 hours')
    ON CONFLICT (id) DO NOTHING;
  `);

  bookingSql(`
    INSERT INTO bookings
      (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,
       dropoff_station_id,base_fare,discount_amount,total_amount,status,confirmed_at,
       completed_at,created_at,updated_at)
    VALUES
      ('${ids.activeBooking}','VR-D40-ACTIVE','${ids.passenger}','${ids.mainTrip}','${ids.operatorA}','${ids.duplicateStation}','${ids.destinationStation}',100000,0,100000,'CONFIRMED',now()-interval '1 hour',NULL,now()-interval '2 hours',now()),
      ('${ids.historicalBooking}','VR-D40-HISTORY','${ids.passenger}','${ids.mainTrip}','${ids.operatorA}','${ids.duplicateStation}','${ids.destinationStation}',100000,0,100000,'COMPLETED',now()-interval '3 hours',now()-interval '2 hours',now()-interval '4 hours',now()),
      ('${ids.reportBookingA}','VR-D40-REPORT-A','${ids.passenger}','${ids.completedTripA}','${ids.operatorA}','${ids.primaryStation}','${ids.destinationStation}',300000,0,300000,'COMPLETED',now()-interval '2 hours',now()-interval '1 hour',now()-interval '3 hours',now()),
      ('${ids.reportBookingB}','VR-D40-REPORT-B','${ids.passenger}','${ids.completedTripB}','${ids.deletedOperator}','${ids.primaryStation}','${ids.destinationStation}',200000,0,200000,'COMPLETED',now()-interval '2 hours',now()-interval '30 minutes',now()-interval '3 hours',now()),
      ('${ids.boundaryBooking}','VR-D40-BOUNDARY','${ids.passenger}','${ids.completedTripA}','${ids.operatorA}','${ids.primaryStation}','${ids.destinationStation}',90000,0,90000,'COMPLETED',now()-interval '2 hours',now()-interval '2 days',now()-interval '3 days',now()),
      ('${ids.nonCompletedBooking}','VR-D40-NONTERM','${ids.passenger}','${ids.mainTrip}','${ids.operatorA}','${ids.primaryStation}','${ids.destinationStation}',70000,0,70000,'CONFIRMED',now()-interval '1 hour',NULL,now()-interval '2 hours',now())
    ON CONFLICT (id) DO NOTHING;
  `);

  parcelSql(`
    INSERT INTO parcels
      (id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,
       size_category,estimated_weight_kg,delivery_method,total_price_vnd,deposit_percent,
       deposit_amount,original_deposit_amount,discount_amount,additional_amount,refund_amount,
       status,confirmed_at,created_at,updated_at)
    VALUES
      ('${ids.parcelA}','VRP-D40-A','${ids.passenger}','Recipient A','+84910040701','${ids.operatorA}','${ids.completedTripA}','SMALL',1,'TERMINAL_PICKUP',150000,100,100000,100000,0,50000,10000,'DELIVERY_CONFIRMED',now()-interval '45 minutes',now()-interval '2 hours',now()),
      ('${ids.parcelNegative}','VRP-D40-N','${ids.passenger}','Recipient N','+84910040702','${ids.deletedOperator}','${ids.completedTripB}','SMALL',1,'TERMINAL_PICKUP',50000,100,10000,10000,0,0,50000,'DELIVERY_CONFIRMED',now()-interval '20 minutes',now()-interval '2 hours',now()),
      ('${ids.parcelBoundary}','VRP-D40-B','${ids.passenger}','Recipient B','+84910040703','${ids.operatorA}','${ids.completedTripA}','SMALL',1,'TERMINAL_PICKUP',70000,100,70000,70000,0,0,0,'DELIVERY_CONFIRMED',now()-interval '2 days',now()-interval '3 days',now()),
      ('${ids.parcelNonTerminal}','VRP-D40-P','${ids.passenger}','Recipient P','+84910040704','${ids.operatorA}','${ids.mainTrip}','SMALL',1,'TERMINAL_PICKUP',80000,100,80000,80000,0,0,0,'PENDING',NULL,now()-interval '1 hour',now())
    ON CONFLICT (id) DO NOTHING;
  `);

  paymentSql(`
    INSERT INTO wallets (user_id,balance,currency,row_version)
    VALUES ('${ids.passenger}',5000000,'VND',0)
    ON CONFLICT (user_id) DO UPDATE SET balance=5000000,row_version=0;
  `);

  redis('SET', `identity:login_lockout:${ids.lockedActive}`, '5', 'EX', '3600');
  redis('SET', `identity:login_lockout:${ids.lockedPending}`, '5', 'EX', '3600');
}

function seedRaceUser(suffix, { status = 'ACTIVE', lockedFromStatus = null } = {}) {
  const userId = id(10_000 + suffix);
  const phone = `+8492${String(suffix).padStart(8, '0')}`;
  identitySql(`
    INSERT INTO users
      (id,email,phone,password_hash,display_name,role,status,locked_from_status,
       failed_login_attempts,created_at,updated_at)
    SELECT '${userId}','race-${suffix}@day40.test','${phone}',password_hash,
           'Day 40 Race ${suffix}','PASSENGER','${status}',
           ${lockedFromStatus ? `'${lockedFromStatus}'` : 'NULL'},
           ${status === 'LOCKED' ? 5 : 0},now(),now()
    FROM users WHERE id='${ids.systemAdmin}'
    ON CONFLICT (id) DO UPDATE SET
      status=EXCLUDED.status,
      locked_from_status=EXCLUDED.locked_from_status,
      failed_login_attempts=EXCLUDED.failed_login_attempts,
      deleted_at=NULL;
  `);
  return { id: userId, email: `race-${suffix}@day40.test` };
}

function reportPath(from, to) {
  return `/v1/admin/reports/platform?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;
}

function currentReportRange() {
  return {
    from: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    to: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  };
}

function startRowLock(database, schema, table, rowIds, seconds = 3) {
  const idsSql = rowIds.map((rowId) => `'${rowId}'`).join(',');
  const marker = `day40-row-lock-${randomUUID()}`;
  const done = runAsync(
    'docker',
    sqlArgs(
      database,
      schema,
      `/* ${marker} */ BEGIN; SELECT id FROM ${table} WHERE id IN (${idsSql}) ORDER BY id::text FOR UPDATE; SELECT pg_sleep(${seconds}); COMMIT;`,
    ),
  );
  const ready = poll(
    () =>
      count(
        sql(
          database,
          schema,
          `SELECT count(*) FROM pg_stat_activity WHERE datname='${database}' AND wait_event='PgSleep' AND position('${marker}' in query) > 0`,
        ),
      ) === 1,
    `Timed out waiting for PostgreSQL row-lock barrier ${marker}`,
    10_000,
  );
  return { done, ready };
}

function rabbitQueues() {
  return run('docker', [
    'exec',
    containers.rabbitmq,
    'rabbitmqctl',
    'list_queues',
    '--quiet',
    'name',
    'messages_ready',
    'messages_unacknowledged',
  ]);
}

async function runMigrationGate() {
  if (useDev) return;
  const services = [
    {
      name: 'identity',
      project: 'apps/identity/src/VietRide.Identity.Infrastructure',
      envKey: 'IDENTITY_DESIGN_CONNECTION',
      schema: 'vietride_identity',
      previous: '20260714080233_EnforceSingleActiveSubscriptionUpgradeAttempt',
      migrations: [
        '20260716114846_AddLockedFromStatus',
        '20260716132910_AddImmutableActivityLogReadModel',
        '20260716182216_AddStationAuditActions',
      ],
      absentCheck:
        "SELECT count(*) FROM information_schema.columns WHERE table_schema='vietride_identity' AND table_name='users' AND column_name='locked_from_status';",
    },
    {
      name: 'trip',
      project: 'apps/trip/src/VietRide.Trip.Infrastructure',
      envKey: 'TRIP_DESIGN_CONNECTION',
      schema: 'vietride_trip',
      previous: '20260715133857_AddTripDestinationArrival',
      migrations: [
        '20260716142716_AddStationMergeRedirect',
        '20260716194532_AddCompletedTripReportIndex',
      ],
      absentCheck:
        "SELECT count(*) FROM information_schema.columns WHERE table_schema='vietride_trip' AND table_name='stations' AND column_name='merged_into_station_id';",
    },
    {
      name: 'booking',
      project: 'apps/booking/src/VietRide.Booking.Infrastructure',
      envKey: 'BOOKING_DESIGN_CONNECTION',
      schema: 'vietride_booking',
      previous: '20260712182713_AddBookingShuttleIntent',
      migrations: [
        '20260716165252_AddBookingStationRedirects',
        '20260716191518_AddCompletedBookingReportIndex',
      ],
      absentCheck:
        "SELECT count(*) FROM information_schema.tables WHERE table_schema='vietride_booking' AND table_name='booking_station_redirects';",
    },
    {
      name: 'parcel',
      project: 'apps/parcel/src/VietRide.Parcel.Infrastructure',
      envKey: 'PARCEL_DESIGN_CONNECTION',
      schema: 'vietride_parcel',
      previous: '20260714113506_PreserveExplicitParcelUpdatedAt',
      migrations: ['20260716201420_AddConfirmedParcelReportIndex'],
      absentCheck:
        "SELECT count(*) FROM pg_indexes WHERE schemaname='vietride_parcel' AND indexname='idx_parcels_confirmed_report';",
    },
  ];

  for (const service of services) {
    const scratch = `day40_${service.name}_migration`;
    run('docker', [
      'exec',
      containers.postgres,
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      postgresUser,
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
      postgresUser,
      '-d',
      'postgres',
      '-c',
      `CREATE DATABASE ${scratch};`,
    ]);
    const connection =
      `Host=127.0.0.1;Port=55440;Database=${scratch};` +
      `Username=${postgresUser};Password=${postgresPassword}`;
    const migrate = (target) =>
      run(
        'dotnet',
        [
          'ef',
          'database',
          'update',
          ...(target ? [target] : []),
          '--project',
          service.project,
          '--configuration',
          'Release',
          '--no-build',
        ],
        { env: { [service.envKey]: connection } },
      );

    migrate();
    const applied = sql(
      scratch,
      service.schema,
      'SELECT "MigrationId" FROM "__ef_migrations_history" ORDER BY "MigrationId";',
    );
    for (const migration of service.migrations) {
      assert(applied.includes(migration), `${service.name} migration missing: ${migration}`);
    }
    migrate(service.previous);
    assert(
      count(sql(scratch, service.schema, service.absentCheck)) === 0,
      `${service.name} rollback left Day 40 schema artifacts`,
    );
    migrate();
    const reapplied = sql(
      scratch,
      service.schema,
      'SELECT "MigrationId" FROM "__ef_migrations_history" ORDER BY "MigrationId";',
    );
    for (const migration of service.migrations) {
      assert(reapplied.includes(migration), `${service.name} migration reapply missing: ${migration}`);
    }
  }
}

async function runAcceptance() {
  if (!useDev) {
    composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
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
      'booking',
      'payment',
      'parcel',
      'gateway',
    ]);
  }

  await Promise.all([
    waitFor(`${urls.gateway}/health`),
    waitFor(`${urls.gateway}/ready`),
    waitFor(`${urls.identity}/health`),
    waitFor(`${urls.identity}/ready`),
    waitFor(`${urls.trip}/health`),
    waitFor(`${urls.trip}/ready`),
    waitFor(`${urls.booking}/health`),
    waitFor(`${urls.booking}/ready`),
    waitFor(`${urls.payment}/health`),
    waitFor(`${urls.payment}/ready`),
    waitFor(`${urls.parcel}/health`),
    waitFor(`${urls.parcel}/ready`),
  ]);

  seedPrerequisites();
  state.tokens = {
    systemAdmin: await userJwt(ids.systemAdmin, 'SYSTEM_ADMIN'),
    operatorAdmin: await userJwt(ids.operatorAdmin, 'OPERATOR_ADMIN', ids.operatorA),
    operatorAdminB: await userJwt(ids.operatorAdminB, 'OPERATOR_ADMIN', ids.operatorB),
    passenger: await userJwt(ids.passenger, 'PASSENGER'),
    driver: await userJwt(ids.driver, 'DRIVER', ids.operatorA),
  };
  mark('seed');

  await scenario(1, 'Admin user directory filters, paging, redaction, and RBAC', async () => {
    const active = await api(
      'GET',
      '/v1/admin/users?role=PASSENGER&status=ACTIVE&page=1&pageSize=2&sortBy=email&sortDir=asc',
      { token: state.tokens.systemAdmin },
    );
    assert(active.status === 200 && active.json?.data?.items?.length <= 2, active.text);
    assert(active.json.data.page === 1 && active.json.data.pageSize === 2, 'Paging drifted');
    assert(
      active.json.data.items.every(
        (item) =>
          !('passwordHash' in item) &&
          !('failedLoginAttempts' in item) &&
          !('oauthSubject' in item),
      ),
      'Admin directory leaked secret fields',
    );

    const deleted = await api(
      'GET',
      '/v1/admin/users?status=DELETED&includeDeleted=true&page=1&pageSize=20',
      { token: state.tokens.systemAdmin },
    );
    assert(
      deleted.status === 200 &&
        deleted.json.data.items.some((item) => item.id === ids.deletedUser),
      `Deleted user missing: ${deleted.text}`,
    );
    const hidden = await api(
      'GET',
      '/v1/admin/users?status=DELETED&includeDeleted=false&page=1&pageSize=20',
      { token: state.tokens.systemAdmin },
    );
    assert(hidden.status === 200 && hidden.json.data.items.length === 0, hidden.text);
    const forbidden = await api('GET', '/v1/admin/users', {
      token: state.tokens.operatorAdmin,
    });
    expectError(forbidden, 403, 'FORBIDDEN');
    const invalid = await api('GET', '/v1/admin/users?role=NOT_A_ROLE', {
      token: state.tokens.systemAdmin,
    });
    expectError(invalid, 422, 'VALIDATION_ERROR');
    mark('admin users');
  });

  await scenario(2, 'Lock/unlock revokes refresh sessions and restores locked origins', async () => {
    const login = await api('POST', '/v1/auth/login', {
      body: {
        email: 'target@day40.test',
        password: e2eEnv.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD || 'Day40-E2E-Only-Password-123!',
      },
    });
    assert(login.status === 200 && login.json?.data?.refreshToken, `Target login failed: ${login.text}`);

    const lockKey = idemKey('lifecycle-lock');
    const locked = await api('POST', `/v1/admin/users/${ids.target}/lock`, {
      token: state.tokens.systemAdmin,
      key: lockKey,
    });
    assert(
      locked.status === 200 &&
        locked.json?.data?.status === 'LOCKED' &&
        locked.json?.data?.statusChanged === true,
      locked.text,
    );
    assert(
      count(
        identitySql(
          `SELECT count(*) FROM refresh_tokens WHERE user_id='${ids.target}' AND revoked_at IS NULL`,
        ),
      ) === 0,
      'Lock left an active refresh token',
    );
    const refresh = await api('POST', '/v1/auth/refresh', {
      body: { refreshToken: login.json.data.refreshToken },
    });
    assert([401, 403].includes(refresh.status), `Revoked refresh token survived: ${refresh.text}`);
    const replay = await api('POST', `/v1/admin/users/${ids.target}/lock`, {
      token: state.tokens.systemAdmin,
      key: lockKey,
    });
    assert(replay.status === 200 && replay.text === locked.text, 'Lock replay was not byte-equivalent');
    const selfLock = await api('POST', `/v1/admin/users/${ids.systemAdmin}/lock`, {
      token: state.tokens.systemAdmin,
      key: idemKey('self-lock'),
    });
    expectError(selfLock, 403, 'FORBIDDEN');

    const unlocked = await api('POST', `/v1/admin/users/${ids.target}/unlock`, {
      token: state.tokens.systemAdmin,
      key: idemKey('lifecycle-unlock'),
    });
    assert(
      unlocked.status === 200 && unlocked.json?.data?.status === 'ACTIVE',
      `Unlock failed: ${unlocked.text}`,
    );
    const loginAfter = await api('POST', '/v1/auth/login', {
      body: {
        email: 'target@day40.test',
        password: e2eEnv.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD || 'Day40-E2E-Only-Password-123!',
      },
    });
    assert(loginAfter.status === 200, `Login after unlock failed: ${loginAfter.text}`);

    const pendingUnlock = await api('POST', `/v1/admin/users/${ids.lockedPending}/unlock`, {
      token: state.tokens.systemAdmin,
      key: idemKey('pending-origin-unlock'),
    });
    assert(
      pendingUnlock.status === 200 &&
        pendingUnlock.json?.data?.status === 'PENDING_EMAIL_VERIFICATION',
      `Pending origin was promoted: ${pendingUnlock.text}`,
    );
    assert(
      scalar(identitySql(`SELECT status FROM users WHERE id='${ids.lockedPending}'`)) ===
        'PENDING_EMAIL_VERIFICATION',
      'Locked pending user did not restore pending origin',
    );
    assert(
      Number(redis('EXISTS', `identity:login_lockout:${ids.lockedPending}`)) === 0,
      'Unlock did not reset Redis lockout',
    );
    mark('lock/unlock');
    mark('locked origin restore');
  });

  await scenario(3, 'Concurrent lock versus password login and refresh is linearizable', async () => {
    const password = e2eEnv.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD || 'Day40-E2E-Only-Password-123!';
    for (let iteration = 0; iteration < 5; iteration += 1) {
      const user = seedRaceUser(100 + iteration);
      await Promise.all([
        api('POST', `/v1/admin/users/${user.id}/lock`, {
          token: state.tokens.systemAdmin,
          key: idemKey(`login-race-lock-${iteration}`),
        }),
        api('POST', '/v1/auth/login', {
          body: { email: user.email, password },
        }),
      ]);
      assert(
        scalar(identitySql(`SELECT status FROM users WHERE id='${user.id}'`)) === 'LOCKED',
        `Login race ${iteration} escaped final LOCKED state`,
      );
      assert(
        count(
          identitySql(
            `SELECT count(*) FROM refresh_tokens WHERE user_id='${user.id}' AND revoked_at IS NULL`,
          ),
        ) === 0,
        `Login race ${iteration} left active refresh token`,
      );

      const refreshUser = seedRaceUser(200 + iteration);
      const login = await api('POST', '/v1/auth/login', {
        body: { email: refreshUser.email, password },
      });
      assert(login.status === 200 && login.json?.data?.refreshToken, login.text);
      await Promise.all([
        api('POST', `/v1/admin/users/${refreshUser.id}/lock`, {
          token: state.tokens.systemAdmin,
          key: idemKey(`refresh-race-lock-${iteration}`),
        }),
        api('POST', '/v1/auth/refresh', {
          body: { refreshToken: login.json.data.refreshToken },
        }),
      ]);
      assert(
        scalar(identitySql(`SELECT status FROM users WHERE id='${refreshUser.id}'`)) === 'LOCKED' &&
          count(
            identitySql(
              `SELECT count(*) FROM refresh_tokens WHERE user_id='${refreshUser.id}' AND revoked_at IS NULL`,
            ),
          ) === 0,
        `Refresh race ${iteration} violated lock invariant`,
      );
    }
    mark('identity race invariants');
  });

  await scenario(4, 'Failed-login, forgot-password, and reset-password races preserve lock', async () => {
    const password = e2eEnv.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD || 'Day40-E2E-Only-Password-123!';
    const failedUser = seedRaceUser(300);
    await Promise.all([
      api('POST', `/v1/admin/users/${failedUser.id}/lock`, {
        token: state.tokens.systemAdmin,
        key: idemKey('failed-login-lock'),
      }),
      api('POST', '/v1/auth/login', {
        body: { email: failedUser.email, password: 'WrongPassword123!' },
      }),
    ]);
    const failedState = scalar(
      identitySql(
        `SELECT status||':'||failed_login_attempts FROM users WHERE id='${failedUser.id}'`,
      ),
    );
    assert(/^LOCKED:(0|1)$/.test(failedState), `Failed-login race drifted: ${failedState}`);

    const forgotUser = seedRaceUser(301);
    await Promise.all([
      api('POST', `/v1/admin/users/${forgotUser.id}/lock`, {
        token: state.tokens.systemAdmin,
        key: idemKey('forgot-lock'),
      }),
      api('POST', '/v1/auth/forgot-password', { body: { email: forgotUser.email } }),
    ]);
    assert(
      scalar(identitySql(`SELECT status FROM users WHERE id='${forgotUser.id}'`)) === 'LOCKED',
      'Forgot-password race unlocked user',
    );
    const forgotOtpCount = count(
      identitySql(
        `SELECT count(*) FROM email_verification_tokens WHERE user_id='${forgotUser.id}' AND purpose='PASSWORD_RESET'`,
      ),
    );
    assert([0, 1].includes(forgotOtpCount), `Forgot race duplicated OTP: ${forgotOtpCount}`);

    const resetUser = seedRaceUser(302);
    const resetCode = '654321';
    identitySql(`
      INSERT INTO email_verification_tokens (id,user_id,purpose,code,expires_at,failed_attempts)
      VALUES ('${id(20_302)}','${resetUser.id}','PASSWORD_RESET','${resetCode}',now()+interval '10 minutes',0)
      ON CONFLICT DO NOTHING;
    `);
    const [lockResult, resetResult] = await Promise.all([
      api('POST', `/v1/admin/users/${resetUser.id}/lock`, {
        token: state.tokens.systemAdmin,
        key: idemKey('reset-lock'),
      }),
      api('POST', '/v1/auth/reset-password', {
        body: { email: resetUser.email, code: resetCode, newPassword: 'Day40-New-Password-123!' },
      }),
    ]);
    assert(lockResult.status === 200, lockResult.text);
    assert([200, 400, 422].includes(resetResult.status), resetResult.text);
    assert(
      scalar(identitySql(`SELECT status FROM users WHERE id='${resetUser.id}'`)) === 'LOCKED',
      'Reset-password race escaped final LOCKED state',
    );
    assert(password.length > 0, 'Password fixture missing');
    mark('password reset lock race');
  });

  await scenario(5, 'Shared idempotency pending, replay, and mismatch are exact', async () => {
    identitySql(`
      UPDATE users SET status='ACTIVE',locked_from_status=NULL,failed_login_attempts=0
      WHERE id='${ids.target}';
    `);
    const key = idemKey('shared-pending-lock');
    const rowLock = startRowLock(
      'vietride_identity',
      'vietride_identity',
      'users',
      [ids.target],
      5,
    );
    await rowLock.ready;
    const firstPromise = api('POST', `/v1/admin/users/${ids.target}/lock`, {
      token: state.tokens.systemAdmin,
      key,
    });
    await sleep(200);
    const second = await api('POST', `/v1/admin/users/${ids.target}/lock`, {
      token: state.tokens.systemAdmin,
      key,
    });
    const first = await firstPromise;
    await rowLock.done;
    assert(first.status === 200, first.text);
    expectError(second, 409, 'IDEMPOTENCY_REQUEST_PENDING');
    const replay = await api('POST', `/v1/admin/users/${ids.target}/lock`, {
      token: state.tokens.systemAdmin,
      key,
    });
    assert(replay.status === 200 && replay.text === first.text, 'Completed replay was not exact');
    const mismatch = await api('POST', `/v1/admin/users/${ids.target}/unlock`, {
      token: state.tokens.systemAdmin,
      key,
    });
    expectError(mismatch, 422, 'IDEMPOTENCY_KEY_MISMATCH');
    const missing = await api('POST', `/v1/admin/users/${ids.target}/unlock`, {
      token: state.tokens.systemAdmin,
    });
    expectError(missing, 422, 'IDEMPOTENCY_KEY_REQUIRED');
    assert(
      Number(redis('EXISTS', `identity:idem:v2:response:${idemHash(key)}`)) === 1,
      'Completed idempotency response missing from Redis',
    );
    await api('POST', `/v1/admin/users/${ids.target}/unlock`, {
      token: state.tokens.systemAdmin,
      key: idemKey('shared-pending-cleanup'),
    });
    mark('shared idempotency');
  });

  await scenario(6, 'ActivityLog query is redacted and PostgreSQL-immutable', async () => {
    const response = await api(
      'GET',
      `/v1/admin/activity-logs?userId=${ids.systemAdmin}&page=1&pageSize=100`,
      { token: state.tokens.systemAdmin },
    );
    assert(response.status === 200 && response.json?.data?.items?.length > 0, response.text);
    const serialized = JSON.stringify(response.json.data.items);
    assert(
      !/password|refreshToken|otpCode/i.test(serialized),
      'ActivityLog response leaked credential material',
    );
    const logId = scalar(
      identitySql(
        `SELECT id FROM activity_logs WHERE user_id='${ids.systemAdmin}' ORDER BY created_at DESC LIMIT 1`,
      ),
    );
    const mutation = runResult(
      'docker',
      sqlArgs(
        'vietride_identity',
        'vietride_identity',
        `UPDATE activity_logs SET ip_address='10.0.0.1' WHERE id='${logId}';`,
      ),
    );
    assert(mutation.status !== 0, 'ActivityLog UPDATE unexpectedly succeeded');
    assert(
      `${mutation.stderr}${mutation.stdout}`.includes('activity_logs is append-only'),
      'ActivityLog trigger failed with the wrong reason',
    );
    mark('activity immutability');
  });

  await scenario(7, 'Station normalize persists one Outbox event and one Identity audit', async () => {
    const response = await api('PATCH', `/v1/admin/stations/${ids.primaryStation}`, {
      token: state.tokens.systemAdmin,
      key: idemKey('normalize-primary'),
      body: {
        addressStreet: '  40 Nguyen Hue  ',
        supportsShuttle: true,
      },
    });
    assert(
      response.status === 200 &&
        response.json?.data?.id === ids.primaryStation &&
        response.json?.data?.supportsShuttle === true,
      response.text,
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM outbox_events WHERE event_type='trip.station.normalized' AND payload->>'stationId'='${ids.primaryStation}'`,
        ),
      ) === 1,
      'Normalize emitted duplicate/missing Outbox event',
    );
    const normalizeEventId = scalar(
      tripSql(
        `SELECT payload->>'eventId' FROM outbox_events WHERE event_type='trip.station.normalized' AND payload->>'stationId'='${ids.primaryStation}'`,
      ),
    );
    assert(normalizeEventId, 'Normalize Outbox event id is missing');
    await poll(
      () =>
        count(
          identitySql(
            `SELECT count(*) FROM activity_logs WHERE action='STATION_NORMALIZED' AND source_event_id='${normalizeEventId}' AND user_id='${ids.systemAdmin}'`,
          ),
        ) === 1,
      'Identity normalize audit timeout',
    );
    mark('station normalize');
  });

  await scenario(8, 'Station merge is atomic, idempotent, and fans out to consumers', async () => {
    const key = idemKey('merge-primary');
    const rowLock = startRowLock(
      'vietride_trip',
      'vietride_trip',
      'stations',
      [ids.primaryStation, ids.duplicateStation],
      5,
    );
    await rowLock.ready;
    const firstPromise = api('POST', `/v1/admin/stations/${ids.primaryStation}/merge`, {
      token: state.tokens.systemAdmin,
      key,
      body: { duplicateId: ids.duplicateStation },
    });
    await sleep(200);
    const pending = await api('POST', `/v1/admin/stations/${ids.primaryStation}/merge`, {
      token: state.tokens.systemAdmin,
      key,
      body: { duplicateId: ids.duplicateStation },
    });
    const first = await firstPromise;
    await rowLock.done;
    assert(
      first.status === 200 &&
        first.json?.data?.primaryStation?.id === ids.primaryStation &&
        first.json?.data?.duplicateStationId === ids.duplicateStation,
      first.text,
    );
    expectError(pending, 409, 'IDEMPOTENCY_REQUEST_PENDING');
    const replay = await api('POST', `/v1/admin/stations/${ids.primaryStation}/merge`, {
      token: state.tokens.systemAdmin,
      key,
      body: { duplicateId: ids.duplicateStation },
    });
    assert(replay.status === 200 && replay.text === first.text, 'Merge replay drifted');
    const mergeEventId = scalar(
      tripSql(
        `SELECT payload->>'eventId' FROM outbox_events WHERE event_type='trip.station.merged' AND payload->>'duplicateStationId'='${ids.duplicateStation}'`,
      ),
    );
    assert(mergeEventId, 'Merge Outbox event id is missing');

    const liveBooking = await api('POST', '/v1/bookings', {
      token: state.tokens.passenger,
      key: idemKey('live-booking-canonical-race'),
      body: {
        tripId: ids.liveTrip,
        pickup: { stationId: ids.duplicateStation },
        dropoff: { stationId: ids.destinationStation },
        seats: [{ seatNumber: 'D01' }],
        paymentMethod: 'WALLET',
      },
    });
    assert(liveBooking.status === 201, `Canonical create booking failed: ${liveBooking.text}`);
    state.liveBookingId = liveBooking.json?.data?.bookingId;

    await poll(
      () =>
        count(
          bookingSql(
            `SELECT count(*) FROM booking_station_redirects WHERE duplicate_station_id='${ids.duplicateStation}' AND canonical_station_id='${ids.primaryStation}'`,
          ),
        ) === 1,
      'Booking merge consumer timeout',
    );
    await poll(
      () =>
        count(
          identitySql(
            `SELECT count(*) FROM activity_logs WHERE action='STATION_MERGED' AND source_event_id='${mergeEventId}' AND user_id='${ids.systemAdmin}'`,
          ),
        ) === 1,
      'Identity merge audit timeout',
    );
    assert(
      scalar(
        tripSql(
          `SELECT (deleted_at IS NOT NULL)::int||':'||merged_into_station_id FROM stations WHERE id='${ids.duplicateStation}'`,
        ),
      ) === `1:${ids.primaryStation}`,
      'Duplicate Station redirect state is invalid',
    );
    assert(
      scalar(
        tripSql(
          `SELECT merged_into_station_id FROM stations WHERE id='${ids.priorRedirect}'`,
        ),
      ) === ids.primaryStation,
      'Prior Station redirect was not flattened',
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM routes WHERE (origin_station_id='${ids.duplicateStation}' OR destination_station_id='${ids.duplicateStation}')`,
        ),
      ) === 0 &&
        count(
          tripSql(
            `SELECT count(*) FROM alternative_routes WHERE destination_station_id='${ids.duplicateStation}'`,
          ),
        ) === 0 &&
        count(
          tripSql(`SELECT count(*) FROM shuttle_trips WHERE station_id='${ids.duplicateStation}'`),
        ) === 0,
      'Trip aggregate references were not fully relinked',
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM operator_stations WHERE operator_id='${ids.operatorA}' AND station_id='${ids.primaryStation}'`,
        ),
      ) === 1,
      'OperatorStation collision was not collapsed',
    );
    assert(
      scalar(
        bookingSql(`SELECT pickup_station_id FROM bookings WHERE id='${ids.activeBooking}'`),
      ) === ids.primaryStation,
      'Active Booking was not relinked',
    );
    assert(
      scalar(
        bookingSql(`SELECT pickup_station_id FROM bookings WHERE id='${ids.historicalBooking}'`),
      ) === ids.duplicateStation,
      'Historical Booking was rewritten',
    );
    assert(
      scalar(
        bookingSql(`SELECT pickup_station_id FROM bookings WHERE id='${state.liveBookingId}'`),
      ) === ids.primaryStation,
      'Concurrent canonical Booking writer persisted duplicate Station',
    );
    await poll(
      () =>
        count(
          tripSql(
            `SELECT count(*) FROM outbox_events WHERE event_type='trip.station.merged' AND payload->>'duplicateStationId'='${ids.duplicateStation}' AND status='PUBLISHED'`,
          ),
        ) === 1,
      'Station merge Outbox was not published',
    );
    mark('station merge');
    mark('booking relink');
    mark('booking station race invariants');
    mark('audit consumers');
  });

  await scenario(9, 'Booking redirect graph converges under out-of-order events and replay', async () => {
    const eventAB = id(30_901);
    const eventBC = id(30_902);
    const payloadAB = stationMergedPayload(eventAB, ids.graphB, ids.graphA);
    const payloadBC = stationMergedPayload(eventBC, ids.graphC, ids.graphB);
    await Promise.all([
      publish('trip.station.merged', payloadBC),
      publish('trip.station.merged', payloadAB),
    ]);
    await poll(
      () =>
        scalar(
          bookingSql(`
            SELECT
              (SELECT canonical_station_id FROM booking_station_redirects WHERE duplicate_station_id='${ids.graphA}')::text || ':' ||
              (SELECT canonical_station_id FROM booking_station_redirects WHERE duplicate_station_id='${ids.graphB}')::text
          `),
        ) === `${ids.graphC}:${ids.graphC}`,
      'Out-of-order Booking redirect graph did not converge',
    );
    const before = count(
      bookingSql(
        `SELECT count(*) FROM booking_station_redirects WHERE source_event_id IN ('${eventAB}','${eventBC}')`,
      ),
    );
    await Promise.all([
      publish('trip.station.merged', payloadAB, randomUUID()),
      publish('trip.station.merged', payloadBC, randomUUID()),
    ]);
    await sleep(1_500);
    assert(
      count(
        bookingSql(
          `SELECT count(*) FROM booking_station_redirects WHERE source_event_id IN ('${eventAB}','${eventBC}')`,
        ),
      ) === before,
      'Station event replay duplicated Booking redirect rows',
    );
    await poll(
      () =>
        count(
          identitySql(
            `SELECT count(*) FROM activity_logs WHERE source_event_id IN ('${eventAB}','${eventBC}')`,
          ),
        ) === 2,
      'Identity out-of-order audit consumers timeout',
    );
  });

  await scenario(10, 'Internal Station resolution distinguishes merged and ordinary deleted rows', async () => {
    const merged = await tripInternalApi(`/internal/v1/stations/${ids.duplicateStation}`);
    assert(
      merged.status === 200 &&
        merged.json?.id === ids.duplicateStation &&
        merged.json?.isMerged === true &&
        merged.json?.canonicalStationId === ids.primaryStation,
      `Merged Station did not resolve to its canonical Station: ${merged.text}`,
    );
    const deleted = await tripInternalApi(`/internal/v1/stations/${ids.deletedStation}`);
    expectError(deleted, 404, 'STATION_NOT_FOUND');

    const conflict = await api(
      'POST',
      `/v1/admin/stations/${ids.conflictPrimary}/merge`,
      {
        token: state.tokens.systemAdmin,
        key: idemKey('merge-conflict'),
        body: { duplicateId: ids.conflictDuplicate },
      },
    );
    expectError(conflict, 409, 'STATION_MERGE_CONFLICT');
    assert(
      scalar(
        tripSql(
          `SELECT (deleted_at IS NULL)::int||':'||(merged_into_station_id IS NULL)::int FROM stations WHERE id='${ids.conflictDuplicate}'`,
        ),
      ) === '1:1',
      'Conflict merge partially mutated duplicate Station',
    );

    const racePrimary = id(32_901);
    const raceDuplicate = id(32_902);
    tripSql(`
      INSERT INTO stations (id,name,slug,city,province,is_active)
      VALUES
        ('${racePrimary}','Day 40 Race Primary','day40-race-primary','HCM','HCM',true),
        ('${raceDuplicate}','Day 40 Race Duplicate','day40-race-duplicate','HCM','HCM',true)
      ON CONFLICT (id) DO NOTHING;
    `);
    const outcomes = await Promise.all([
      api('POST', `/v1/admin/stations/${racePrimary}/merge`, {
        token: state.tokens.systemAdmin,
        key: idemKey('merge-race-a'),
        body: { duplicateId: raceDuplicate },
      }),
      api('POST', `/v1/admin/stations/${racePrimary}/merge`, {
        token: state.tokens.systemAdmin,
        key: idemKey('merge-race-b'),
        body: { duplicateId: raceDuplicate },
      }),
    ]);
    assert(
      outcomes.filter((outcome) => outcome.status === 200).length === 1 &&
        outcomes.filter((outcome) => outcome.status === 409).length === 1,
      `Merge race outcomes invalid: ${outcomes.map((outcome) => outcome.status).join(',')}`,
    );
    assert(
      count(
        tripSql(
          `SELECT count(*) FROM outbox_events WHERE event_type='trip.station.merged' AND payload->>'duplicateStationId'='${raceDuplicate}'`,
        ),
      ) === 1,
      'Merge race emitted duplicate Outbox events',
    );
  });

  const range = currentReportRange();
  let baselineReport;
  await scenario(11, 'Platform report aggregates boundaries, signed Parcel net, and names', async () => {
    baselineReport = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    assert(baselineReport.status === 200, baselineReport.text);
    const data = baselineReport.json?.data;
    assert(data?.period?.timezone === 'UTC', 'Report period timezone drifted');
    assert(data.totals.completedBookingCount === 2, JSON.stringify(data.totals));
    assert(data.totals.completedTripCount === 3, JSON.stringify(data.totals));
    assert(data.totals.deliveredParcelCount === 2, JSON.stringify(data.totals));
    assert(data.totals.bookingRevenueVnd === 500_000, JSON.stringify(data.totals));
    assert(data.totals.parcelRevenueVnd === 100_000, JSON.stringify(data.totals));
    assert(data.totals.netRevenueVnd === 600_000, JSON.stringify(data.totals));
    assert(data.byOperator.length === 3, JSON.stringify(data.byOperator));
    const operatorA = data.byOperator.find((item) => item.operatorId === ids.operatorA);
    const deletedOperator = data.byOperator.find(
      (item) => item.operatorId === ids.deletedOperator,
    );
    const missingOperator = data.byOperator.find(
      (item) => item.operatorId === ids.missingOperator,
    );
    assert(
      operatorA?.operatorName === 'Day 40 Operator A' && operatorA.netRevenueVnd === 440_000,
      `Operator A report drifted: ${JSON.stringify(operatorA)}`,
    );
    assert(
      deletedOperator?.operatorName === 'Day 40 Deleted Operator' &&
        deletedOperator.parcelRevenueVnd === -40_000 &&
        deletedOperator.netRevenueVnd === 160_000,
      `Deleted operator signed metrics drifted: ${JSON.stringify(deletedOperator)}`,
    );
    assert(
      missingOperator?.operatorName === null &&
        missingOperator.completedTripCount === 1 &&
        missingOperator.netRevenueVnd === 0,
      `Missing operator metrics drifted: ${JSON.stringify(missingOperator)}`,
    );
    assert(
      data.byOperator.every(
        (item, index) => index === 0 || data.byOperator[index - 1].netRevenueVnd >= item.netRevenueVnd,
      ),
      'Sort drifted',
    );
    mark('platform report');
  });

  await scenario(12, 'Source and orchestrator overflow return REPORT_VALUE_OVERFLOW', async () => {
    const sourceOperator = id(80_001);
    const sourceBookingA = id(80_011);
    const sourceBookingB = id(80_012);
    bookingSql(`
      INSERT INTO bookings
        (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,
         dropoff_station_id,base_fare,discount_amount,total_amount,status,completed_at)
      VALUES
        ('${sourceBookingA}','VR-D40-OVF-A','${ids.passenger}','${ids.completedTripA}','${sourceOperator}','${ids.primaryStation}','${ids.destinationStation}',9223372036854775807,0,9223372036854775807,'COMPLETED',now()),
        ('${sourceBookingB}','VR-D40-OVF-B','${ids.passenger}','${ids.completedTripA}','${sourceOperator}','${ids.primaryStation}','${ids.destinationStation}',9223372036854775807,0,9223372036854775807,'COMPLETED',now());
    `);
    const sourceOverflow = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    expectError(sourceOverflow, 500, 'REPORT_VALUE_OVERFLOW');
    bookingSql(`DELETE FROM bookings WHERE id IN ('${sourceBookingA}','${sourceBookingB}');`);

    const localOperator = id(80_002);
    const localBooking = id(80_021);
    const localParcel = id(80_022);
    bookingSql(`
      INSERT INTO bookings
        (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,
         dropoff_station_id,base_fare,discount_amount,total_amount,status,completed_at)
      VALUES ('${localBooking}','VR-D40-LOCAL-OVF','${ids.passenger}','${ids.completedTripA}','${localOperator}','${ids.primaryStation}','${ids.destinationStation}',9223372036854775807,0,9223372036854775807,'COMPLETED',now());
    `);
    parcelSql(`
      INSERT INTO parcels
        (id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,
         size_category,estimated_weight_kg,delivery_method,total_price_vnd,deposit_percent,
         deposit_amount,original_deposit_amount,discount_amount,additional_amount,refund_amount,
         status,confirmed_at)
      VALUES ('${localParcel}','VRP-D40-LOCAL-OVF','${ids.passenger}','Overflow Recipient','+84910040822','${localOperator}','${ids.completedTripA}','SMALL',1,'TERMINAL_PICKUP',1,100,1,1,0,0,0,'DELIVERY_CONFIRMED',now());
    `);
    const localOverflow = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    expectError(localOverflow, 500, 'REPORT_VALUE_OVERFLOW');
    bookingSql(`DELETE FROM bookings WHERE id='${localBooking}';`);
    parcelSql(`DELETE FROM parcels WHERE id='${localParcel}';`);
    const recovered = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    assert(recovered.status === 200, `Report did not recover after overflow: ${recovered.text}`);
    mark('signed/overflow report');
  });

  await scenario(13, 'Platform report rejects invalid range and non-admin roles', async () => {
    const missing = await api('GET', '/v1/admin/reports/platform', {
      token: state.tokens.systemAdmin,
    });
    expectError(missing, 422, 'VALIDATION_ERROR');
    const offset = await api(
      'GET',
      '/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00%2B07%3A00&to=2026-07-02T00%3A00%3A00Z',
      { token: state.tokens.systemAdmin },
    );
    expectError(offset, 422, 'VALIDATION_ERROR');
    const tooWide = await api(
      'GET',
      reportPath('2025-01-01T00:00:00Z', '2026-07-01T00:00:00Z'),
      { token: state.tokens.systemAdmin },
    );
    expectError(tooWide, 422, 'VALIDATION_ERROR');
    const forbidden = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.operatorAdmin,
    });
    expectError(forbidden, 403, 'FORBIDDEN');
  });

  await scenario(14, 'Live Booking and Trip completion increments report exactly once', async () => {
    assert(state.liveBookingId, 'Live booking was not created');
    const before = baselineReport.json.data.totals;
    tripSql(`
      UPDATE trips
      SET status='IN_PROGRESS',actual_departure_time=now()-interval '30 minutes',
          destination_arrived_at=now(),destination_arrived_by_user_id='${ids.driver}',updated_at=now()
      WHERE id='${ids.liveTrip}';
    `);
    const key = idemKey('complete-live-trip');
    const completed = await api('POST', `/v1/driver/trips/${ids.liveTrip}/complete`, {
      token: state.tokens.driver,
      key,
    });
    assert(completed.status === 200, `Live Trip completion failed: ${completed.text}`);
    await poll(
      () =>
        scalar(
          bookingSql(`SELECT status FROM bookings WHERE id='${state.liveBookingId}'`),
        ) === 'COMPLETED',
      'Booking completion consumer timeout',
    );
    const after = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    assert(after.status === 200, after.text);
    assert(
      after.json.data.totals.completedBookingCount === before.completedBookingCount + 1 &&
        after.json.data.totals.completedTripCount === before.completedTripCount + 1 &&
        after.json.data.totals.bookingRevenueVnd === before.bookingRevenueVnd + 100_000,
      `Live report increment drifted: ${JSON.stringify(after.json.data.totals)}`,
    );
    const replay = await api('POST', `/v1/driver/trips/${ids.liveTrip}/complete`, {
      token: state.tokens.driver,
      key,
    });
    assert(replay.status === 200 && replay.text === completed.text, 'Trip completion replay drifted');
    const afterReplay = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    assert(
      JSON.stringify(afterReplay.json.data.totals) === JSON.stringify(after.json.data.totals),
      'Completion replay double-counted report',
    );
  });

  await scenario(15, 'Parcel outage returns no partial report and restart recovers', async () => {
    if (useDev) {
      const healthy = await api('GET', reportPath(range.from, range.to), {
        token: state.tokens.systemAdmin,
      });
      assert(healthy.status === 200, healthy.text);
      return;
    }
    run('docker', ['stop', containers.parcel]);
    const unavailable = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    expectError(unavailable, 502, 'UPSTREAM_UNAVAILABLE');
    assert(unavailable.json?.data === undefined, 'Upstream failure returned partial data');
    composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'parcel']);
    await waitFor(`${urls.parcel}/health`);
    await waitFor(`${urls.parcel}/ready`);
    const recovered = await api('GET', reportPath(range.from, range.to), {
      token: state.tokens.systemAdmin,
    });
    assert(recovered.status === 200, `Report did not recover: ${recovered.text}`);
    mark('upstream failure');
  });

  await scenario(16, 'Station event transport replay does not duplicate consumer effects', async () => {
    const payloadText = scalar(
      tripSql(
        `SELECT payload::text FROM outbox_events WHERE event_type='trip.station.merged' AND payload->>'duplicateStationId'='${ids.duplicateStation}' LIMIT 1`,
      ),
    );
    const payload = JSON.parse(payloadText);
    const beforeRedirects = count(
      bookingSql(
        `SELECT count(*) FROM booking_station_redirects WHERE source_event_id='${payload.eventId}'`,
      ),
    );
    const beforeAudits = count(
      identitySql(`SELECT count(*) FROM activity_logs WHERE source_event_id='${payload.eventId}'`),
    );
    await publish('trip.station.merged', payload, randomUUID());
    await sleep(1_500);
    assert(
      count(
        bookingSql(
          `SELECT count(*) FROM booking_station_redirects WHERE source_event_id='${payload.eventId}'`,
        ),
      ) === beforeRedirects &&
        count(
          identitySql(
            `SELECT count(*) FROM activity_logs WHERE source_event_id='${payload.eventId}'`,
          ),
        ) === beforeAudits,
      'Transport replay duplicated consumer side effects',
    );
  });

  await scenario(17, 'Cycle-poison Station event rolls back Booking marker and relink', async () => {
    const cycleEventId = id(30_903);
    await publish(
      'trip.station.merged',
      stationMergedPayload(cycleEventId, ids.graphA, ids.graphC),
    );
    await sleep(2_000);
    assert(
      count(
        bookingSql(
          `SELECT count(*) FROM booking_station_redirects WHERE source_event_id='${cycleEventId}' OR duplicate_station_id='${ids.graphC}'`,
        ),
      ) === 0,
      'Cycle-poison event persisted a Booking redirect marker',
    );
  });

  await scenario(18, 'Direct PostgreSQL, Redis, and RabbitMQ assertions prove side effects', async () => {
    assert(
      count(identitySql(`SELECT count(*) FROM users WHERE id::text LIKE '40000000-%'`)) >= 9,
      'Identity deterministic seed missing',
    );
    assert(
      count(
        identitySql(
          'SELECT count(*) FROM (SELECT source_event_id FROM activity_logs WHERE source_event_id IS NOT NULL GROUP BY source_event_id HAVING count(*) > 1) duplicate_events',
        ),
      ) === 0,
      'Identity audit source_event_id duplicated',
    );
    assert(
      count(
        bookingSql(
          'SELECT count(*) FROM (SELECT source_event_id FROM booking_station_redirects GROUP BY source_event_id HAVING count(*) > 1) duplicate_events',
        ),
      ) === 0,
      'Booking redirect source_event_id duplicated',
    );
    assert(
      count(
        tripSql(
          "SELECT count(*) FROM outbox_events WHERE event_type IN ('trip.station.normalized','trip.station.merged') AND status='PUBLISHED'",
        ),
      ) >= 2,
      'Trip Outbox publish proof missing',
    );
    const redisKeys = redis('--scan', '--pattern', '*:idem:v2:response:*')
      .split(/\r?\n/)
      .filter(Boolean);
    assert(redisKeys.length >= 3, 'Redis idempotency response keys missing');
    for (const key of redisKeys) {
      assert(Number(redis('TTL', key)) > 0, `Redis key has no TTL: ${key}`);
    }
    const exchanges = run('docker', [
      'exec',
      containers.rabbitmq,
      'rabbitmqctl',
      'list_exchanges',
      '--quiet',
      'name',
      'type',
    ]);
    assert(exchanges.includes('vietride.events') && exchanges.includes('topic'), 'Rabbit exchange missing');
    assert(rabbitQueues().includes('booking.station-merged'), 'Booking Station consumer queue missing');
    mark('database assertions');
  });

  await scenario(19, 'Day 40 EF migrations rollback and reapply cleanly', async () => {
    await runMigrationGate();
  });

  await scenario(20, 'Required acceptance summary is complete', async () => {
    const required = [
      'seed',
      'admin users',
      'lock/unlock',
      'identity race invariants',
      'password reset lock race',
      'locked origin restore',
      'shared idempotency',
      'activity immutability',
      'station normalize',
      'station merge',
      'booking relink',
      'booking station race invariants',
      'audit consumers',
      'platform report',
      'signed/overflow report',
      'upstream failure',
      'database assertions',
    ];
    const missing = required.filter((gate) => !summary.has(gate));
    assert(missing.length === 0, `Summary gates missing: ${missing.join(', ')}`);
  });
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
    for (const service of ['identity', 'trip', 'booking', 'payment', 'parcel', 'gateway']) {
      try {
        console.error(
          `DIAGNOSTIC ${service}\n${run('docker', ['logs', containers[service], '--tail', '120'])}`,
        );
      } catch {
        // A container may not have reached creation; cleanup remains mandatory.
      }
    }
  }
} finally {
  if (!useDev) {
    try {
      composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
      mark('cleanup');
    } catch (error) {
      failed ??= error;
      results.push({ scenario: 'CLEANUP', name: String(error), passed: false });
    }
  } else {
    mark('cleanup');
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'day40-admin-reports-e2e',
      scenariosPassed: results.filter((result) => result.passed).length,
      results,
    },
    null,
    2,
  ),
);
process.exitCode =
  failed || results.length < 20 || results.some((result) => !result.passed) ? 1 : 0;
