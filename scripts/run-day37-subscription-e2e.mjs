import { createHmac } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const useDevelopmentStack = process.env.DAY37_E2E_USE_DEV_STACK === '1';
const gatewayBaseUrl = process.env.DAY37_GATEWAY_BASE_URL
  || (useDevelopmentStack ? 'http://localhost:3000' : 'http://localhost:55300');
const compose = [
  'compose',
  '--env-file', '.env',
  '-f', 'infra/docker/docker-compose.yml',
  '-f', 'infra/docker/docker-compose.day37-e2e.yml',
  '--profile', 'app',
];
const postgresContainer = useDevelopmentStack ? 'vietride_postgres' : 'day37-e2e-postgres';
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

function sql(statement, database = 'vietride_identity') {
  return run('docker', [
    'exec', postgresContainer, 'psql', '-v', 'ON_ERROR_STOP=1', '-U',
    process.env.POSTGRES_USER || 'vietride', '-d', database, '-Atc',
    `SET search_path TO ${database === 'vietride_identity' ? 'vietride_identity' : 'vietride_payment'},public; ${statement}`,
  ]);
}

function waitFor(url, timeoutMs = 180000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const probe = spawnSync('node', ['-e', `fetch('${url}').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))`], {
      cwd: root,
      stdio: 'ignore',
    });
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
      (id, operator_id, plan_id, status, started_at, expires_at, current_vehicles,
       current_routes, current_trips_this_month, last_reset_at)
    VALUES
      ('${subscriptionId}', '${operatorId}', '${starterPlanId}', 'ACTIVE', now(), now() + interval '30 days', 0, 0, 0, now())
    ON CONFLICT (operator_id) DO UPDATE SET
      plan_id = EXCLUDED.plan_id, previous_active_plan_id = NULL, status = 'ACTIVE',
      started_at = EXCLUDED.started_at, expires_at = EXCLUDED.expires_at,
      current_vehicles = 0, current_routes = 0, current_trips_this_month = 0,
      updated_at = now();
  `);
}

async function operatorToken() {
  const app = JSON.parse(fs.readFileSync(
    path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
    'utf8',
  ));
  const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || app.IdentityJwt.PrivateKey, 'RS256');
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
  return createHmac('sha512', process.env.VNPAY_HASH_SECRET || 'sandbox-hash-secret-for-local-dev-only')
    .update(canonical)
    .digest('hex');
}

async function runHarness() {
  if (!useDevelopmentStack) {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
    run('docker', [...compose, '--parallel', '1', 'up', '-d', '--build', 'gateway']);
  }
  waitFor(`${gatewayBaseUrl}/health`);
  waitFor(`${gatewayBaseUrl}/ready`);
  record(useDevelopmentStack ? 'development compose health' : 'isolated compose health', true, gatewayBaseUrl);

  seedIdentity();
  const token = await operatorToken();
  const current = await api('GET', '/v1/operator/subscription', token);
  assert(current.status === 200 && current.json?.data?.status === 'ACTIVE', `subscription read failed: ${JSON.stringify(current)}`);
  record('trial approval fixture is readable', true, `plan=${current.json.data.planId}`);

  const idempotencyKey = crypto.randomUUID();
  const upgradeRequest = { planId: premiumPlanId, billingPeriod: 'YEARLY', returnUrl: 'https://e2e.vietride.test/return' };
  const firstUpgrade = await api('POST', '/v1/operator/subscription/upgrade', token, upgradeRequest, idempotencyKey);
  assert(firstUpgrade.status === 202 && firstUpgrade.json?.data?.paymentId, `upgrade failed: ${JSON.stringify(firstUpgrade)}`);
  const replayUpgrade = await api('POST', '/v1/operator/subscription/upgrade', token, upgradeRequest, idempotencyKey);
  assert(replayUpgrade.status === 202 && replayUpgrade.json?.data?.paymentId === firstUpgrade.json.data.paymentId,
    `upgrade replay failed: ${JSON.stringify(replayUpgrade)}`);
  record('upgrade idempotency', true, `payment=${firstUpgrade.json.data.paymentId}`);

  const redirect = new URL(firstUpgrade.json.data.paymentRedirectUrl);
  const ipn = {
    vnp_Amount: redirect.searchParams.get('vnp_Amount'),
    vnp_ResponseCode: '00',
    vnp_TmnCode: redirect.searchParams.get('vnp_TmnCode'),
    vnp_TransactionNo: '3700000001',
    vnp_TxnRef: redirect.searchParams.get('vnp_TxnRef'),
  };
  ipn.vnp_SecureHash = signVnPay(ipn);
  const ipnQuery = new URLSearchParams(ipn).toString();
  const ipnFirst = await api('POST', `/v1/payments/subscription-vnpay-ipn?${ipnQuery}`);
  assert(ipnFirst.status === 200, `subscription IPN failed: ${JSON.stringify(ipnFirst)}`);
  const ipnReplay = await api('POST', `/v1/payments/subscription-vnpay-ipn?${ipnQuery}`);
  assert(ipnReplay.status === 200, `subscription IPN replay failed: ${JSON.stringify(ipnReplay)}`);
  record('VNPay IPN replay', true, `txnRef=${ipn.vnp_TxnRef}`);

  const paymentCount = sql(`SELECT count(*) FROM payments WHERE id = '${firstUpgrade.json.data.paymentId}'`, 'vietride_payment');
  assert(paymentCount.endsWith('1'), `payment persistence assertion failed: ${paymentCount}`);
  const outboxCount = sql("SELECT count(*) FROM outbox_events WHERE event_type = 'payment.subscription.payment_succeeded'", 'vietride_payment');
  assert(Number(outboxCount.split('\n').at(-1)) >= 1, `payment outbox assertion failed: ${outboxCount}`);
  record('Payment and outbox persistence', true, `paymentRows=${paymentCount.split('\n').at(-1)} outbox=${outboxCount.split('\n').at(-1)}`);
}

try {
  await runHarness();
} catch (error) {
  record('harness', false, error instanceof Error ? error.message : String(error));
}

console.log(JSON.stringify({ suite: 'day37-subscription-e2e', results }, null, 2));
process.exitCode = results.every((result) => result.passed) ? 0 : 1;
