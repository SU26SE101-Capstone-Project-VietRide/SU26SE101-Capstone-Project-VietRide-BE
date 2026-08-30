import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const gatewayBaseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const envFile = fs.existsSync(path.join(root, '.env')) ? '.env' : '.env.example';
const localEnv = loadEnv(path.join(root, envFile));
const compose = [
  'compose',
  '--env-file',
  envFile,
  '-f',
  'infra/docker/docker-compose.yml',
  '--profile',
  'app',
];
const postgresContainer = 'vietride_postgres';
const redisContainer = 'vietride_redis';
const ids = Object.freeze({
  operatorA: crypto.randomUUID(),
  operatorB: crypto.randomUUID(),
  operatorAdminA: crypto.randomUUID(),
  operatorAdminB: crypto.randomUUID(),
  passenger: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  activeRouteA: crypto.randomUUID(),
  inactiveRouteA: crypto.randomUUID(),
  activeRouteB: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  depositPolicy: crypto.randomUUID(),
  dimWeightConfig: crypto.randomUUID(),
});
const runTag = ids.trip.replaceAll('-', '').slice(0, 12).toUpperCase();
const tomorrow = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Ho_Chi_Minh',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
}).format(new Date(Date.now() + 24 * 60 * 60 * 1000));
const departureDateTime = `${tomorrow}T08:00:00+07:00`;
const estimatedArrivalTime = new Date(
  Date.parse(departureDateTime) + 8 * 60 * 60 * 1000,
).toISOString();
const dimWeightVersion = 2_000_000_000 + crypto.randomInt(100_000_000);
const idempotencyKeys = [];
let infrastructureStarted = false;
let seeded = false;
let assertions = 0;
let runError;
let cleanupError;

function loadEnv(file) {
  const values = {};
  for (const rawLine of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    const separator = line.indexOf('=');
    if (separator < 1) continue;
    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    values[key] = value.replaceAll('\\n', '\n');
  }
  return values;
}

function run(command, args, options = {}) {
  const value = execFileSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    stdio: options.capture === false ? 'inherit' : ['ignore', 'pipe', 'pipe'],
  });
  return typeof value === 'string' ? value.trim() : '';
}

function psql(database, sql) {
  return run('docker', [
    'exec',
    postgresContainer,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    process.env.POSTGRES_USER || 'vietride',
    '-d',
    database,
    '-qAtc',
    sql,
  ]);
}

function redis(...args) {
  return run('docker', ['exec', redisContainer, 'redis-cli', '--raw', ...args]);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
  assertions += 1;
}

function pass(label) {
  console.log(`PASS | ${label}`);
}

async function waitFor(url, timeoutMs = 300_000) {
  const deadline = Date.now() + timeoutMs;
  let lastStatus = 'unreachable';
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      lastStatus = response.status;
      if (response.ok) return;
    } catch (error) {
      lastStatus = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`Timed out waiting for ${url}; last=${lastStatus}`);
}

async function mintToken(subject, role, operatorId = null) {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const privateKey = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY ||
      localEnv.USER_JWT_PRIVATE_KEY ||
      settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  const claims = {
    role,
    email: `${role.toLowerCase()}-${runTag}@parcel-route-fare.test`,
    hasPhone: 'true',
  };
  if (operatorId) {
    claims.operatorId = operatorId;
    claims.operatorStatus = 'APPROVED';
  }
  return new SignJWT(claims)
    .setProtectedHeader({
      alg: 'RS256',
      kid: process.env.USER_JWT_KID || localEnv.USER_JWT_KID || settings.IdentityJwt.Kid,
    })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(privateKey);
}

async function gatewayRequest(method, pathname, token, traceId, body, idempotencyKey) {
  const headers = {
    Authorization: `Bearer ${token}`,
    'X-Request-Id': traceId,
  };
  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
    idempotencyKeys.push(idempotencyKey);
  }
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${gatewayBaseUrl}${pathname}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let parsed = null;
  if (text) {
    try {
      parsed = JSON.parse(text);
    } catch {
      throw new Error(`${method} ${pathname} returned non-JSON: ${text.slice(0, 300)}`);
    }
  }
  return {
    status: response.status,
    body: parsed,
    responseTraceId: response.headers.get('x-request-id'),
  };
}

function assertEnvelope(result, expectedStatus, traceId, errorCode = null) {
  assert(
    result.status === expectedStatus,
    `Expected HTTP ${expectedStatus}, got ${result.status}: ${JSON.stringify(result.body)}`,
  );
  assert(result.responseTraceId === traceId, `Response X-Request-Id mismatch for ${traceId}`);
  assert(result.body?.statusCode === expectedStatus, `Envelope statusCode mismatch for ${traceId}`);
  assert(result.body?.meta?.traceId === traceId, `Envelope traceId mismatch for ${traceId}`);
  assert(result.body?.success === (errorCode === null), `Envelope success mismatch for ${traceId}`);
  if (errorCode) {
    assert(
      result.body?.error?.code === errorCode,
      `Expected ${errorCode}, got ${result.body?.error?.code}`,
    );
  } else {
    assert(result.body?.data !== undefined, `Envelope data missing for ${traceId}`);
  }
  return result.body;
}

function buildStack() {
  run('docker', ['info']);
  for (const service of ['identity', 'trip', 'parcel', 'gateway']) {
    let lastError;
    for (let attempt = 1; attempt <= 3; attempt += 1) {
      try {
        run('docker', [...compose, 'build', service], {
          env: { COMPOSE_PARALLEL_LIMIT: '1' },
          capture: false,
        });
        lastError = undefined;
        break;
      } catch (error) {
        lastError = error;
        console.error(`Build ${service} attempt ${attempt}/3 failed.`);
      }
    }
    if (lastError) throw lastError;
  }
  run('docker', [
    ...compose,
    'up',
    '-d',
    '--no-build',
    'postgres',
    'redis',
    'rabbitmq',
    'identity',
    'trip',
    'parcel',
  ]);
  run('docker', [...compose, 'up', '-d', '--no-build', '--no-deps', 'gateway']);
  infrastructureStarted = true;
}

async function waitForStack() {
  await Promise.all([
    waitFor('http://localhost:5001/health'),
    waitFor('http://localhost:5002/health'),
    waitFor('http://localhost:5005/health'),
    waitFor(`${gatewayBaseUrl}/health`),
  ]);
  pass('real compose stack health');
}

function cleanupFixture() {
  const errors = [];
  const operations = [
    () =>
      psql(
        'vietride_parcel',
        `DELETE FROM vietride_parcel.parcel_route_fares WHERE route_id IN ('${ids.activeRouteA}','${ids.inactiveRouteA}','${ids.activeRouteB}');
         DELETE FROM vietride_parcel.operator_deposit_policies WHERE id='${ids.depositPolicy}';
         DELETE FROM vietride_parcel.system_configs WHERE id='${ids.dimWeightConfig}';`,
      ),
    () =>
      psql(
        'vietride_trip',
        `DELETE FROM vietride_trip.trips WHERE id='${ids.trip}';
         DELETE FROM vietride_trip.routes WHERE id IN ('${ids.activeRouteA}','${ids.inactiveRouteA}','${ids.activeRouteB}');
         DELETE FROM vietride_trip.vehicles WHERE id='${ids.vehicle}';
         DELETE FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}';
         DELETE FROM vietride_trip.stations WHERE id IN ('${ids.originStation}','${ids.destinationStation}');`,
      ),
    () =>
      psql(
        'vietride_identity',
        `DELETE FROM vietride_identity.users WHERE id IN ('${ids.operatorAdminA}','${ids.operatorAdminB}','${ids.passenger}');
         DELETE FROM vietride_identity.operators WHERE id IN ('${ids.operatorA}','${ids.operatorB}');`,
      ),
    () => {
      const keys = idempotencyKeys.flatMap((key) => {
        const hash = crypto.createHash('sha256').update(key).digest('hex').toUpperCase();
        return [
          `parcel:idem:${key}`,
          `parcel:idem:v2:response:${hash}`,
          `parcel:idem:v2:processing:${hash}`,
        ];
      });
      if (keys.length > 0) redis('DEL', ...keys);
    },
  ];
  for (const operation of operations) {
    try {
      operation();
    } catch (error) {
      errors.push(error);
    }
  }
  if (errors.length > 0) throw new AggregateError(errors, 'Fixture cleanup failed');
}

function assertClean() {
  const parcelCount = psql(
    'vietride_parcel',
    `SELECT
       (SELECT count(*) FROM vietride_parcel.parcel_route_fares WHERE route_id IN ('${ids.activeRouteA}','${ids.inactiveRouteA}','${ids.activeRouteB}'))
       + (SELECT count(*) FROM vietride_parcel.operator_deposit_policies WHERE id='${ids.depositPolicy}')
       + (SELECT count(*) FROM vietride_parcel.system_configs WHERE id='${ids.dimWeightConfig}');`,
  );
  const tripCount = psql(
    'vietride_trip',
    `SELECT
       (SELECT count(*) FROM vietride_trip.trips WHERE id='${ids.trip}')
       + (SELECT count(*) FROM vietride_trip.routes WHERE id IN ('${ids.activeRouteA}','${ids.inactiveRouteA}','${ids.activeRouteB}'))
       + (SELECT count(*) FROM vietride_trip.vehicles WHERE id='${ids.vehicle}')
       + (SELECT count(*) FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}')
       + (SELECT count(*) FROM vietride_trip.stations WHERE id IN ('${ids.originStation}','${ids.destinationStation}'));`,
  );
  const identityCount = psql(
    'vietride_identity',
    `SELECT
       (SELECT count(*) FROM vietride_identity.users WHERE id IN ('${ids.operatorAdminA}','${ids.operatorAdminB}','${ids.passenger}'))
       + (SELECT count(*) FROM vietride_identity.operators WHERE id IN ('${ids.operatorA}','${ids.operatorB}'));`,
  );
  const taggedCount = [
    psql(
      'vietride_identity',
      `SELECT count(*) FROM vietride_identity.users WHERE email LIKE '%${runTag}%';`,
    ),
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.routes WHERE name LIKE '%${runTag}%';`,
    ),
  ];
  assert(
    parcelCount === '0' && tripCount === '0' && identityCount === '0',
    `Fixture rows remain parcel=${parcelCount}, trip=${tripCount}, identity=${identityCount}`,
  );
  assert(
    taggedCount.every((count) => count === '0'),
    `Run-tag rows remain: ${taggedCount}`,
  );
  for (const key of idempotencyKeys) {
    const hash = crypto.createHash('sha256').update(key).digest('hex').toUpperCase();
    for (const redisKey of [
      `parcel:idem:${key}`,
      `parcel:idem:v2:response:${hash}`,
      `parcel:idem:v2:processing:${hash}`,
    ]) {
      assert(redis('EXISTS', redisKey) === '0', `Redis key remains: ${redisKey}`);
    }
  }
}

function seedFixture() {
  cleanupFixture();
  seeded = true;
  psql(
    'vietride_identity',
    `INSERT INTO vietride_identity.operators
       (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
     VALUES
       ('${ids.operatorA}','Parcel Fare Operator A ${runTag}','PFA-${runTag}','PFA-TAX-${runTag}','operator-a-${runTag}@parcel-route-fare.test','+8491${runTag.slice(0, 8).replace(/[A-F]/g, '1')}','APPROVED',now(),true),
       ('${ids.operatorB}','Parcel Fare Operator B ${runTag}','PFB-${runTag}','PFB-TAX-${runTag}','operator-b-${runTag}@parcel-route-fare.test','+8492${runTag.slice(0, 8).replace(/[A-F]/g, '2')}','APPROVED',now(),true);
     INSERT INTO vietride_identity.users (id,email,phone,display_name,role,status,operator_id)
     VALUES
       ('${ids.operatorAdminA}','admin-a-${runTag}@parcel-route-fare.test',NULL,'Operator Admin A ${runTag}','OPERATOR_ADMIN','ACTIVE','${ids.operatorA}'),
       ('${ids.operatorAdminB}','admin-b-${runTag}@parcel-route-fare.test',NULL,'Operator Admin B ${runTag}','OPERATOR_ADMIN','ACTIVE','${ids.operatorB}'),
       ('${ids.passenger}','passenger-${runTag}@parcel-route-fare.test',NULL,'Passenger ${runTag}','PASSENGER','ACTIVE',NULL);`,
  );
  psql(
    'vietride_trip',
    `INSERT INTO vietride_trip.stations (id,name,slug,city,is_active)
     VALUES
       ('${ids.originStation}','Bến đi ${runTag}','parcel-fare-origin-${runTag.toLowerCase()}','Ho Chi Minh',true),
       ('${ids.destinationStation}','Bến đến ${runTag}','parcel-fare-destination-${runTag.toLowerCase()}','Da Nang',true);
     INSERT INTO vietride_trip.vehicle_types
       (id,code,display_name,default_seat_count,is_system_defined,is_active)
     VALUES ('${ids.vehicleType}','PFA_${runTag}','Parcel Fare Vehicle ${runTag}',4,false,true);
     INSERT INTO vietride_trip.vehicles
       (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
     VALUES
       ('${ids.vehicle}','${ids.operatorA}','${ids.vehicleType}','PF${runTag.slice(0, 8)}','{"version":1,"totalSeats":4,"rows":2,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1}]}',4,100,10,'ACTIVE',true);
     INSERT INTO vietride_trip.routes
       (id,operator_id,name,origin_station_id,destination_station_id,base_fare,estimated_duration_minutes,is_active)
     VALUES
       ('${ids.activeRouteA}','${ids.operatorA}','Active Route A ${runTag}','${ids.originStation}','${ids.destinationStation}',150000,480,true),
       ('${ids.inactiveRouteA}','${ids.operatorA}','Inactive Route A ${runTag}','${ids.originStation}','${ids.destinationStation}',150000,480,false),
       ('${ids.activeRouteB}','${ids.operatorB}','Active Route B ${runTag}','${ids.originStation}','${ids.destinationStation}',150000,480,true);
     INSERT INTO vietride_trip.trips
       (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare,max_cargo_weight_kg,max_cargo_volume_m3,estimated_passenger_luggage_kg,reserved_parcel_weight_kg,reserved_parcel_volume_m3,total_loaded_weight_kg,total_loaded_volume_m3,seat_layout_snapshot_json)
     VALUES
       ('${ids.trip}','${ids.operatorA}','${ids.activeRouteA}','${ids.vehicle}','${ids.operatorAdminA}','${departureDateTime}','${estimatedArrivalTime}','SCHEDULED','MANUAL',150000,100,10,0,0,0,0,0,'{"version":1,"totalSeats":4,"rows":2,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1}]}');`,
  );
  psql(
    'vietride_parcel',
    `DELETE FROM vietride_parcel.parcel_route_fares
       WHERE route_id='${ids.activeRouteA}' AND size_category='MEDIUM';
     INSERT INTO vietride_parcel.operator_deposit_policies
       (id,operator_id,route_id,deposit_percent,effective_from,is_active)
     VALUES ('${ids.depositPolicy}','${ids.operatorA}','${ids.activeRouteA}',20,now()-interval '1 hour',true);
     INSERT INTO vietride_parcel.system_configs
       (id,key,decimal_value,version,is_active,effective_from)
     VALUES ('${ids.dimWeightConfig}','DIM_WEIGHT_FACTOR',6000,${dimWeightVersion},true,now()-interval '1 hour');`,
  );
  pass('isolated Identity, Trip and Parcel fixture seeded');
}

async function runJourney() {
  const [operatorToken, passengerToken] = await Promise.all([
    mintToken(ids.operatorAdminA, 'OPERATOR_ADMIN', ids.operatorA),
    mintToken(ids.passenger, 'PASSENGER'),
  ]);

  const routesTrace = `parcel-fare-${runTag}-routes`;
  const routesResult = await gatewayRequest(
    'GET',
    '/v1/operator/routes?page=1&pageSize=100',
    operatorToken,
    routesTrace,
  );
  const routesEnvelope = assertEnvelope(routesResult, 200, routesTrace);
  const activeRoute = routesEnvelope.data.items.find((route) => route.id === ids.activeRouteA);
  assert(activeRoute?.isActive === true, 'GET operator routes did not return active route A');
  assert(
    routesEnvelope.data.items.some(
      (route) => route.id === ids.inactiveRouteA && route.isActive === false,
    ),
    'GET operator routes must preserve inactive route visibility',
  );
  pass('operator route ID read through Gateway');

  const effectiveFrom = new Date(Date.now() - 60 * 60 * 1000).toISOString();
  const effectiveUntil = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
  const fareTrace = `parcel-fare-${runTag}-create`;
  const fareKey = crypto.randomUUID();
  const fareResult = await gatewayRequest(
    'POST',
    '/v1/operator/parcel-route-fares',
    operatorToken,
    fareTrace,
    {
      routeId: activeRoute.id,
      sizeCategory: 'SMALL',
      priceVnd: 150000,
      effectiveFrom,
      effectiveUntil,
    },
    fareKey,
  );
  const fareEnvelope = assertEnvelope(fareResult, 201, fareTrace);
  assert(fareEnvelope.data.routeId === activeRoute.id, 'Created fare routeId mismatch');
  assert(fareEnvelope.data.operatorId === ids.operatorA, 'Created fare operatorId mismatch');
  assert(fareEnvelope.data.priceVnd === 150000, 'Created fare price mismatch');
  pass('route fare created with Route API ID through real Trip ownership lookup');

  const availableTrace = `parcel-fare-${runTag}-available`;
  const params = new URLSearchParams({
    originStationId: ids.originStation,
    destinationStationId: ids.destinationStation,
    departureDate: tomorrow,
    lengthCm: '10',
    widthCm: '10',
    heightCm: '10',
    estimatedWeightKg: '1',
    sizeCategory: 'MEDIUM',
    page: '1',
    pageSize: '20',
  });
  const availableResult = await gatewayRequest(
    'GET',
    `/v1/parcels/available-trips?${params}`,
    passengerToken,
    availableTrace,
  );
  const availableEnvelope = assertEnvelope(availableResult, 200, availableTrace);
  const trip = availableEnvelope.data.items.find((item) => item.tripId === ids.trip);
  assert(trip, 'Expected seeded trip in available-trips');
  assert(trip.routeId === ids.activeRouteA, 'Available trip routeId mismatch');
  assert(trip.status === 'SCHEDULED', 'Available trip status mismatch');
  assert(trip.operatorId === ids.operatorA, 'Available trip operatorId mismatch');
  assert(trip.operatorName === `Parcel Fare Operator A ${runTag}`, 'Operator name mismatch');
  assert(
    trip.originStation?.id === ids.originStation && trip.originStation?.name === `Bến đi ${runTag}`,
    'Origin station projection mismatch',
  );
  assert(
    trip.destinationStation?.id === ids.destinationStation &&
      trip.destinationStation?.name === `Bến đến ${runTag}`,
    'Destination station projection mismatch',
  );
  assert(
    Date.parse(trip.departureDateTime) === Date.parse(departureDateTime),
    'Departure mismatch',
  );
  assert(
    Date.parse(trip.estimatedArrivalTime) === Date.parse(estimatedArrivalTime),
    'Estimated arrival mismatch',
  );
  assert(trip.estimatedPriceVnd === 150000, 'estimatedPriceVnd mismatch');
  assert(trip.depositPercent === 20, 'depositPercent mismatch');
  assert(trip.estimatedDepositVnd === 30000, 'estimatedDepositVnd mismatch');
  for (const hiddenField of ['availableCargoWeightKg', 'availableCargoVolumeM3', 'priceVnd']) {
    assert(!Object.hasOwn(trip, hiddenField), `Public response leaked ${hiddenField}`);
  }
  pass('available trip enriched public projection and pricing');

  const negativeCases = [
    ['cross-operator', ids.activeRouteB],
    ['inactive', ids.inactiveRouteA],
    ['missing', crypto.randomUUID()],
  ];
  for (const [label, routeId] of negativeCases) {
    const traceId = `parcel-fare-${runTag}-${label}`;
    const result = await gatewayRequest(
      'POST',
      '/v1/operator/parcel-route-fares',
      operatorToken,
      traceId,
      {
        routeId,
        sizeCategory: 'SMALL',
        priceVnd: 150000,
        effectiveFrom,
        effectiveUntil,
      },
      crypto.randomUUID(),
    );
    assertEnvelope(result, 404, traceId, 'ROUTE_NOT_FOUND');
    pass(`${label} route rejected by real Trip ownership`);
  }
}

async function main() {
  buildStack();
  await waitForStack();
  seedFixture();
  await runJourney();
}

try {
  await main();
} catch (error) {
  runError = error;
  console.error(error);
} finally {
  if (infrastructureStarted && seeded) {
    try {
      cleanupFixture();
      assertClean();
      pass('fixture and exact Redis idempotency keys cleaned');
    } catch (error) {
      cleanupError = error;
      console.error(error);
    }
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'parcel-route-fare-availability-e2e',
      runTag,
      assertions,
      passed: !runError && !cleanupError,
    },
    null,
    2,
  ),
);
process.exitCode = runError || cleanupError ? 1 : 0;
