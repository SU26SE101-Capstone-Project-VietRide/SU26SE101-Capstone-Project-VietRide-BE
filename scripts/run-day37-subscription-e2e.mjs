import { createHmac } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const useDevelopmentStack = process.env.DAY37_E2E_USE_DEV_STACK === '1';
const manageIsolatedStack = !useDevelopmentStack && process.env.DAY37_E2E_SKIP_COMPOSE !== '1';
const invocationId = `${process.pid}-${crypto.randomUUID().slice(0, 8)}`;
const composeProject = `day37-e2e-${invocationId}`;
const containerPrefix = composeProject;
let gatewayBaseUrl = process.env.DAY37_GATEWAY_BASE_URL || 'http://localhost:3000';
let identityBaseUrl = process.env.DAY37_IDENTITY_BASE_URL || 'http://localhost:5001';
let ragBaseUrl = process.env.DAY37_RAG_BASE_URL || 'http://localhost:3003';
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-p',
  composeProject,
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day37-e2e.yml',
  '--profile',
  'app',
];
let e2eEnv = {};
let postgresContainer = process.env.DAY37_POSTGRES_CONTAINER || 'vietride_postgres';
const operatorId = '37000000-0000-4000-8000-000000000001';
const starterPlanId = '37000000-0000-4000-8000-000000000011';
const premiumPlanId = '37000000-0000-4000-8000-000000000012';
const subscriptionId = '37000000-0000-4000-8000-000000000021';
const passengerId = '37000000-0000-4000-8000-000000000032';
const driverId = '37000000-0000-4000-8000-000000000033';
const originStationId = '37000000-0000-4000-8000-000000000041';
const destinationStationId = '37000000-0000-4000-8000-000000000042';
const vehicleTypeId = '37000000-0000-4000-8000-000000000043';
const vehicleId = '37000000-0000-4000-8000-000000000044';
const routeId = '37000000-0000-4000-8000-000000000045';
const tripId = '37000000-0000-4000-8000-000000000046';
const results = [];

function localEnvValue(name) {
  const envPath = path.join(root, '.env');
  if (!fs.existsSync(envPath)) return undefined;
  const line = fs
    .readFileSync(envPath, 'utf8')
    .split(/\r?\n/)
    .find((candidate) => candidate.startsWith(`${name}=`));
  if (!line) return undefined;
  return line
    .slice(name.length + 1)
    .trim()
    .replace(/^(["'])(.*)\1$/, '$2');
}

async function allocatePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (!address || typeof address === 'string') {
        server.close(() => reject(new Error('Could not allocate an isolated host port.')));
        return;
      }
      server.close((error) => (error ? reject(error) : resolve(address.port)));
    });
  });
}

async function configureRuntime() {
  if (useDevelopmentStack || !manageIsolatedStack) return;

  const names = [
    'POSTGRES_PORT',
    'REDIS_PORT',
    'RABBITMQ_PORT',
    'RABBITMQ_MGMT_PORT',
    'IDENTITY_PORT',
    'TRIP_PORT',
    'BOOKING_PORT',
    'PAYMENT_PORT',
    'PARCEL_PORT',
    'GATEWAY_PORT',
    'RAG_PORT',
  ];
  const ports = await Promise.all(names.map(() => allocatePort()));
  e2eEnv = Object.fromEntries(names.map((name, index) => [name, String(ports[index])]));
  Object.assign(e2eEnv, {
    DAY37_CONTAINER_PREFIX: containerPrefix,
    VNPAY_HASH_SECRET: 'day37-e2e-vnpay-hash-secret-not-for-production',
  });
  postgresContainer = `${containerPrefix}-postgres`;
  gatewayBaseUrl = `http://localhost:${e2eEnv.GATEWAY_PORT}`;
  identityBaseUrl = `http://localhost:${e2eEnv.IDENTITY_PORT}`;
  ragBaseUrl = `http://localhost:${e2eEnv.RAG_PORT}`;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  }
  return result.stdout.trim();
}

function record(name, passed, detail) {
  results.push({ name, passed, detail });
  console.log(`${passed ? 'PASS' : 'FAIL'} | ${name} | ${detail}`);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function scalar(value) {
  return String(value).split(/\r?\n/).filter(Boolean).at(-1)?.trim() ?? '';
}

function poll(label, probe, predicate, timeoutMs = 120000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = probe();
    if (predicate(last)) return last;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  throw new Error(`${label} timed out; last=${last}`);
}

function sql(statement, database = 'vietride_identity') {
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
    '-Atc',
    `SET search_path TO ${database},public; ${statement}`,
  ]);
}

function waitFor(url, timeoutMs = 180000) {
  const deadline = Date.now() + timeoutMs;
  const curlCommand = process.platform === 'win32' ? 'curl.exe' : 'curl';
  while (Date.now() < deadline) {
    const probe = spawnSync(
      curlCommand,
      ['--fail', '--silent', '--show-error', '--max-time', '5', url],
      {
        cwd: root,
        stdio: 'ignore',
      },
    );
    if (probe.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  throw new Error(`Timed out waiting for ${url}`);
}

function seedIdentity() {
  sql(`
    INSERT INTO subscription_plans
      (id, name, description, price_per_month, price_per_year, max_vehicles, max_drivers,
       max_assistants, max_operator_users, max_routes, max_trips_per_month,
       enable_parcel, enable_shuttle, enable_rag, is_active)
    VALUES
      ('${starterPlanId}', 'Day 37 E2E Starter', 'Isolated Day 37 starter fixture', 0, 0, 2, 2, 2, 2, 2, 80, true, true, true, true),
      ('${premiumPlanId}', 'Day 37 E2E Premium', 'Isolated Day 37 paid fixture', 120000, 1200000, 10, 10, 10, 10, 10, 1000, true, true, true, true)
    ON CONFLICT (id) DO UPDATE SET
      name = EXCLUDED.name, price_per_month = EXCLUDED.price_per_month,
      price_per_year = EXCLUDED.price_per_year, enable_parcel = EXCLUDED.enable_parcel,
      enable_shuttle = EXCLUDED.enable_shuttle, enable_rag = EXCLUDED.enable_rag,
      max_vehicles = EXCLUDED.max_vehicles, is_active = EXCLUDED.is_active;

    INSERT INTO operators
      (id, name, business_registration_number, tax_code, contact_email, contact_phone,
       registration_status, approved_at, is_active)
    VALUES
      ('${operatorId}', 'Day 37 E2E Operator', 'D37-E2E-BRN', 'D37-E2E-TAX',
       'day37-e2e@example.test', '+84910000037', 'APPROVED', now(), true)
    ON CONFLICT (id) DO UPDATE SET registration_status = 'APPROVED', is_active = true, deleted_at = NULL;

    INSERT INTO operator_subscriptions
      (id, operator_id, active_plan_id, status, started_at, expires_at, current_vehicles,
       current_routes, current_trips_this_month, last_reset_at)
    VALUES
      ('${subscriptionId}', '${operatorId}', '${starterPlanId}', 'ACTIVE', now(), now() + interval '30 days', 0, 0, 0, now())
    ON CONFLICT (operator_id) DO UPDATE SET
      active_plan_id = EXCLUDED.active_plan_id, status = 'ACTIVE',
      started_at = EXCLUDED.started_at, expires_at = EXCLUDED.expires_at,
      current_vehicles = 0, current_routes = 0, current_trips_this_month = 0,
      updated_at = now();

    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${passengerId}','passenger-day37@example.test','+84910000038','Day 37 Passenger','PASSENGER','ACTIVE',NULL),
      ('${driverId}','driver-day37@example.test','+84910000039','Day 37 Driver','DRIVER','ACTIVE','${operatorId}')
    ON CONFLICT (id) DO UPDATE SET status='ACTIVE', deleted_at=NULL;
  `);

  sql(
    `
    INSERT INTO stations (id,name,slug,city,province,is_active)
    VALUES
      ('${originStationId}','Day 37 Origin','day37-origin','Ho Chi Minh','Ho Chi Minh',true),
      ('${destinationStationId}','Day 37 Destination','day37-destination','Da Nang','Da Nang',true)
    ON CONFLICT (id) DO UPDATE SET is_active=true, deleted_at=NULL;
    INSERT INTO vehicle_types
      (id,code,display_name,default_seat_count,is_system_defined,is_active)
    VALUES ('${vehicleTypeId}','D37_E2E','Day 37 Vehicle',20,false,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true;
    INSERT INTO vehicles
      (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,
       max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
    VALUES
      ('${vehicleId}','${operatorId}','${vehicleTypeId}','D37E2E',
       '{"version":1,"totalSeats":20,"rows":5,"cols":4,"decks":1,"aisles":[],"seats":[]}',
       20,100,10,'ACTIVE',true)
    ON CONFLICT (id) DO UPDATE SET status='ACTIVE', is_active=true, deleted_at=NULL;
    INSERT INTO routes
      (id,operator_id,name,origin_station_id,destination_station_id,base_fare,
       estimated_duration_minutes,is_active)
    VALUES
      ('${routeId}','${operatorId}','Day 37 Route','${originStationId}',
       '${destinationStationId}',150000,480,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true, deleted_at=NULL;
    INSERT INTO trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,
       status,source,base_fare,max_cargo_weight_kg,max_cargo_volume_m3,
       estimated_passenger_luggage_kg,reserved_parcel_weight_kg,
       reserved_parcel_volume_m3,total_loaded_weight_kg,total_loaded_volume_m3)
    VALUES
      ('${tripId}','${operatorId}','${routeId}','${vehicleId}','${driverId}',now()+interval '1 day',
       now()+interval '1 day 8 hours','SCHEDULED','MANUAL',150000,100,10,0,0,0,0,0)
    ON CONFLICT (id) DO UPDATE SET status='SCHEDULED';
  `,
    'vietride_trip',
  );
}

async function operatorToken() {
  const app = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const key = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || app.IdentityJwt.PrivateKey,
    'RS256',
  );
  return new SignJWT({
    role: 'OPERATOR_ADMIN',
    email: 'day37-e2e@example.test',
    hasPhone: 'true',
    operatorId,
  })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || app.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject('37000000-0000-4000-8000-000000000031')
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

async function passengerToken() {
  const app = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const key = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || app.IdentityJwt.PrivateKey,
    'RS256',
  );
  return new SignJWT({ role: 'PASSENGER', email: 'passenger-day37@example.test', hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || app.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(passengerId)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

async function apiAt(baseUrl, method, pathname, token, body, idempotencyKey, internalToken) {
  const response = await fetch(`${baseUrl}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(internalToken ? { 'X-Internal-Auth': `Bearer ${internalToken}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  let json;
  try {
    json = await response.json();
  } catch {
    json = null;
  }
  return { status: response.status, json };
}

function api(method, pathname, token, body, idempotencyKey) {
  return apiAt(gatewayBaseUrl, method, pathname, token, body, idempotencyKey);
}

async function internalToken() {
  const secret = process.env.INTERNAL_JWT_SECRET || localEnvValue('INTERNAL_JWT_SECRET');
  assert(secret && secret.length >= 32, 'INTERNAL_JWT_SECRET >=32 chars is required');
  return new SignJWT({ callerService: 'day37-e2e' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setSubject('day37-e2e')
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('2m')
    .sign(new TextEncoder().encode(secret));
}

function signVnPay(parameters) {
  const canonical = Object.entries(parameters)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
  const secret = process.env.VNPAY_HASH_SECRET || e2eEnv.VNPAY_HASH_SECRET;
  if (!secret) throw new Error('VNPAY_HASH_SECRET is required for the VNPay E2E harness.');
  return createHmac('sha512', secret).update(canonical).digest('hex');
}

function createIpn(paymentRedirectUrl, responseCode, transactionNo) {
  const redirect = new URL(paymentRedirectUrl);
  const ipn = {
    vnp_Amount: redirect.searchParams.get('vnp_Amount'),
    vnp_ResponseCode: responseCode,
    vnp_TmnCode: redirect.searchParams.get('vnp_TmnCode'),
    vnp_TransactionNo: transactionNo,
    vnp_TxnRef: redirect.searchParams.get('vnp_TxnRef'),
  };
  ipn.vnp_SecureHash = signVnPay(ipn);
  return ipn;
}

async function sendSubscriptionIpn(paymentRedirectUrl, responseCode, transactionNo) {
  const ipn = createIpn(paymentRedirectUrl, responseCode, transactionNo);
  const response = await api(
    'POST',
    `/v1/payments/subscription-vnpay-ipn?${new URLSearchParams(ipn).toString()}`,
  );
  assert(response.status === 200, `subscription IPN failed: ${JSON.stringify(response)}`);
  return ipn;
}

async function runHarness() {
  await configureRuntime();
  if (manageIsolatedStack) {
    run('docker', [...compose, '--parallel', '1', 'up', '-d', '--build', 'gateway', 'rag'], {
      env: e2eEnv,
    });
  }
  waitFor(`${gatewayBaseUrl}/health`);
  waitFor(`${gatewayBaseUrl}/ready`);
  waitFor(`${ragBaseUrl}/health`);
  waitFor(`${ragBaseUrl}/ready`);
  record(
    useDevelopmentStack
      ? 'development compose health'
      : manageIsolatedStack
        ? 'isolated compose health'
        : 'externally managed isolated compose health',
    true,
    gatewayBaseUrl,
  );

  seedIdentity();
  const token = await operatorToken();
  const serviceToken = await internalToken();
  const current = await api('GET', '/v1/operator/subscription', token);
  assert(
    current.status === 200 &&
      current.json?.data?.status === 'ACTIVE' &&
      current.json?.data?.plan?.modules?.enableParcel === true &&
      current.json?.data?.plan?.modules?.enableRag === true,
    `subscription read failed: ${JSON.stringify(current)}`,
  );
  const activeRag = await api('GET', '/v1/operator/policies?page=1&pageSize=1', token);
  assert(activeRag.status === 200, `ACTIVE RAG access failed: ${JSON.stringify(activeRag)}`);
  const activeQuota = await apiAt(
    identityBaseUrl,
    'POST',
    `/internal/v1/operators/${operatorId}/usage/increment`,
    undefined,
    { resource: 'VEHICLES', delta: 1 },
    crypto.randomUUID(),
    serviceToken,
  );
  assert(
    activeQuota.status === 200 && activeQuota.json?.usage?.currentVehicles === 1,
    `ACTIVE quota increment failed: ${JSON.stringify(activeQuota)}`,
  );
  record('ACTIVE plan modules are usable', true, `plan=${current.json.data.plan.planId}`);
  record('ACTIVE quota uses active plan', true, 'VEHICLES=1/2');

  const idempotencyKey = crypto.randomUUID();
  const upgradeRequest = {
    planId: premiumPlanId,
    billingPeriod: 'YEARLY',
    paymentMethod: 'VNPAY',
    returnUrl: 'https://e2e.vietride.test/return',
  };
  const firstUpgrade = await api(
    'POST',
    '/v1/operator/subscription/upgrade',
    token,
    upgradeRequest,
    idempotencyKey,
  );
  assert(
    firstUpgrade.status === 202 && firstUpgrade.json?.data?.paymentId,
    `upgrade failed: ${JSON.stringify(firstUpgrade)}`,
  );
  const replayUpgrade = await api(
    'POST',
    '/v1/operator/subscription/upgrade',
    token,
    upgradeRequest,
    idempotencyKey,
  );
  assert(
    replayUpgrade.status === 202 &&
      replayUpgrade.json?.data?.paymentId === firstUpgrade.json.data.paymentId,
    `upgrade replay failed: ${JSON.stringify(replayUpgrade)}`,
  );
  record('upgrade idempotency', true, `payment=${firstUpgrade.json.data.paymentId}`);

  const upgradeAttemptId = firstUpgrade.json.data.upgradeAttemptId;
  await sendSubscriptionIpn(firstUpgrade.json.data.paymentRedirectUrl, '24', '3700000001');
  poll(
    'Identity payment-failed event consumption',
    () =>
      scalar(
        sql(
          `SELECT latest_payment_status::text FROM subscription_upgrade_attempts WHERE id='${upgradeAttemptId}'`,
        ),
      ),
    (status) => status === 'FAILED',
  );

  const retryKey = crypto.randomUUID();
  const retryPath = `/v1/operator/subscription/upgrade/${upgradeAttemptId}/retry-payment`;
  const firstRetry = await api('POST', retryPath, token, undefined, retryKey);
  assert(
    firstRetry.status === 202 && firstRetry.json?.data?.paymentId,
    `subscription retry failed: ${JSON.stringify(firstRetry)}`,
  );
  const replayRetry = await api('POST', retryPath, token, undefined, retryKey);
  assert(
    replayRetry.status === 202 &&
      replayRetry.json?.data?.paymentId === firstRetry.json.data.paymentId,
    `subscription retry replay failed: ${JSON.stringify(replayRetry)}`,
  );
  const retryPaymentCount = scalar(
    sql(
      `SELECT count(*)||':'||count(DISTINCT idempotency_key) FROM payments WHERE reference_type='SUBSCRIPTION' AND reference_id='${upgradeAttemptId}'`,
      'vietride_payment',
    ),
  );
  assert(retryPaymentCount === '2:2', `retry created duplicate payment rows: ${retryPaymentCount}`);
  record(
    'retry-payment idempotency',
    true,
    `attempt=${upgradeAttemptId} payment=${firstRetry.json.data.paymentId}`,
  );

  sql(
    `UPDATE subscription_upgrade_attempts SET due_at=now()-interval '1 minute' WHERE id='${upgradeAttemptId}'`,
  );
  poll(
    'Identity lifecycle expiry',
    () =>
      [
        scalar(
          sql(
            `SELECT status::text FROM subscription_upgrade_attempts WHERE id='${upgradeAttemptId}'`,
          ),
        ),
        scalar(
          sql(
            `SELECT status::text FROM payments WHERE id='${firstRetry.json.data.paymentId}'`,
            'vietride_payment',
          ),
        ),
        scalar(
          sql(
            `SELECT count(*) FROM payments WHERE reference_type='SUBSCRIPTION' AND reference_id='${upgradeAttemptId}'`,
            'vietride_payment',
          ),
        ),
      ].join(':'),
    (state) => state === 'EXPIRED:EXPIRED:2',
    180000,
  );
  record('lifecycle expiry idempotency', true, `attempt=${upgradeAttemptId} paymentRows=2`);

  const successKey = crypto.randomUUID();
  const successUpgrade = await api(
    'POST',
    '/v1/operator/subscription/upgrade',
    token,
    upgradeRequest,
    successKey,
  );
  assert(
    successUpgrade.status === 202 && successUpgrade.json?.data?.paymentId,
    `success upgrade failed: ${JSON.stringify(successUpgrade)}`,
  );
  const successIpn = await sendSubscriptionIpn(
    successUpgrade.json.data.paymentRedirectUrl,
    '00',
    '3700000002',
  );
  const successIpnReplay = await api(
    'POST',
    `/v1/payments/subscription-vnpay-ipn?${new URLSearchParams(successIpn).toString()}`,
  );
  assert(
    successIpnReplay.status === 200,
    `subscription IPN replay failed: ${JSON.stringify(successIpnReplay)}`,
  );
  record('VNPay IPN replay', true, `txnRef=${successIpn.vnp_TxnRef}`);

  const paymentCount = sql(
    `SELECT count(*) FROM payments WHERE id = '${successUpgrade.json.data.paymentId}'`,
    'vietride_payment',
  );
  assert(paymentCount.endsWith('1'), `payment persistence assertion failed: ${paymentCount}`);
  const outboxCount = sql(
    "SELECT count(*) FROM outbox_events WHERE event_type = 'payment.subscription.payment_succeeded'",
    'vietride_payment',
  );
  assert(
    Number(outboxCount.split('\n').at(-1)) >= 1,
    `payment outbox assertion failed: ${outboxCount}`,
  );
  record(
    'Payment and outbox persistence',
    true,
    `paymentRows=${paymentCount.split('\n').at(-1)} outbox=${outboxCount.split('\n').at(-1)}`,
  );

  sql(`
    UPDATE operator_subscriptions
    SET active_plan_id='${starterPlanId}', status='PENDING_PAYMENT',
        current_vehicles=0, updated_at=now()
    WHERE id='${subscriptionId}';
    UPDATE subscription_plans
    SET enable_parcel=true, enable_rag=true, max_vehicles=2, updated_at=now()
    WHERE id='${starterPlanId}';
  `);
  const pendingSubscription = await apiAt(
    identityBaseUrl,
    'GET',
    `/internal/v1/operators/${operatorId}/subscription`,
    undefined,
    undefined,
    undefined,
    serviceToken,
  );
  assert(
    pendingSubscription.status === 200 &&
      pendingSubscription.json?.status === 'PENDING_PAYMENT' &&
      pendingSubscription.json?.plan?.modules?.enableParcel === true &&
      pendingSubscription.json?.plan?.modules?.enableRag === true,
    `PENDING_PAYMENT active-plan modules drifted: ${JSON.stringify(pendingSubscription)}`,
  );
  record('PENDING_PAYMENT keeps active Parcel/RAG modules', true, `plan=${starterPlanId}`);

  const quotaIncrement = await apiAt(
    identityBaseUrl,
    'POST',
    `/internal/v1/operators/${operatorId}/usage/increment`,
    undefined,
    { resource: 'VEHICLES', delta: 1 },
    crypto.randomUUID(),
    serviceToken,
  );
  assert(
    quotaIncrement.status === 200 && quotaIncrement.json?.usage?.currentVehicles === 1,
    `PENDING_PAYMENT quota increment failed: ${JSON.stringify(quotaIncrement)}`,
  );
  record('PENDING_PAYMENT quota uses active plan', true, 'VEHICLES=1/2');

  const quotaAtCapacity = await apiAt(
    identityBaseUrl,
    'POST',
    `/internal/v1/operators/${operatorId}/usage/increment`,
    undefined,
    { resource: 'VEHICLES', delta: 1 },
    crypto.randomUUID(),
    serviceToken,
  );
  assert(
    quotaAtCapacity.status === 200 && quotaAtCapacity.json?.usage?.currentVehicles === 2,
    `PENDING_PAYMENT quota did not reach active-plan capacity: ${JSON.stringify(quotaAtCapacity)}`,
  );
  const quotaExceeded = await apiAt(
    identityBaseUrl,
    'POST',
    `/internal/v1/operators/${operatorId}/usage/increment`,
    undefined,
    { resource: 'VEHICLES', delta: 1 },
    crypto.randomUUID(),
    serviceToken,
  );
  const quotaAfterRejection = await apiAt(
    identityBaseUrl,
    'GET',
    `/internal/v1/operators/${operatorId}/subscription`,
    undefined,
    undefined,
    undefined,
    serviceToken,
  );
  assert(
    quotaExceeded.status === 422 &&
      quotaExceeded.json?.error?.code === 'SUBSCRIPTION_LIMIT_EXCEEDED' &&
      quotaAfterRejection.json?.usage?.currentVehicles === 2,
    `PENDING_PAYMENT quota hard limit drifted: ${JSON.stringify({ quotaExceeded, quotaAfterRejection })}`,
  );
  record('PENDING_PAYMENT active-plan hard limit', true, 'VEHICLES=2/2; rejected=422');

  const ragAllowed = await api('GET', '/v1/operator/policies?page=1&pageSize=1', token);
  assert(
    ragAllowed.status === 200,
    `PENDING_PAYMENT RAG access failed: ${JSON.stringify(ragAllowed)}`,
  );
  sql(
    `UPDATE subscription_plans SET enable_parcel=false, enable_rag=false, updated_at=now() WHERE id='${starterPlanId}'`,
  );
  const ragDisabled = await api('GET', '/v1/operator/policies?page=1&pageSize=1', token);
  assert(
    ragDisabled.status === 403 && ragDisabled.json?.error?.code === 'SUBSCRIPTION_MODULE_DISABLED',
    `Disabled RAG module contract drifted: ${JSON.stringify(ragDisabled)}`,
  );
  const disabledModules = await apiAt(
    identityBaseUrl,
    'GET',
    `/internal/v1/operators/${operatorId}/subscription`,
    undefined,
    undefined,
    undefined,
    serviceToken,
  );
  assert(
    disabledModules.status === 200 &&
      disabledModules.json?.plan?.modules?.enableParcel === false &&
      disabledModules.json?.plan?.modules?.enableRag === false,
    `Disabled active-plan modules drifted: ${JSON.stringify(disabledModules)}`,
  );
  const passenger = await passengerToken();
  const parcelCountBefore = Number(scalar(sql('SELECT count(*) FROM parcels', 'vietride_parcel')));
  const blockedParcel = await api(
    'POST',
    '/v1/parcels',
    passenger,
    {
      tripId,
      dropoffStopId: null,
      bookingId: null,
      itemName: 'Day 37 blocked parcel',
      description: 'Module guard acceptance probe',
      sizeCategory: 'SMALL',
      lengthCm: 20,
      widthCm: 20,
      heightCm: 20,
      estimatedWeightKg: 1,
      photoUrl: null,
      recipient: {
        fullName: 'Day 37 Recipient',
        phoneNumber: '0912345678',
        email: 'recipient-day37@example.test',
      },
      deliveryMethod: 'TERMINAL_PICKUP',
      paymentMethod: 'WALLET',
      voucherCode: null,
    },
    crypto.randomUUID(),
  );
  const parcelCountAfter = Number(scalar(sql('SELECT count(*) FROM parcels', 'vietride_parcel')));
  assert(
    blockedParcel.status === 403 &&
      blockedParcel.json?.error?.code === 'SUBSCRIPTION_MODULE_DISABLED' &&
      parcelCountAfter === parcelCountBefore,
    `Parcel module guard allowed a write or drifted: ${JSON.stringify({ blockedParcel, parcelCountBefore, parcelCountAfter })}`,
  );
  record('disabled Parcel/RAG flags stay enforced', true, 'RAG=403; Parcel=403/no-write');
}

let failed;
try {
  await runHarness();
} catch (error) {
  failed = error;
  record('harness', false, error instanceof Error ? error.message : String(error));
} finally {
  if (manageIsolatedStack) {
    try {
      run('docker', [...compose, 'down', '-v', '--remove-orphans'], { env: e2eEnv });
      record('isolated compose cleanup', true, 'containers and volumes removed');
    } catch (error) {
      failed ??= error;
      record(
        'isolated compose cleanup',
        false,
        error instanceof Error ? error.message : String(error),
      );
    }
  }
}

console.log(JSON.stringify({ suite: 'day37-subscription-e2e', results }, null, 2));
process.exitCode = failed || results.some((result) => !result.passed) ? 1 : 0;
