import { createHmac } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const useDevelopmentStack = process.env.DAY37_E2E_USE_DEV_STACK === '1';
const manageIsolatedStack =
  !useDevelopmentStack && process.env.DAY37_E2E_SKIP_COMPOSE !== '1';
const gatewayBaseUrl =
  process.env.DAY37_GATEWAY_BASE_URL ||
  (useDevelopmentStack ? 'http://localhost:3000' : 'http://localhost:55300');
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day37-e2e.yml',
  '--profile',
  'app',
];
const e2eEnv = useDevelopmentStack
  ? {}
  : {
      POSTGRES_PORT: '55437',
      REDIS_PORT: '56379',
      RABBITMQ_PORT: '55672',
      RABBITMQ_MGMT_PORT: '55673',
      IDENTITY_PORT: '55001',
      TRIP_PORT: '55002',
      BOOKING_PORT: '55003',
      PAYMENT_PORT: '55004',
      PARCEL_PORT: '55005',
      GATEWAY_PORT: '55300',
      VNPAY_HASH_SECRET: 'day37-e2e-vnpay-hash-secret-not-for-production',
    };
const postgresContainer =
  process.env.DAY37_POSTGRES_CONTAINER ||
  (useDevelopmentStack ? 'vietride_postgres' : 'day37-e2e-postgres');
const operatorId = '37000000-0000-4000-8000-000000000001';
const starterPlanId = '37000000-0000-4000-8000-000000000011';
const premiumPlanId = '37000000-0000-4000-8000-000000000012';
const subscriptionId = '37000000-0000-4000-8000-000000000021';
const results = [];

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
    `SET search_path TO ${database === 'vietride_identity' ? 'vietride_identity' : 'vietride_payment'},public; ${statement}`,
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
      ('${starterPlanId}', 'Day 37 E2E Starter', 'Isolated Day 37 starter fixture', 0, 0, 2, 2, 2, 2, 2, 80, false, true, false, true),
      ('${premiumPlanId}', 'Day 37 E2E Premium', 'Isolated Day 37 paid fixture', 120000, 1200000, 10, 10, 10, 10, 10, 1000, true, true, false, true)
    ON CONFLICT (id) DO UPDATE SET
      name = EXCLUDED.name, price_per_month = EXCLUDED.price_per_month,
      price_per_year = EXCLUDED.price_per_year, enable_parcel = EXCLUDED.enable_parcel,
      enable_shuttle = EXCLUDED.enable_shuttle, is_active = EXCLUDED.is_active;

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
  `);
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

async function api(method, pathname, token, body, idempotencyKey) {
  const response = await fetch(`${gatewayBaseUrl}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
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

function signVnPay(parameters) {
  const canonical = Object.entries(parameters)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
  const secret = process.env.VNPAY_HASH_SECRET || e2eEnv.VNPAY_HASH_SECRET;
  if (!secret)
    throw new Error('VNPAY_HASH_SECRET is required for the VNPay E2E harness.');
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
  if (manageIsolatedStack) {
    run('docker', [...compose, 'down', '-v', '--remove-orphans'], { env: e2eEnv });
    run('docker', [...compose, '--parallel', '1', 'up', '-d', '--build', 'gateway'], {
      env: e2eEnv,
    });
  }
  waitFor(`${gatewayBaseUrl}/health`);
  waitFor(`${gatewayBaseUrl}/ready`);
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
  const current = await api('GET', '/v1/operator/subscription', token);
  assert(
    current.status === 200 && current.json?.data?.status === 'ACTIVE',
    `subscription read failed: ${JSON.stringify(current)}`,
  );
  record('trial approval fixture is readable', true, `plan=${current.json.data.planId}`);

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
  await sendSubscriptionIpn(
    firstUpgrade.json.data.paymentRedirectUrl,
    '24',
    '3700000001',
  );
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
