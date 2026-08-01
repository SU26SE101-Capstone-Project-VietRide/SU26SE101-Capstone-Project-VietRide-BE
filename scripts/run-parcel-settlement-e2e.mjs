import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { importPKCS8, SignJWT } from 'jose';
import { resolveParcelSettlementE2ePorts } from './parcel-settlement-e2e-ports.mjs';

const root = process.cwd();
const envFile = fs.existsSync(path.join(root, '.env')) ? '.env' : '.env.example';
const compose = [
  'compose',
  '--env-file',
  envFile,
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day37-e2e.yml',
  '--profile',
  'app',
];
const e2ePorts = await resolveParcelSettlementE2ePorts();
const gateway = process.env.GATEWAY_BASE_URL || e2ePorts.urls.gateway;
const postgresContainer = 'day37-e2e-postgres';
const postgresUser = process.env.POSTGRES_USER || 'vietride';
const reuseImages = process.argv.includes('--reuse-images') || process.env.E2E_REUSE_IMAGES === '1';
const e2eEnv = e2ePorts.env;
const ids = Object.freeze({
  operator: crypto.randomUUID(),
  subscriptionPlan: crypto.randomUUID(),
  subscription: crypto.randomUUID(),
  operatorAdmin: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  passenger: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  fullVehicle: crypto.randomUUID(),
  route: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  fullTrip: crypto.randomUUID(),
  depositPolicy: crypto.randomUUID(),
  dimWeightConfig: crypto.randomUUID(),
  platformWallet: crypto.randomUUID(),
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
const fullTripDepartureTime = new Date(Date.parse(departureDateTime) + 5 * 60 * 1000).toISOString();
const fullTripArrivalTime = new Date(
  Date.parse(estimatedArrivalTime) + 5 * 60 * 1000,
).toISOString();
let assertions = 0;

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...e2eEnv, ...options.env },
    stdio: options.stdio || ['ignore', 'pipe', 'pipe'],
    maxBuffer: 32 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new Error(
      `${command} ${args.join(' ')} failed: ${result.stderr || result.stdout || 'unknown error'}`,
    );
  }
  return result.stdout?.trim() || '';
}

function sql(database, schema, statement) {
  return run('docker', [
    'exec',
    postgresContainer,
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
const paymentSql = (statement) => sql('vietride_payment', 'vietride_payment', statement);

function assert(condition, message) {
  if (!condition) throw new Error(message);
  assertions += 1;
}

function pass(label) {
  console.log(`PASS | ${label}`);
}

function scalar(value) {
  return String(value).split(/\r?\n/u).filter(Boolean).at(-1)?.trim() || '';
}

async function poll(action, message, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await action();
      if (last) return last;
    } catch (error) {
      last = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${message}; last=${last instanceof Error ? last.message : String(last)}`);
}

async function mintToken(subject, role, operatorId = null) {
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
  const claims = {
    role,
    email: `${role.toLowerCase()}-${runTag}@parcel-settlement.test`,
    hasPhone: 'true',
  };
  if (operatorId) {
    claims.operatorId = operatorId;
    claims.operator_id = operatorId;
  }
  return new SignJWT(claims)
    .setProtectedHeader({
      alg: 'RS256',
      kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid,
    })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(privateKey);
}

async function api(method, pathname, { token, body, key, traceId } = {}) {
  const requestTraceId = traceId || `parcel-settlement-${crypto.randomUUID()}`;
  const request = {
    method,
    headers: {
      'X-Request-Id': requestTraceId,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  };
  let response;
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    try {
      response = await fetch(`${gateway}${pathname}`, request);
      break;
    } catch (error) {
      if (attempt === 3) throw error;
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
  }
  const text = await response.text();
  let json = null;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      throw new Error(`${method} ${pathname} returned non-JSON: ${text.slice(0, 300)}`);
    }
  }
  return {
    status: response.status,
    json,
    traceId: requestTraceId,
    responseTraceId: response.headers.get('x-request-id'),
  };
}

function assertEnvelope(response, expectedStatus, errorCode = null) {
  assert(
    response.status === expectedStatus,
    `Expected HTTP ${expectedStatus}, got ${response.status}: ${JSON.stringify(response.json)}`,
  );
  assert(response.json?.statusCode === expectedStatus, 'Envelope statusCode mismatch');
  assert(response.json?.meta?.traceId === response.traceId, 'Envelope traceId mismatch');
  assert(response.responseTraceId === response.traceId, 'Response X-Request-Id mismatch');
  if (errorCode) {
    assert(response.json?.success === false, 'Error envelope success must be false');
    assert(
      response.json?.error?.code === errorCode,
      `Expected ${errorCode}, got ${response.json?.error?.code}`,
    );
  } else {
    assert(response.json?.success === true, 'Success envelope success must be true');
    assert(response.json?.data !== undefined, 'Success envelope data missing');
  }
  return response.json?.data;
}

async function waitFor(url, timeoutMs = 300_000) {
  const deadline = Date.now() + timeoutMs;
  let last = 'unreachable';
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      last = response.status;
      if (response.ok) return;
    } catch (error) {
      last = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`Timed out waiting for ${url}; last=${last}`);
}

async function buildAndStartStack() {
  run('docker', ['info']);
  run('docker', [...compose, 'down', '-v', '--remove-orphans']);
  if (!reuseImages) {
    for (const service of ['identity', 'trip', 'payment', 'parcel', 'gateway']) {
      run('docker', [...compose, 'build', service], { stdio: 'inherit' });
    }
  }
  run('docker', [...compose, 'up', '-d', '--no-build', 'postgres', 'redis', 'rabbitmq'], {
    stdio: 'inherit',
  });
  await poll(
    () => {
      try {
        run('docker', ['exec', 'day37-e2e-rabbitmq', 'rabbitmq-diagnostics', '-q', 'ping']);
        return true;
      } catch {
        return false;
      }
    },
    'RabbitMQ did not become ready',
    180_000,
  );
  run(
    'docker',
    [...compose, 'up', '-d', '--no-build', '--wait', 'identity', 'trip', 'payment', 'parcel'],
    { stdio: 'inherit' },
  );
  run('docker', [...compose, 'up', '-d', '--no-build', '--no-deps', '--wait', 'gateway'], {
    stdio: 'inherit',
  });
}

async function waitForStack() {
  await Promise.all([
    waitFor(`${e2ePorts.urls.identity}/health`),
    waitFor(`${e2ePorts.urls.trip}/health`),
    waitFor(`${e2ePorts.urls.payment}/health`),
    waitFor(`${e2ePorts.urls.parcel}/health`),
    waitFor(`${gateway}/health`),
  ]);
  pass('isolated Gateway, Identity, Trip, Payment, Parcel and RabbitMQ health');
}

async function waitForRabbitMq() {
  await poll(
    () => {
      try {
        run('docker', ['exec', 'day37-e2e-rabbitmq', 'rabbitmq-diagnostics', '-q', 'ping']);
        return true;
      } catch {
        return false;
      }
    },
    'RabbitMQ did not become ready',
    180_000,
  );
}

async function triggerSettlementTimeoutJob() {
  const jobKey = 'recurring-job:parcel.settlement-timeout';
  const previousLastJobId = scalar(
    parcelSql(`
      SELECT coalesce(max(value) FILTER (WHERE field='LastJobId'),'')
      FROM hangfire.hash
      WHERE key='${jobKey}';
    `),
  );
  const jobId = await poll(
    () => {
      const currentLastJobId = scalar(
        parcelSql(`
        SELECT coalesce(max(value) FILTER (WHERE field='LastJobId'),'')
        FROM hangfire.hash
        WHERE key='${jobKey}';
      `),
      );
      return currentLastJobId && currentLastJobId !== previousLastJobId ? currentLastJobId : false;
    },
    'Hangfire did not schedule parcel.settlement-timeout',
    360_000,
  );
  const terminalState = await poll(
    () => {
      const state = scalar(
        parcelSql(`
        SELECT statename
        FROM hangfire.job
        WHERE id=${jobId};
      `),
      );
      return state === 'Succeeded' || state === 'Failed' || state === 'Deleted' ? state : false;
    },
    `Hangfire parcel.settlement-timeout job ${jobId} did not finish`,
    90_000,
  );
  assert(
    terminalState === 'Succeeded',
    `Hangfire parcel.settlement-timeout job ${jobId} ended as ${terminalState}`,
  );
}

function seedFixtures() {
  identitySql(`
    INSERT INTO subscription_plans
      (id,name,description,price_per_month,price_per_year,max_vehicles,max_drivers,
       max_assistants,max_operator_users,max_routes,max_trips_per_month,
       enable_parcel,enable_shuttle,enable_rag,is_active)
    VALUES
      ('${ids.subscriptionPlan}','Parcel E2E ${runTag}','Isolated Parcel settlement E2E',0,0,
       10,10,10,10,10,100,true,false,false,true);
    INSERT INTO operators
      (id,name,business_registration_number,tax_code,contact_email,contact_phone,
       registration_status,approved_at,is_active)
    VALUES
      ('${ids.operator}','Parcel E2E Operator ${runTag}','PSE-${runTag}','PSE-TAX-${runTag}',
       'operator-${runTag}@parcel-settlement.test','+84910000001','APPROVED',now(),true);
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.operatorAdmin}','admin-${runTag}@parcel-settlement.test','+84910000002','Operator Admin ${runTag}','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
      ('${ids.driver}','driver-${runTag}@parcel-settlement.test','+84910000003','Driver ${runTag}','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.assistant}','assistant-${runTag}@parcel-settlement.test','+84910000004','Assistant ${runTag}','ASSISTANT','ACTIVE','${ids.operator}'),
      ('${ids.passenger}','passenger-${runTag}@parcel-settlement.test','+84910000005','Passenger ${runTag}','PASSENGER','ACTIVE',NULL);
    INSERT INTO operator_subscriptions
      (id,operator_id,active_plan_id,status,started_at,expires_at,current_vehicles,
       current_routes,current_trips_this_month,last_reset_at)
    VALUES
      ('${ids.subscription}','${ids.operator}','${ids.subscriptionPlan}','ACTIVE',
       now()-interval '1 day',now()+interval '30 days',0,0,0,now());
  `);

  tripSql(`
    INSERT INTO stations (id,name,slug,city,province,is_active)
    VALUES
      ('${ids.originStation}','Bến đi ${runTag}','parcel-settlement-origin-${runTag.toLowerCase()}','Hồ Chí Minh','Hồ Chí Minh',true),
      ('${ids.destinationStation}','Bến đến ${runTag}','parcel-settlement-destination-${runTag.toLowerCase()}','Đà Nẵng','Đà Nẵng',true);
    INSERT INTO vehicle_types
      (id,code,display_name,default_seat_count,is_system_defined,is_active)
    VALUES
      ('${ids.vehicleType}','PSE_${runTag}','Parcel Settlement Vehicle ${runTag}',20,false,true);
    INSERT INTO vehicles
      (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,
       max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
    VALUES
      ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','PS${runTag.slice(0, 8)}',
       '{"version":1,"totalSeats":20,"rows":5,"cols":4,"decks":1,"aisles":[],"seats":[]}',
       20,100,10,'ACTIVE',true),
      ('${ids.fullVehicle}','${ids.operator}','${ids.vehicleType}','PF${runTag.slice(0, 8)}',
       '{"version":1,"totalSeats":20,"rows":5,"cols":4,"decks":1,"aisles":[],"seats":[]}',
       20,3,1,'ACTIVE',true);
    INSERT INTO routes
      (id,operator_id,name,origin_station_id,destination_station_id,base_fare,
       estimated_duration_minutes,is_active)
    VALUES
      ('${ids.route}','${ids.operator}','Parcel Settlement Route ${runTag}',
       '${ids.originStation}','${ids.destinationStation}',150000,480,true);
    INSERT INTO trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,
       departure_date_time,estimated_arrival_time,status,source,base_fare,
       max_cargo_weight_kg,max_cargo_volume_m3,estimated_passenger_luggage_kg,
       reserved_parcel_weight_kg,reserved_parcel_volume_m3,
       total_loaded_weight_kg,total_loaded_volume_m3)
    VALUES
      ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}','${ids.assistant}',
       '${departureDateTime}','${estimatedArrivalTime}','SCHEDULED','MANUAL',150000,
       100,10,0,0,0,0,0),
      ('${ids.fullTrip}','${ids.operator}','${ids.route}','${ids.fullVehicle}','${ids.driver}','${ids.assistant}',
       '${fullTripDepartureTime}','${fullTripArrivalTime}','SCHEDULED','MANUAL',150000,
       3,1,0,3,0.5,0,0);
  `);

  parcelSql(`
    INSERT INTO parcel_route_fares
      (route_id,size_category,operator_id,price_vnd,price_per_chargeable_kg_vnd,
       minimum_price_vnd,effective_from)
    VALUES
      ('${ids.route}','SMALL','${ids.operator}',1000,1000,0,now()-interval '1 hour'),
      ('${ids.route}','MEDIUM','${ids.operator}',1000,1000,0,now()-interval '1 hour');
    INSERT INTO operator_deposit_policies
      (id,operator_id,route_id,deposit_percent,effective_from,is_active)
    VALUES
      ('${ids.depositPolicy}','${ids.operator}','${ids.route}',20,now()-interval '1 hour',true);
    INSERT INTO system_configs
      (id,key,decimal_value,version,is_active,effective_from)
    VALUES
      ('${ids.dimWeightConfig}','DIM_WEIGHT_FACTOR',6000,2000000001,true,now()-interval '1 hour');
  `);

  paymentSql(`
    INSERT INTO wallets (user_id,balance,currency,row_version)
    VALUES ('${ids.passenger}',100000,'VND',0);
    INSERT INTO platform_wallets (id,balance,currency,row_version)
    SELECT '${ids.platformWallet}',0,'VND',0
    WHERE NOT EXISTS (SELECT 1 FROM platform_wallets);
    UPDATE platform_wallets SET balance=0,row_version=0,updated_at=now();
  `);
  pass('isolated Identity, Trip, Parcel pricing and Payment wallet fixtures seeded');
}

function searchPath({ weightKg = 3.2, sizeCategory } = {}) {
  const params = new URLSearchParams({
    originStationId: ids.originStation,
    destinationStationId: ids.destinationStation,
    departureDate: tomorrow,
    lengthCm: '20',
    widthCm: '20',
    heightCm: '20',
    estimatedWeightKg: String(weightKg),
    page: '1',
    pageSize: '20',
  });
  if (sizeCategory !== undefined) params.set('sizeCategory', sizeCategory);
  return `/v1/parcels/available-trips?${params}`;
}

async function getParcel(parcelId, token) {
  const response = await api('GET', `/v1/parcels/${parcelId}`, { token });
  return assertEnvelope(response, 200);
}

async function waitForParcel(parcelId, token, predicate, message, timeoutMs = 120_000) {
  return poll(
    async () => {
      const parcel = await getParcel(parcelId, token);
      return predicate(parcel) ? parcel : false;
    },
    message,
    timeoutMs,
  );
}

async function createParcel(token, tripId, estimatedWeightKg, suffix) {
  const response = await api('POST', '/v1/parcels', {
    token,
    key: crypto.randomUUID(),
    body: {
      tripId,
      dropoffStopId: null,
      bookingId: null,
      itemName: `Kiện hàng ${suffix}`,
      description: 'Parcel settlement full-stack E2E',
      sizeCategory: 'MEDIUM',
      lengthCm: 20,
      widthCm: 20,
      heightCm: 20,
      estimatedWeightKg,
      photoUrl: null,
      recipient: {
        fullName: `Người nhận ${suffix}`,
        phoneNumber: '0912345678',
        email: `recipient-${suffix.toLowerCase()}@parcel-settlement.test`,
      },
      deliveryMethod: 'TERMINAL_PICKUP',
      paymentMethod: 'WALLET',
      voucherCode: null,
    },
  });
  return assertEnvelope(response, 201);
}

async function startDeposit(parcel, passengerToken) {
  const response = await api('POST', `/v1/parcels/${parcel.parcelId}/deposit-payment`, {
    token: passengerToken,
    key: crypto.randomUUID(),
    body: { paymentMethod: 'WALLET' },
  });
  const data = assertEnvelope(response, 200);
  assert(data.depositPaymentId, 'Deposit payment ID missing');
  return waitForParcel(
    parcel.parcelId,
    passengerToken,
    (detail) => detail.status === 'RESERVED' && detail.depositPaidVnd > 0,
    `Deposit callback did not reserve parcel ${parcel.parcelId}`,
  );
}

async function checkIn(parcel, passengerToken, assistantToken) {
  const response = await api('POST', `/v1/assistant/parcels/${parcel.parcelId}/check-in`, {
    token: assistantToken,
    key: crypto.randomUUID(),
    body: { tripId: ids.trip, parcelCode: parcel.parcelCode },
  });
  const data = assertEnvelope(response, 200);
  assert(data.status === 'CHECKED_IN', `Expected CHECKED_IN, got ${data.status}`);
  return getParcel(parcel.parcelId, passengerToken);
}

async function reweigh(parcel, actualWeightKg, assistantToken) {
  const response = await api('POST', `/v1/assistant/parcels/${parcel.parcelId}/reweigh`, {
    token: assistantToken,
    key: crypto.randomUUID(),
    body: {
      actualLengthCm: 20,
      actualWidthCm: 20,
      actualHeightCm: 20,
      actualWeightKg,
    },
  });
  return assertEnvelope(response, 200);
}

async function load(parcel, assistantToken) {
  const response = await api('POST', `/v1/assistant/parcels/${parcel.parcelId}/load`, {
    token: assistantToken,
    key: crypto.randomUUID(),
    body: { tripId: ids.trip, parcelCode: parcel.parcelCode },
  });
  const data = assertEnvelope(response, 200);
  assert(data.status === 'LOADED', `Expected LOADED, got ${data.status}`);
}

async function runJourney() {
  const [passengerToken, assistantToken] = await Promise.all([
    mintToken(ids.passenger, 'PASSENGER'),
    mintToken(ids.assistant, 'ASSISTANT', ids.operator),
  ]);

  const unauthorized = await api('GET', searchPath());
  assertEnvelope(unauthorized, 401, 'AUTH_TOKEN_INVALID');

  const invalid = await api('GET', searchPath().replace('lengthCm=20', 'lengthCm=0'), {
    token: passengerToken,
  });
  assertEnvelope(invalid, 422, 'VALIDATION_ERROR');

  const searchWithoutHint = await api('GET', searchPath(), { token: passengerToken });
  const searchData = assertEnvelope(searchWithoutHint, 200);
  const availableTrip = searchData.items.find((trip) => trip.tripId === ids.trip);
  assert(availableTrip, 'Expected available trip was not returned');
  assert(
    !searchData.items.some((trip) => trip.tripId === ids.fullTrip),
    'Full-capacity trip leaked into available results',
  );
  assert(availableTrip.estimatedPriceVnd === 3200, '3.2 kg must price as 3,200 VND');
  assert(availableTrip.depositPercent === 20, 'Deposit percent must be 20');
  assert(availableTrip.estimatedDepositVnd === 640, 'Estimated deposit must be 640 VND');

  const mismatchedHintSearch = await api('GET', searchPath({ sizeCategory: 'MEDIUM' }), {
    token: passengerToken,
  });
  const mismatchedData = assertEnvelope(mismatchedHintSearch, 200);
  const mismatchedTrip = mismatchedData.items.find((trip) => trip.tripId === ids.trip);
  assert(mismatchedTrip, 'Legacy mismatched size hint hid the valid trip');
  assert(
    mismatchedTrip.estimatedPriceVnd === availableTrip.estimatedPriceVnd,
    'Legacy size hint changed derived pricing',
  );
  pass('available-trip search derives SMALL, ignores MEDIUM hint and excludes full trip');

  const balanceParcel = await createParcel(passengerToken, availableTrip.tripId, 3.2, 'BALANCE');
  assert(balanceParcel.estimatedSizeCategory === 'SMALL', 'Create did not derive SMALL');
  assert(balanceParcel.estimatedGrossPriceVnd === 3200, 'Create/search gross price mismatch');
  assert(balanceParcel.depositRequiredVnd === 640, 'Create/search deposit mismatch');
  await startDeposit(balanceParcel, passengerToken);
  await checkIn(balanceParcel, passengerToken, assistantToken);
  const balanceReweigh = await reweigh(balanceParcel, 4.5, assistantToken);
  assert(balanceReweigh.status === 'PENDING_FINAL_PAYMENT', 'Positive balance not requested');
  assert(balanceReweigh.finalGrossPriceVnd === 4500, '4.5 kg must price as 4,500 VND');
  assert(balanceReweigh.balanceRequiredVnd === 3860, 'Balance must equal 4,500 - 640');

  const prematureLoad = await api('POST', `/v1/assistant/parcels/${balanceParcel.parcelId}/load`, {
    token: assistantToken,
    key: crypto.randomUUID(),
    body: { tripId: ids.trip, parcelCode: balanceParcel.parcelCode },
  });
  assertEnvelope(prematureLoad, 409, 'INVALID_STATUS');

  const finalPayment = await api('POST', `/v1/parcels/${balanceParcel.parcelId}/final-payment`, {
    token: passengerToken,
    key: crypto.randomUUID(),
    body: { paymentMethod: 'WALLET' },
  });
  const finalPaymentData = assertEnvelope(finalPayment, 200);
  assert(finalPaymentData.balancePaymentId, 'Final payment ID missing');
  const readyBalance = await waitForParcel(
    balanceParcel.parcelId,
    passengerToken,
    (detail) => detail.status === 'READY_TO_LOAD' && detail.balancePaidVnd === 3860,
    'Final-payment callback did not move parcel to READY_TO_LOAD',
  );
  assert(readyBalance.finalTotalPriceVnd === 4500, 'Final total mismatch after payment');
  await load(balanceParcel, assistantToken);
  pass('search result -> create -> deposit -> check-in -> final payment -> load');

  const refundParcel = await createParcel(passengerToken, availableTrip.tripId, 10, 'REFUND');
  assert(refundParcel.estimatedSizeCategory === 'MEDIUM', 'Refund fixture must derive MEDIUM');
  assert(refundParcel.depositRequiredVnd === 2000, 'Refund fixture deposit must be 2,000');
  await startDeposit(refundParcel, passengerToken);
  await checkIn(refundParcel, passengerToken, assistantToken);
  const refundReweigh = await reweigh(refundParcel, 0.2, assistantToken);
  assert(refundReweigh.status === 'READY_TO_LOAD', 'Refund must not block READY_TO_LOAD');
  assert(refundReweigh.finalTotalPriceVnd === 1330, 'DIM weight must drive final price');
  assert(refundReweigh.refundDueVnd === 670, 'Refund due must equal 2,000 - 1,330');
  await waitForParcel(
    refundParcel.parcelId,
    passengerToken,
    (detail) => detail.status === 'READY_TO_LOAD' && detail.refundedAmountVnd === 670,
    'RabbitMQ refund did not update Parcel refundedAmountVnd',
  );
  await load(refundParcel, assistantToken);
  pass('lower actual weight triggers idempotent refund without blocking loading');

  const checkInTimeoutParcel = await createParcel(
    passengerToken,
    availableTrip.tripId,
    1,
    'CHECK-IN-TIMEOUT',
  );
  const reservedTimeoutParcel = await startDeposit(checkInTimeoutParcel, passengerToken);
  parcelSql(`
    UPDATE parcels
    SET latest_check_in_at=now()-interval '1 second'
    WHERE id='${checkInTimeoutParcel.parcelId}';
  `);

  const callbackRaceParcel = await createParcel(
    passengerToken,
    availableTrip.tripId,
    2,
    'CALLBACK-RACE',
  );
  await startDeposit(callbackRaceParcel, passengerToken);
  await checkIn(callbackRaceParcel, passengerToken, assistantToken);
  const callbackRaceReweigh = await reweigh(callbackRaceParcel, 3, assistantToken);
  assert(
    callbackRaceReweigh.status === 'PENDING_FINAL_PAYMENT',
    'Callback-race fixture did not require final payment',
  );

  run('docker', ['stop', 'day37-e2e-rabbitmq']);
  const raceFinalPayment = await api(
    'POST',
    `/v1/parcels/${callbackRaceParcel.parcelId}/final-payment`,
    {
      token: passengerToken,
      key: crypto.randomUUID(),
      body: { paymentMethod: 'WALLET' },
    },
  );
  const raceFinalPaymentData = assertEnvelope(raceFinalPayment, 200);
  assert(raceFinalPaymentData.balancePaymentId, 'Callback-race Payment ID missing');
  const racePaidAt = scalar(
    paymentSql(`
      SELECT succeeded_at::text FROM payments
      WHERE id='${raceFinalPaymentData.balancePaymentId}';
    `),
  );
  assert(racePaidAt, 'Callback-race authoritative paidAt missing');
  const heldOutboxStatus = scalar(
    paymentSql(`
      UPDATE outbox_events
      SET status='PUBLISHING', retry_count=0, last_error=NULL
      WHERE event_type='payment.payment.succeeded'
        AND payload::jsonb->>'paymentId'='${raceFinalPaymentData.balancePaymentId}'
      RETURNING status::text;
    `),
  );
  assert(heldOutboxStatus === 'PUBLISHING', 'Callback-race outbox event was not held');
  run('docker', ['start', 'day37-e2e-rabbitmq']);
  await waitForRabbitMq();
  parcelSql(`
    UPDATE parcels
    SET final_payment_deadline='${racePaidAt}'::timestamptz + interval '1 second'
    WHERE id='${callbackRaceParcel.parcelId}';
  `);
  await new Promise((resolve) => setTimeout(resolve, 2_000));
  await triggerSettlementTimeoutJob();
  const rejectedTimeoutParcel = await waitForParcel(
    checkInTimeoutParcel.parcelId,
    passengerToken,
    (detail) => detail.status === 'REJECTED',
    'Check-in timeout did not reject the reserved Parcel',
  );
  assert(
    rejectedTimeoutParcel.forfeitedDepositVnd === reservedTimeoutParcel.depositPaidVnd,
    'Check-in timeout did not forfeit the full deposit',
  );
  assert(
    scalar(
      tripSql(`
        SELECT state::text FROM trip_cargo_parcels
        WHERE trip_id='${ids.trip}' AND parcel_id='${checkInTimeoutParcel.parcelId}';
      `),
    ) === 'RELEASED',
    'Check-in timeout did not release Trip cargo',
  );
  pass('check-in timeout forfeits deposit and releases cargo');

  const timedOutBeforeCallback = await waitForParcel(
    callbackRaceParcel.parcelId,
    passengerToken,
    (detail) => detail.status === 'REJECTED' && detail.forfeitedDepositVnd > 0,
    'Final-payment timeout did not reject before callback delivery',
  );
  assert(
    timedOutBeforeCallback.balancePaidVnd === 0,
    'Balance was recognized before the delayed callback arrived',
  );

  const releasedOutboxStatus = scalar(
    paymentSql(`
      UPDATE outbox_events
      SET status='PENDING', retry_count=0, last_error=NULL
      WHERE event_type='payment.payment.succeeded'
        AND payload::jsonb->>'paymentId'='${raceFinalPaymentData.balancePaymentId}'
        AND status='PUBLISHING'
      RETURNING status::text;
    `),
  );
  assert(releasedOutboxStatus === 'PENDING', 'Callback-race outbox event was not released');
  run('docker', ['restart', 'day37-e2e-payment']);
  await waitFor(`${e2ePorts.urls.payment}/health`, 180_000);
  const reconciledRaceParcel = await waitForParcel(
    callbackRaceParcel.parcelId,
    passengerToken,
    (detail) =>
      detail.status === 'READY_TO_LOAD' &&
      detail.balancePaidVnd === callbackRaceReweigh.balanceRequiredVnd &&
      detail.forfeitedDepositVnd === 0,
    'On-time Payment callback did not reverse forfeiture and reconcile READY_TO_LOAD',
    180_000,
  );
  assert(
    reconciledRaceParcel.finalTotalPriceVnd === 3000,
    'Callback reconciliation changed the final price',
  );
  await load(callbackRaceParcel, assistantToken);
  pass('on-time Payment callback arriving after timeout reverses forfeiture and loads');

  const staleParcel = await createParcel(passengerToken, availableTrip.tripId, 1, 'STALE');
  tripSql(`
    UPDATE trips
    SET max_cargo_weight_kg = reserved_parcel_weight_kg + total_loaded_weight_kg,
        max_cargo_volume_m3 = reserved_parcel_volume_m3 + total_loaded_volume_m3
    WHERE id='${ids.trip}';
  `);
  const paymentCountBefore = Number(
    scalar(
      paymentSql(`SELECT count(*) FROM payments WHERE reference_id='${staleParcel.parcelId}';`),
    ),
  );
  const staleDeposit = await api('POST', `/v1/parcels/${staleParcel.parcelId}/deposit-payment`, {
    token: passengerToken,
    key: crypto.randomUUID(),
    body: { paymentMethod: 'WALLET' },
  });
  assertEnvelope(staleDeposit, 409, 'TRIP_CARGO_CAPACITY_EXCEEDED');
  const paymentCountAfter = Number(
    scalar(
      paymentSql(`SELECT count(*) FROM payments WHERE reference_id='${staleParcel.parcelId}';`),
    ),
  );
  assert(paymentCountAfter === paymentCountBefore, 'Capacity race created a Payment');
  pass('stale search result loses cargo race without charging passenger');

  const cargoState = scalar(
    tripSql(`
      SELECT reserved_parcel_weight_kg::text || ':' || total_loaded_weight_kg::text
      FROM trips WHERE id='${ids.trip}';
    `),
  );
  assert(cargoState.endsWith(':7.70'), `Unexpected Trip cargo counters: ${cargoState}`);
  const paymentStatuses = paymentSql(`
    SELECT status::text || ':' || count(*)::text
    FROM payments
    WHERE reference_id IN ('${balanceParcel.parcelId}','${refundParcel.parcelId}')
    GROUP BY status
    ORDER BY status;
  `);
  assert(
    paymentStatuses.split(/\r?\n/u).sort().join(',') === 'REFUNDED:1,SUCCEEDED:2',
    `Unexpected Payment status counts: ${paymentStatuses}`,
  );
  pass('Trip cargo ledger and real Payment rows agree with Parcel states');
}

let failure;
try {
  await buildAndStartStack();
  await waitForStack();
  seedFixtures();
  await runJourney();
} catch (error) {
  failure = error;
  const failureMessage = error instanceof Error ? error.stack || error.message : String(error);
  console.error(`FAILURE | ${failureMessage}`);
  for (const service of ['trip', 'payment', 'parcel', 'gateway']) {
    try {
      const logs = run('docker', [...compose, 'logs', '--no-color', '--tail', '180', service]);
      const relevant = logs
        .split(/\r?\n/u)
        .filter((line) =>
          /error|exception|failed|HTTP (POST|GET)|payment|refund|parcel/iu.test(line),
        )
        .slice(-60)
        .join('\n');
      console.error(`DIAGNOSTIC ${service}\n${relevant || '(no relevant logs)'}`);
    } catch {
      // Cleanup below remains mandatory even when a service did not start.
    }
  }
} finally {
  try {
    run('docker', [...compose, 'down', '-v', '--remove-orphans'], { stdio: 'inherit' });
    pass('isolated E2E containers and volumes cleaned');
  } catch (error) {
    failure ||= error;
    console.error(`FAIL | isolated cleanup | ${String(error)}`);
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'parcel-settlement-e2e',
      runTag,
      assertions,
      passed: !failure,
      failure: failure instanceof Error ? failure.message : failure ? String(failure) : null,
    },
    null,
    2,
  ),
);
process.exitCode = failure ? 1 : 0;
