import 'dotenv/config';
import { spawnSync } from 'node:child_process';
import { createHmac, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import amqp from 'amqplib';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const useDev = process.env.DAY38_E2E_USE_DEV_STACK === '1';
const gateway = process.env.DAY38_GATEWAY_BASE_URL || (useDev ? 'http://localhost:3000' : 'http://localhost:57300');
const payment = process.env.DAY38_PAYMENT_BASE_URL || (useDev ? 'http://localhost:5004' : 'http://localhost:57004');
const notification = process.env.DAY38_NOTIFICATION_BASE_URL || (useDev ? 'http://localhost:3002' : 'http://localhost:57012');
const compose = [
  'compose', '--env-file', '.env',
  '-f', 'infra/docker/docker-compose.yml',
  '-f', 'infra/docker/docker-compose.day38-e2e.yml',
  '--profile', 'app',
];
const containers = {
  postgres: useDev ? 'vietride_postgres' : 'day38-e2e-postgres',
  redis: useDev ? 'vietride_redis' : 'day38-e2e-redis',
  rabbitmq: useDev ? 'vietride_rabbitmq' : 'day38-e2e-rabbitmq',
  payment: useDev ? 'vietride_payment' : 'day38-e2e-payment',
  notification: useDev ? 'vietride_notification' : 'day38-e2e-notification',
};
const ids = {
  operatorA: '38000000-0000-4000-8000-000000000001',
  operatorB: '38000000-0000-4000-8000-000000000002',
  oldOperator: '38000000-0000-4000-8000-000000000003',
  adminA: '38000000-0000-4000-8000-000000000011',
  staffA: '38000000-0000-4000-8000-000000000012',
  adminB: '38000000-0000-4000-8000-000000000013',
  systemAdmin: '38000000-0000-4000-8000-000000000014',
  systemAdminB: '38000000-0000-4000-8000-000000000017',
  driver: '38000000-0000-4000-8000-000000000015',
  passenger: '38000000-0000-4000-8000-000000000016',
  starterPlan: '38000000-0000-4000-8000-000000000021',
  paidPlan: '38000000-0000-4000-8000-000000000022',
  subscriptionA: '38000000-0000-4000-8000-000000000031',
  subscriptionB: '38000000-0000-4000-8000-000000000032',
  stationA: '38000000-0000-4000-8000-000000000041',
  stationB: '38000000-0000-4000-8000-000000000042',
  route: '38000000-0000-4000-8000-000000000043',
  vehicleType: '38000000-0000-4000-8000-000000000044',
  vehicle: '38000000-0000-4000-8000-000000000045',
  tripA: '38000000-0000-4000-8000-000000000051',
  tripB: '38000000-0000-4000-8000-000000000052',
  tripC: '38000000-0000-4000-8000-000000000053',
  tripRace: '38000000-0000-4000-8000-000000000054',
  tripLateRefund: '38000000-0000-4000-8000-000000000055',
  bookingA: '38000000-0000-4000-8000-000000000061',
  bookingB: '38000000-0000-4000-8000-000000000062',
  parcelA: '38000000-0000-4000-8000-000000000063',
  bookingRace: '38000000-0000-4000-8000-000000000064',
  bookingLateRefund: '38000000-0000-4000-8000-000000000065',
  bookingGroup: '38000000-0000-4000-8000-000000000066',
  bookingGroupA: '38000000-0000-4000-8000-000000000067',
  bookingGroupB: '38000000-0000-4000-8000-000000000068',
  legacyBooking: '38000000-0000-4000-8000-000000000069',
};
const results = [];
const state = {};

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    maxBuffer: 16 * 1024 * 1024,
  });
  if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  return result.stdout.trim();
}

function sql(database, schema, statement) {
  return run('docker', [
    'exec', containers.postgres, 'psql', '-v', 'ON_ERROR_STOP=1',
    '-U', process.env.POSTGRES_USER || 'vietride', '-d', database, '-qAtc',
    `SET search_path TO ${schema},public; ${statement}`,
  ]);
}

const paymentSql = (statement) => sql('vietride_payment', 'vietride_payment', statement);
const identitySql = (statement) => sql('vietride_identity', 'vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', 'vietride_trip', statement);
const bookingSql = (statement) => sql('vietride_booking', 'vietride_booking', statement);
const notificationSql = (statement) => sql('vietride_notification', 'vietride_notification', statement);
const redis = (...args) => run('docker', ['exec', containers.redis, 'redis-cli', ...args]);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function scalar(value) {
  const line = String(value).split(/\r?\n/).filter(Boolean).at(-1) ?? '';
  return line.trim();
}

function count(value) {
  return Number(scalar(value));
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

async function confirmBookingVnPay(redirectUrl, transactionNo) {
  const redirect = new URL(redirectUrl);
  const ipn = {
    vnp_Amount: redirect.searchParams.get('vnp_Amount'),
    vnp_ResponseCode: '00',
    vnp_TmnCode: redirect.searchParams.get('vnp_TmnCode'),
    vnp_TransactionNo: transactionNo,
    vnp_TransactionStatus: '00',
    vnp_TxnRef: redirect.searchParams.get('vnp_TxnRef'),
  };
  ipn.vnp_SecureHash = signVnPay(ipn);
  return api(payment, 'POST', `/v1/payments/vnpay-ipn?${new URLSearchParams(ipn)}`);
}

async function confirmSubscriptionVnPay(redirectUrl, transactionNo) {
  const redirect = new URL(redirectUrl);
  const ipn = {
    vnp_Amount: redirect.searchParams.get('vnp_Amount'),
    vnp_ResponseCode: '00',
    vnp_TmnCode: redirect.searchParams.get('vnp_TmnCode'),
    vnp_TransactionNo: transactionNo,
    vnp_TransactionStatus: '00',
    vnp_TxnRef: redirect.searchParams.get('vnp_TxnRef'),
  };
  ipn.vnp_SecureHash = signVnPay(ipn);
  return api(payment, 'POST', `/v1/payments/subscription-vnpay-ipn?${new URLSearchParams(ipn)}`);
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
  await poll(async () => {
    try {
      return (await fetch(url)).ok;
    } catch {
      return false;
    }
  }, `Timed out waiting for ${url}`, timeoutMs);
}

async function scenario(number, name, fn) {
  const startedAt = Date.now();
  await fn();
  const result = { scenario: `E2E-${String(number).padStart(2, '0')}`, name, passed: true, durationMs: Date.now() - startedAt };
  results.push(result);
  console.log(`PASS | ${result.scenario} | ${name} | ${result.durationMs}ms`);
}

async function userJwt(userId, role, operatorId) {
  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  return new SignJWT({
    role,
    ...(operatorId ? { operatorId, operator_id: operatorId } : {}),
    email: `${role.toLowerCase()}@day38.test`,
    hasPhone: true,
  })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(userId)
    .setIssuedAt().setExpirationTime('30m').sign(key);
}

async function internalJwt() {
  const secret = process.env.INTERNAL_JWT_SECRET;
  assert(secret && secret.length >= 32, 'INTERNAL_JWT_SECRET >=32 chars is required');
  return new SignJWT({ service: 'day38-e2e' })
    .setProtectedHeader({ alg: 'HS256' }).setIssuer('vietride-gateway')
    .setAudience('vietride-internal').setSubject('day38-e2e')
    .setIssuedAt().setExpirationTime('2m').sign(new TextEncoder().encode(secret));
}

async function api(baseUrl, method, pathname, { token, internalToken, body, key } = {}) {
  const response = await fetch(`${baseUrl}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(internalToken ? { 'X-Internal-Auth': `Bearer ${internalToken}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const json = await response.json().catch(() => null);
  return { status: response.status, json, headers: response.headers };
}

async function publish(routingKey, payload, eventId = payload.eventId) {
  const connection = await amqp.connect({
    hostname: '127.0.0.1',
    port: useDev ? Number(process.env.RABBITMQ_PORT || 5672) : 55682,
    username: process.env.RABBITMQ_USER || 'vietride',
    password: process.env.RABBITMQ_PASSWORD || 'vietride_dev',
  });
  try {
    const channel = await connection.createChannel();
    await channel.assertExchange('vietride.events', 'topic', { durable: true });
    channel.publish('vietride.events', routingKey, Buffer.from(JSON.stringify(payload)), {
      persistent: true,
      contentType: 'application/json',
      messageId: eventId,
    });
    await channel.waitForConfirms?.();
    await channel.close();
  } finally {
    await connection.close();
  }
}

function seedPrerequisites() {
  identitySql(`
    INSERT INTO subscription_plans
      (id,name,description,price_per_month,price_per_year,max_vehicles,max_drivers,max_assistants,max_operator_users,max_routes,max_trips_per_month,enable_parcel,enable_shuttle,enable_rag,is_active)
    VALUES
      ('${ids.starterPlan}','Day 38 Starter','Day 38 isolated prerequisite',0,0,2,2,2,3,2,50,false,true,false,true),
      ('${ids.paidPlan}','Day 38 Pro','Day 38 payable fixture',120000,1200000,20,20,20,20,20,1000,true,true,false,true)
    ON CONFLICT (id) DO UPDATE SET price_per_month=EXCLUDED.price_per_month,price_per_year=EXCLUDED.price_per_year,is_active=true;
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,address_street,address_ward,address_district,address_province,registration_status,approved_at,is_active)
    VALUES
      ('${ids.operatorA}','Day 38 Operator A','D38-A-BRN','D38-A-TAX','billing-a@day38.test','+84910038001','1 Test Street','Ward 1','District 1','HCM','APPROVED',now(),true),
      ('${ids.operatorB}','Day 38 Operator B','D38-B-BRN','D38-B-TAX','billing-b@day38.test','+84910038002','2 Test Street','Ward 2','District 2','HCM','APPROVED',now(),true),
      ('${ids.oldOperator}','Day 37 Legacy Operator','D37-OLD-BRN','D37-OLD-TAX','legacy@day38.test','+84910038003','3 Test Street','Ward 3','District 3','HCM','APPROVED',now()-interval '40 days',true)
    ON CONFLICT (id) DO UPDATE SET registration_status='APPROVED',is_active=true,deleted_at=NULL;
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.adminA}','admin-a@day38.test','+84910038101','Day 38 Admin A','OPERATOR_ADMIN','ACTIVE','${ids.operatorA}'),
      ('${ids.staffA}','staff-a@day38.test','+84910038102','Day 38 Staff A','OPERATOR_STAFF','ACTIVE','${ids.operatorA}'),
      ('${ids.adminB}','admin-b@day38.test','+84910038103','Day 38 Admin B','OPERATOR_ADMIN','ACTIVE','${ids.operatorB}'),
      ('${ids.systemAdmin}','system@day38.test','+84910038104','Day 38 System Admin','SYSTEM_ADMIN','ACTIVE',NULL),
      ('${ids.systemAdminB}','system-b@day38.test','+84910038107','Day 38 System Admin B','SYSTEM_ADMIN','ACTIVE',NULL),
      ('${ids.driver}','driver@day38.test','+84910038105','Day 38 Driver','DRIVER','ACTIVE','${ids.operatorA}'),
      ('${ids.passenger}','passenger@day38.test','+84910038106','Day 38 Passenger','PASSENGER','ACTIVE',NULL)
    ON CONFLICT (id) DO UPDATE SET status='ACTIVE',operator_id=EXCLUDED.operator_id,deleted_at=NULL;
    INSERT INTO user_devices (id,user_id,fcm_token,platform,is_active)
    VALUES ('38000000-0000-4000-8000-000000000071','${ids.adminA}','day38-admin-a-token','ANDROID',true)
    ON CONFLICT (user_id,fcm_token) DO UPDATE SET is_active=true;
    INSERT INTO operator_subscriptions
      (id,operator_id,plan_id,status,started_at,expires_at,current_vehicles,current_routes,current_trips_this_month,last_reset_at)
    VALUES
      ('${ids.subscriptionA}','${ids.operatorA}','${ids.starterPlan}','ACTIVE',now()-interval '1 day',now()+interval '29 days',0,0,0,now()),
      ('${ids.subscriptionB}','${ids.operatorB}','${ids.starterPlan}','ACTIVE',now()-interval '1 day',now()+interval '29 days',0,0,0,now())
    ON CONFLICT (operator_id) DO UPDATE SET plan_id=EXCLUDED.plan_id,status='ACTIVE',previous_active_plan_id=NULL,updated_at=now();
  `);

  paymentSql(`
    INSERT INTO wallets (user_id,balance,currency,row_version)
    VALUES ('${ids.passenger}',5000000,'VND',0)
    ON CONFLICT (user_id) DO UPDATE SET balance=5000000,row_version=0;
    INSERT INTO operator_wallets (operator_id,balance,currency,row_version)
    VALUES ('${ids.operatorA}',5000000,'VND',0),('${ids.operatorB}',1000000,'VND',0)
    ON CONFLICT (operator_id) DO UPDATE SET balance=EXCLUDED.balance,row_version=0,updated_at=now();
    UPDATE platform_wallets SET balance=10000000,row_version=0,updated_at=now();
  `);

  tripSql(`
    INSERT INTO stations (id,name,slug,city,province,latitude,longitude,supports_shuttle,is_active)
    VALUES
      ('${ids.stationA}','Day 38 Origin','day38-origin','HCM','HCM',10.77,106.70,false,true),
      ('${ids.stationB}','Day 38 Destination','day38-destination','HCM','HCM',10.80,106.75,false,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true,deleted_at=NULL;
    INSERT INTO routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,is_active)
    VALUES ('${ids.route}','${ids.operatorA}','Day 38 Route','${ids.stationA}','${ids.stationB}',100000,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true,deleted_at=NULL;
    INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_active)
    VALUES ('${ids.vehicleType}','DAY38_BUS','Day 38 Bus',20,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true;
    INSERT INTO vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active)
    VALUES ('${ids.vehicle}','${ids.operatorA}','${ids.vehicleType}','51B-380.01','{}',20,'ACTIVE',true)
    ON CONFLICT (id) DO UPDATE SET status='ACTIVE',is_active=true,deleted_at=NULL;
    INSERT INTO trips (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare,has_substitution)
    VALUES
      ('${ids.tripA}','${ids.operatorA}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '3 hours',now()+interval '1 hour','IN_PROGRESS','MANUAL',100000,false),
      ('${ids.tripB}','${ids.operatorA}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '5 hours',now()-interval '3 hours','DISRUPTED','MANUAL',100000,false),
      ('${ids.tripC}','${ids.operatorA}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '7 hours',now()-interval '5 hours','DISRUPTED','MANUAL',100000,true),
      ('${ids.tripRace}','${ids.operatorA}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '2 hours',now()+interval '2 hours','IN_PROGRESS','MANUAL',100000,false),
      ('${ids.tripLateRefund}','${ids.operatorA}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '2 hours 15 minutes',now()+interval '2 hours 15 minutes','IN_PROGRESS','MANUAL',100000,false)
    ON CONFLICT (id) DO UPDATE SET departure_date_time=EXCLUDED.departure_date_time,estimated_arrival_time=EXCLUDED.estimated_arrival_time,status=EXCLUDED.status,has_substitution=EXCLUDED.has_substitution,completed_at=NULL,completed_by_user_id=NULL;
  `);

  bookingSql(`
    INSERT INTO bookings
      (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,
       base_fare,discount_amount,total_amount,status,confirmed_at,created_at,updated_at)
    VALUES
      ('${ids.legacyBooking}','VR-20000101-D3800001','${ids.passenger}','${ids.tripA}','${ids.operatorA}',
       '${ids.stationA}','${ids.stationB}',80000,0,80000,'CONFIRMED','2000-01-01T00:00:00Z',
       '2000-01-01T00:00:00Z','2000-01-01T00:00:00Z')
    ON CONFLICT (id) DO UPDATE SET total_amount=EXCLUDED.total_amount,discount_amount=0,status='CONFIRMED',updated_at=now();
  `);
}

async function triggerJob(name, invoiceId) {
  const jwt = await internalJwt();
  const query = invoiceId ? `?invoiceId=${invoiceId}` : '';
  const response = await api(payment, 'POST', `/internal/e2e/day38/jobs/${name}${query}`, { internalToken: jwt });
  assert(response.status === 202, `Could not enqueue ${name}: ${JSON.stringify(response)}`);
  return response.json?.jobId;
}

async function createCharge(referenceType, referenceId, tripId, amount, extra = {}) {
  const jwt = await internalJwt();
  const allocation = {
    referenceId,
    referenceType,
    operatorId: ids.operatorA,
    tripId,
    grossAmount: amount + (extra.vietrideVoucher || 0) + (extra.operatorVoucher || 0),
    voucherVietRideFundedAmount: extra.vietrideVoucher || 0,
    voucherOperatorFundedAmount: extra.operatorVoucher || 0,
  };
  return api(payment, 'POST', '/internal/v1/payments/charge', {
    internalToken: jwt,
    key: extra.key || `day38-charge-${referenceId}`,
    body: { referenceType, referenceId, userId: ids.passenger, amount, method: extra.method || 'WALLET', context: { version: 1, allocations: [allocation] } },
  });
}

async function runAcceptance() {
  if (!useDev) {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
    run('docker', [...compose, '--parallel', '1', 'up', '-d', '--build', 'gateway', 'notification']);
  }
  await Promise.all([
    waitFor(`${gateway}/health`), waitFor(`${gateway}/ready`),
    waitFor(`${payment}/health`), waitFor(`${payment}/ready`),
    waitFor(`${notification}/health`), waitFor(`${notification}/ready`),
  ]);
  seedPrerequisites();
  state.tokens = {
    operatorA: await userJwt(ids.adminA, 'OPERATOR_ADMIN', ids.operatorA),
    operatorB: await userJwt(ids.adminB, 'OPERATOR_ADMIN', ids.operatorB),
    staff: await userJwt(ids.staffA, 'OPERATOR_STAFF', ids.operatorA),
    system: await userJwt(ids.systemAdmin, 'SYSTEM_ADMIN'),
    systemB: await userJwt(ids.systemAdminB, 'SYSTEM_ADMIN'),
    driver: await userJwt(ids.driver, 'DRIVER', ids.operatorA),
    passenger: await userJwt(ids.passenger, 'PASSENGER'),
  };

  await scenario(1, 'Bootstrap and schema invariants', async () => {
    const requiredIndexes = [
      'uq_invoices_payment_id', 'uq_operator_ledger_entries_source',
      'uq_operator_trip_settlements_operator_trip', 'uq_platform_wallet_transactions_subscription',
    ];
    const present = paymentSql(`SELECT indexname FROM pg_indexes WHERE schemaname='vietride_payment' AND indexname IN (${requiredIndexes.map((x) => `'${x}'`).join(',')}) ORDER BY indexname`);
    for (const index of requiredIndexes) assert(present.includes(index), `Missing Day 38 index ${index}`);
    const eventId = '38000000-0000-4000-8000-000000000081';
    const payload = { eventId, operatorId: ids.oldOperator, approvedAt: new Date().toISOString() };
    await publish('identity.operator.approved', payload, eventId);
    await publish('identity.operator.approved', payload, eventId);
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_wallets WHERE operator_id='${ids.oldOperator}'`)) === 1, 'Operator wallet bootstrap timeout');
    assert(count(paymentSql(`SELECT count(*) FROM processed_integration_events WHERE event_id='${eventId}'`)) === 1, 'Approval consumer marker is not durable/idempotent');
    assert(count(paymentSql('SELECT count(*) FROM platform_wallets')) === 1, 'PlatformWallet singleton violated');
    const subscriptionPaymentMethods = identitySql("SELECT enumlabel FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid WHERE t.typname='subscription_payment_method' ORDER BY e.enumsortorder");
    assert(subscriptionPaymentMethods.split(/\r?\n/).includes('WALLET'), 'Identity subscription_payment_method enum is missing WALLET');
    const identityOutboxEnumSchema = scalar(identitySql("SELECT n.nspname FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE t.typname='outbox_event_status'"));
    assert(identityOutboxEnumSchema === 'vietride_identity', `Identity Outbox enum schema drifted: ${identityOutboxEnumSchema}`);
  });

  await scenario(2, 'Booking WALLET hold and trusted context', async () => {
    const beforePassenger = Number(scalar(paymentSql(`SELECT balance FROM wallets WHERE user_id='${ids.passenger}'`)));
    const beforePlatform = Number(scalar(paymentSql('SELECT balance FROM platform_wallets')));
    const response = await createCharge('BOOKING', ids.bookingA, ids.tripA, 200000);
    assert(response.status === 200 && response.json?.data?.status === 'SUCCEEDED', `Booking charge failed: ${JSON.stringify(response)}`);
    state.bookingPaymentId = response.json.data.paymentId;
    assert(Number(scalar(paymentSql(`SELECT balance FROM wallets WHERE user_id='${ids.passenger}'`))) === beforePassenger - 200000, 'Passenger debit mismatch');
    assert(Number(scalar(paymentSql('SELECT balance FROM platform_wallets'))) === beforePlatform + 200000, 'Platform hold mismatch');
    assert(count(paymentSql(`SELECT count(*) FROM payments WHERE id='${state.bookingPaymentId}' AND status='SUCCEEDED' AND context <> '{}'::jsonb`)) === 1, 'Trusted payment context missing');
    assert(count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE reference_id='${ids.bookingA}' AND entry_type='BOOKING_REVENUE' AND amount=200000`)) === 1, 'Booking revenue ledger mismatch');
  });

  await scenario(3, 'Booking WALLET replay and VNPay callback idempotency', async () => {
    const movements = count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${ids.bookingA}'`));
    const replay = await createCharge('BOOKING', ids.bookingA, ids.tripA, 200000);
    assert(replay.status === 200 && replay.json?.data?.paymentId === state.bookingPaymentId, 'Same-key replay did not return original payment');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${ids.bookingA}'`)) === movements, 'Replay duplicated platform hold');

    const internal = await internalJwt();
    const groupContext = {
      version: 1,
      allocations: [
        { referenceId: ids.bookingGroupA, referenceType: 'BOOKING', operatorId: ids.operatorA, tripId: ids.tripB, grossAmount: 60000, voucherVietRideFundedAmount: 0, voucherOperatorFundedAmount: 0 },
        { referenceId: ids.bookingGroupB, referenceType: 'BOOKING', operatorId: ids.operatorA, tripId: ids.tripC, grossAmount: 60000, voucherVietRideFundedAmount: 0, voucherOperatorFundedAmount: 0 },
      ],
    };
    const pending = await api(payment, 'POST', '/internal/v1/payments/charge', {
      internalToken: internal,
      key: 'day38-vnpay-booking-group',
      body: { referenceType: 'BOOKING_GROUP', referenceId: ids.bookingGroup, userId: ids.passenger, amount: 120000, method: 'VNPAY', context: groupContext },
    });
    assert(pending.status === 200 && pending.json?.data?.status === 'PENDING_REDIRECT', `VNPay group create failed: ${JSON.stringify(pending)}`);
    state.bookingGroupPaymentId = pending.json.data.paymentId;
    const firstIpn = await confirmBookingVnPay(pending.json.data.paymentRedirectUrl, '3800000003');
    const replayIpn = await confirmBookingVnPay(pending.json.data.paymentRedirectUrl, '3800000003');
    assert(firstIpn.status === 200 && (firstIpn.json?.rspCode ?? firstIpn.json?.RspCode) === '00', `VNPay callback failed: ${JSON.stringify(firstIpn)}`);
    assert(replayIpn.status === 200 && (replayIpn.json?.rspCode ?? replayIpn.json?.RspCode) === '00', `VNPay callback replay failed: ${JSON.stringify(replayIpn)}`);
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${ids.bookingGroup}' AND amount=120000`)) === 1, 'VNPay callback duplicated platform hold');
  });

  await scenario(4, 'Booking group creates distinct allocations from one event', async () => {
    const indexDefinition = scalar(paymentSql("SELECT indexdef FROM pg_indexes WHERE schemaname='vietride_payment' AND indexname='uq_operator_ledger_entries_source'"));
    assert(indexDefinition.includes('source_event_id') && indexDefinition.includes('entry_type') && indexDefinition.includes('reference_id'), 'Ledger allocation unique triple is wrong');
    const allocationRows = paymentSql(`SELECT reference_id||':'||amount||':'||source_event_id FROM operator_ledger_entries WHERE reference_id IN ('${ids.bookingGroupA}','${ids.bookingGroupB}') ORDER BY reference_id`);
    const lines = allocationRows.split(/\r?\n/).filter(Boolean);
    assert(lines.length === 2 && lines.every((line) => line.includes(':60000:')), `Booking group allocation rows drifted: ${allocationRows}`);
    assert(new Set(lines.map((line) => line.split(':').at(-1))).size === 1, 'Booking group allocations did not retain one source event');
    assert(count(paymentSql(`SELECT count(*) FROM (SELECT source_event_id,entry_type,reference_id FROM operator_ledger_entries GROUP BY 1,2,3 HAVING count(*)>1) d`)) === 0, 'Duplicate ledger allocation exists');
  });

  await scenario(5, 'Parcel revenue and voucher economics', async () => {
    const response = await createCharge('PARCEL', ids.parcelA, ids.tripA, 70000, { vietrideVoucher: 20000, operatorVoucher: 10000 });
    assert(response.status === 200, `Parcel charge failed: ${JSON.stringify(response)}`);
    state.parcelPaymentId = response.json.data.paymentId;
    const rows = paymentSql(`SELECT entry_type||':'||amount FROM operator_ledger_entries WHERE reference_id='${ids.parcelA}' ORDER BY entry_type`);
    assert(rows.includes('PARCEL_REVENUE:70000'), 'Passenger-paid parcel revenue missing');
    assert(rows.includes('VOUCHER_VIETRIDE_FUNDED_CREDIT:20000'), 'VietRide voucher credit missing');
    assert(rows.includes('VOUCHER_OPERATOR_FUNDED_AUDIT:0'), 'Operator voucher audit missing');
  });

  await scenario(6, 'Refund before settlement', async () => {
    const internal = await internalJwt();
    const beforeOperator = Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorA}'`)));
    const response = await api(payment, 'POST', '/internal/v1/wallet/refund', {
      internalToken: internal, key: 'day38-refund-booking-a',
      body: { userId: ids.passenger, amount: 50000, referenceType: 'BOOKING_REFUND', referenceId: ids.bookingA },
    });
    assert(response.status === 200, `Refund failed: ${JSON.stringify(response)}`);
    assert(Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorA}'`))) === beforeOperator, 'Refund moved OperatorWallet');
    assert(count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE reference_id='${ids.bookingA}' AND entry_type='BOOKING_REFUND' AND amount=-50000`)) === 1, 'Negative refund ledger missing');
  });

  await scenario(7, 'Trip COMPLETED creates one settlement marker', async () => {
    const first = await api(gateway, 'POST', `/v1/driver/trips/${ids.tripA}/complete`, { token: state.tokens.driver, key: 'day38-complete-trip-a' });
    assert(first.status === 200 && first.json?.data?.status === 'COMPLETED', `Trip complete failed: ${JSON.stringify(first)}`);
    const replay = await api(gateway, 'POST', `/v1/driver/trips/${ids.tripA}/complete`, { token: state.tokens.driver, key: 'day38-complete-trip-a' });
    assert(replay.status === 200, 'Trip completion same-key replay failed');
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_trip_settlements WHERE trip_id='${ids.tripA}'`)) === 1, 'Settlement marker timeout');
    state.settlementId = scalar(paymentSql(`SELECT id FROM operator_trip_settlements WHERE trip_id='${ids.tripA}'`));
    assert(count(tripSql(`SELECT count(*) FROM outbox_events WHERE event_type='trip.trip.completed' AND payload->>'tripId'='${ids.tripA}' AND status='PUBLISHED'`)) === 1, 'Canonical Trip Outbox event missing');
  });

  await scenario(8, 'DISRUPTED substitution audit does not change settlement economics', async () => {
    for (const [tripId, substitution, suffix] of [[ids.tripB, false, '82'], [ids.tripC, true, '83']]) {
      const eventId = `38000000-0000-4000-8000-0000000000${suffix}`;
      const payload = { eventId, occurredAt: new Date().toISOString(), tripId, operatorId: ids.operatorA, terminalAt: new Date().toISOString(), hasSubstitution: substitution, reason: 'DAY38_E2E' };
      await publish('trip.trip.disrupted', payload, eventId);
    }
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_trip_settlements WHERE trip_id IN ('${ids.tripB}','${ids.tripC}')`)) === 2, 'Disrupted markers timeout');
    paymentSql(`UPDATE operator_trip_settlements SET eligible_at=trip_terminal_at WHERE trip_id IN ('${ids.tripB}','${ids.tripC}')`);
    await triggerJob('settlement-eligibility');
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_trip_settlements WHERE trip_id IN ('${ids.tripB}','${ids.tripC}') AND status='ELIGIBLE' AND net_amount=60000`)) === 2, 'Substitution changed nonzero settlement economics');
  });

  await scenario(9, 'Invoice pipeline issues PDF through local boundary', async () => {
    run('docker', ['exec', containers.payment, 'sh', '-c', 'touch /tmp/day38-invoices/.fail-next-upload']);
    const key = 'day38-wallet-subscription-a';
    const request = { planId: ids.paidPlan, billingPeriod: 'MONTHLY', paymentMethod: 'WALLET', returnUrl: null };
    const response = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorA, key, body: request });
    assert(response.status === 200 && response.json?.data?.paymentId, `Subscription WALLET failed: ${JSON.stringify(response)}`);
    state.subscriptionPaymentId = response.json.data.paymentId;
    state.invoiceId = await poll(() => {
      const value = scalar(paymentSql(`SELECT id FROM invoices WHERE payment_id='${state.subscriptionPaymentId}'`));
      return value || false;
    }, 'Invoice DRAFT not created');
    await poll(() => scalar(paymentSql(`SELECT pdf_generation_status||':'||pdf_generation_attempts FROM invoices WHERE id='${state.invoiceId}'`)) === 'FAILED:1', 'Injected storage failure did not consume invoice attempt 1');
    paymentSql(`UPDATE invoices SET pdf_generation_next_retry_at=now()-interval '1 second' WHERE id='${state.invoiceId}' AND pdf_generation_status='FAILED'`);
    await triggerJob('invoice-reconciliation');
    await poll(() => scalar(paymentSql(`SELECT status FROM invoices WHERE id='${state.invoiceId}'`)) === 'ISSUED', 'Invoice not issued', 120_000);
    assert(scalar(paymentSql(`SELECT pdf_generation_attempts FROM invoices WHERE id='${state.invoiceId}'`)) === '2', 'Storage recovery did not use the second attempt');
    const objectPath = scalar(paymentSql(`SELECT storage_object_path FROM invoices WHERE id='${state.invoiceId}'`));
    assert(objectPath === `invoices/${ids.operatorA}/${state.invoiceId}.pdf`, 'Invoice object path is not canonical');
    const magic = run('docker', ['exec', containers.payment, 'sh', '-c', `head -c 5 /tmp/day38-invoices/${objectPath}`]);
    assert(magic.startsWith('%PDF-'), 'Generated object is not a PDF');
    assert(count(paymentSql(`SELECT count(*) FROM outbox_events WHERE event_type='payment.invoice.issued' AND payload->>'invoiceId'='${state.invoiceId}'`)) === 1, 'Invoice-issued Outbox missing');
  });

  await scenario(10, 'Eligibility boundary and UTC cron', async () => {
    const cron = scalar(paymentSql("SELECT value FROM hangfire.hash WHERE key='recurring-job:payment.trip-settlement-eligibility' AND field='Cron'"));
    assert(cron === '0 19 * * *', `Eligibility cron drifted: ${cron}`);
    paymentSql(`UPDATE operator_trip_settlements SET eligible_at=trip_terminal_at WHERE id='${state.settlementId}'`);
    await triggerJob('settlement-eligibility');
    await poll(() => scalar(paymentSql(`SELECT status FROM operator_trip_settlements WHERE id='${state.settlementId}'`)) === 'ELIGIBLE', 'Settlement did not become eligible');
  });

  await scenario(11, 'Weekly settlement stays balanced', async () => {
    const cron = scalar(paymentSql("SELECT value FROM hangfire.hash WHERE key='recurring-job:payment.trip-settlement-weekly' AND field='Cron'"));
    assert(cron === '0 2 * * 1', `Weekly cron drifted: ${cron}`);
    const expected = Number(scalar(paymentSql(`SELECT sum(amount) FROM operator_ledger_entries WHERE operator_id='${ids.operatorA}' AND trip_id='${ids.tripA}'`)));
    state.tripAExpectedSettlement = expected;
    await triggerJob('settlement-weekly');
    await poll(() => scalar(paymentSql(`SELECT status FROM operator_trip_settlements WHERE id='${state.settlementId}'`)) === 'SETTLED', 'Weekly settlement did not settle');
    const movements = paymentSql(`SELECT type||':'||amount FROM platform_wallet_transactions WHERE reference_id='${state.settlementId}' UNION ALL SELECT type||':'||amount FROM operator_wallet_transactions WHERE reference_id='${state.settlementId}' ORDER BY 1`);
    assert(movements.includes(`CREDIT:${expected}`) && movements.includes(`DEBIT:${expected}`), `Settlement is not balanced: ${movements}`);
  });

  await scenario(12, 'Manual versus weekly terminal race invariant', async () => {
    const charge = await createCharge('BOOKING', ids.bookingRace, ids.tripRace, 90000);
    assert(charge.status === 200, `Race booking charge failed: ${JSON.stringify(charge)}`);
    const complete = await api(gateway, 'POST', `/v1/driver/trips/${ids.tripRace}/complete`, {
      token: state.tokens.driver,
      key: 'day38-complete-trip-race',
    });
    assert(complete.status === 200, `Race Trip completion failed: ${JSON.stringify(complete)}`);
    const raceSettlementId = await poll(() => {
      const value = scalar(paymentSql(`SELECT id FROM operator_trip_settlements WHERE trip_id='${ids.tripRace}'`));
      return value || false;
    }, 'Race settlement marker timeout');
    paymentSql(`UPDATE operator_trip_settlements SET eligible_at=trip_terminal_at WHERE id='${raceSettlementId}'`);
    await triggerJob('settlement-eligibility');
    await poll(() => scalar(paymentSql(`SELECT status FROM operator_trip_settlements WHERE id='${raceSettlementId}'`)) === 'ELIGIBLE', 'Race settlement did not become eligible');

    const manualKey = 'day38-settle-race-manual';
    const [weeklyJobId, manual] = await Promise.all([
      triggerJob('settlement-weekly'),
      api(gateway, 'POST', `/v1/admin/trip-settlements/${raceSettlementId}/settle`, {
        token: state.tokens.system,
        key: manualKey,
      }),
    ]);
    assert(weeklyJobId, 'Weekly race job was not enqueued');
    assert([200, 409].includes(manual.status), `Race returned an unexpected manual response: ${JSON.stringify(manual)}`);
    await poll(() => scalar(paymentSql(`SELECT status FROM operator_trip_settlements WHERE id='${raceSettlementId}'`)) === 'SETTLED', 'Race settlement did not reach SETTLED');

    const sameKeyReplay = await api(gateway, 'POST', `/v1/admin/trip-settlements/${raceSettlementId}/settle`, {
      token: state.tokens.system,
      key: manualKey,
    });
    assert(sameKeyReplay.status === manual.status, `Manual same-key replay changed response: ${JSON.stringify({ manual, sameKeyReplay })}`);
    const differentKeyLoser = await api(gateway, 'POST', `/v1/admin/trip-settlements/${raceSettlementId}/settle`, {
      token: state.tokens.system,
      key: 'day38-settle-race-loser',
    });
    assert(differentKeyLoser.status === 409 && differentKeyLoser.json?.error?.code === 'TRIP_SETTLEMENT_ALREADY_SETTLED', `Race loser contract drifted: ${JSON.stringify(differentKeyLoser)}`);
    assert(count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE reference_id='${raceSettlementId}' AND reference_type='TRIP_SETTLEMENT'`)) === 1, 'Race duplicated OperatorWallet credit');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${raceSettlementId}' AND reference_type='TRIP_SETTLEMENT'`)) === 1, 'Race duplicated PlatformWallet debit');
    assert(count(paymentSql(`SELECT count(*) FROM outbox_events WHERE event_type='payment.trip_settlement.completed' AND payload->>'settlementId'='${raceSettlementId}'`)) === 1, 'Race duplicated settlement Outbox event');
  });

  await scenario(13, 'Insufficient-balance failure metadata and alert dedupe', async () => {
    const settlement = '38000000-0000-4000-8000-000000000091';
    const trip = '38000000-0000-4000-8000-000000000092';
    const agedSettlement = '38000000-0000-4000-8000-000000000094';
    const agedTrip = '38000000-0000-4000-8000-000000000095';
    paymentSql(`
      INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note)
      VALUES
        ('38000000-0000-4000-8000-000000000096','${ids.operatorA}','${trip}','BOOKING_REVENUE',999999999,'BOOKING','38000000-0000-4000-8000-000000000097','38000000-0000-4000-8000-000000000098','Day 38 insufficient fixture'),
        ('38000000-0000-4000-8000-000000000099','${ids.operatorA}','${agedTrip}','BOOKING_REVENUE',1000,'BOOKING','38000000-0000-4000-8000-000000000100','38000000-0000-4000-8000-000000000101','Day 38 aged stuck fixture')
      ON CONFLICT DO NOTHING;
      INSERT INTO operator_trip_settlements (id,operator_id,trip_id,net_amount,trip_terminal_at,eligible_at,status,settlement_failure_count,last_settlement_failure_at,active_failure_code)
      VALUES
        ('${settlement}','${ids.operatorA}','${trip}',999999999,now()-interval '8 days',now()-interval '1 day','ELIGIBLE',0,NULL,NULL),
        ('${agedSettlement}','${ids.operatorA}','${agedTrip}',1000,now()-interval '30 days',now()-interval '22 days','ELIGIBLE',1,now()-interval '1 day','PLATFORM_WALLET_INSUFFICIENT_BALANCE')
      ON CONFLICT (id) DO NOTHING;
      UPDATE platform_wallets SET balance=0;
    `);
    for (let expectedFailures = 1; expectedFailures <= 3; expectedFailures += 1) {
      await triggerJob('settlement-weekly');
      await poll(
        () => Number(scalar(paymentSql(`SELECT settlement_failure_count FROM operator_trip_settlements WHERE id='${settlement}'`))) >= expectedFailures,
        `Failure count did not reach ${expectedFailures}`,
      );
    }
    await triggerJob('settlement-alert');
    await poll(() => redis('EXISTS', `payment:settlement_insufficient:${settlement}`).trim() === '1', 'Insufficient balance alert key missing');
    assert(Number(redis('TTL', `payment:settlement_insufficient:${settlement}`)) > 0, 'Alert dedupe TTL missing');
    await poll(() => redis('EXISTS', `payment:settlement_insufficient:${agedSettlement}`).trim() === '1', 'Aged settlement alert key missing');
    const high = await api(gateway, 'GET', '/v1/admin/trip-settlements?stuckOnly=true&severity=HIGH&page=1&pageSize=100', { token: state.tokens.system });
    assert(high.status === 200, `Stuck settlement API failed: ${JSON.stringify(high)}`);
    const highPayload = JSON.stringify(high.json);
    assert(highPayload.includes(settlement) && highPayload.includes(agedSettlement), 'HIGH severity OR semantics did not include count and age fixtures');

    paymentSql('UPDATE platform_wallets SET balance=2000000000');
    await triggerJob('settlement-weekly');
    await poll(() => scalar(paymentSql(`SELECT status FROM operator_trip_settlements WHERE id='${settlement}'`)) === 'SETTLED', 'Settlement did not recover after PlatformWallet refill');
    const recovery = scalar(paymentSql(`SELECT settlement_failure_count||':'||coalesce(active_failure_code,'NULL')||':'||(failure_resolved_at IS NOT NULL)::text FROM operator_trip_settlements WHERE id='${settlement}'`));
    assert(recovery === '3:NULL:true', `Failure history/recovery markers drifted: ${recovery}`);
    assert(count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE reference_id='${settlement}' AND reference_type='TRIP_SETTLEMENT'`)) === 1, 'Recovery duplicated OperatorWallet movement');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${settlement}' AND reference_type='TRIP_SETTLEMENT'`)) === 1, 'Recovery duplicated PlatformWallet movement');
    const stuckAfterRecovery = await api(gateway, 'GET', '/v1/admin/trip-settlements?stuckOnly=true&page=1&pageSize=100', { token: state.tokens.system });
    assert(!JSON.stringify(stuckAfterRecovery.json).includes(settlement), 'Recovered settlement remained in stuck filter');
  });

  await scenario(14, 'Concurrent negative-balance guard', async () => {
    const before = Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorB}'`)));
    const body = { type: 'DEBIT', amount: before, note: 'Day 38 concurrent guard' };
    const responses = await Promise.all([1, 2].map((n) => api(gateway, 'POST', `/v1/admin/operators/${ids.operatorB}/wallet/adjust`, { token: state.tokens.system, key: `day38-negative-race-${n}`, body })));
    assert(responses.filter((x) => x.status === 200).length === 1, `Expected one debit winner: ${JSON.stringify(responses)}`);
    assert(responses.filter((x) => x.status === 409 || x.status === 402).length === 1, 'Expected one insufficient-balance loser');
    assert(Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorB}'`))) === 0, 'OperatorWallet became negative or debit was lost');
  });

  await scenario(15, 'Late ledger changes are not frozen in pending summaries', async () => {
    const wallet = await api(gateway, 'GET', '/v1/operator/wallet', { token: state.tokens.operatorA });
    assert(wallet.status === 200 && typeof wallet.json?.data?.pendingHoldAmount === 'number', `Operator wallet summary missing live pending hold: ${JSON.stringify(wallet)}`);

    const charge = await createCharge('BOOKING', ids.bookingLateRefund, ids.tripLateRefund, 40000);
    assert(charge.status === 200, `Late-refund booking charge failed: ${JSON.stringify(charge)}`);
    const complete = await api(gateway, 'POST', `/v1/driver/trips/${ids.tripLateRefund}/complete`, {
      token: state.tokens.driver,
      key: 'day38-complete-trip-late-refund',
    });
    assert(complete.status === 200, `Late-refund Trip completion failed: ${JSON.stringify(complete)}`);
    const settlementId = await poll(() => {
      const value = scalar(paymentSql(`SELECT id FROM operator_trip_settlements WHERE trip_id='${ids.tripLateRefund}'`));
      return value || false;
    }, 'Late-refund settlement marker timeout');
    paymentSql(`UPDATE operator_trip_settlements SET eligible_at=trip_terminal_at WHERE id='${settlementId}'`);
    await triggerJob('settlement-eligibility');
    await poll(() => scalar(paymentSql(`SELECT status||':'||net_amount FROM operator_trip_settlements WHERE id='${settlementId}'`)) === 'ELIGIBLE:40000', 'Late-refund fixture did not become ELIGIBLE with original net');

    const refund = await api(payment, 'POST', '/internal/v1/wallet/refund', {
      internalToken: await internalJwt(),
      key: 'day38-refund-late-booking',
      body: { userId: ids.passenger, amount: 40000, referenceType: 'BOOKING_REFUND', referenceId: ids.bookingLateRefund },
    });
    assert(refund.status === 200, `Late refund failed: ${JSON.stringify(refund)}`);
    const operatorBalanceBefore = Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorA}'`)));
    const platformBalanceBefore = Number(scalar(paymentSql('SELECT balance FROM platform_wallets')));
    await triggerJob('settlement-weekly');
    await poll(() => scalar(paymentSql(`SELECT status||':'||net_amount FROM operator_trip_settlements WHERE id='${settlementId}'`)) === 'CANCELLED:0', 'Weekly settlement did not recompute late refund to net zero');
    assert(Number(scalar(paymentSql(`SELECT balance FROM operator_wallets WHERE operator_id='${ids.operatorA}'`))) === operatorBalanceBefore, 'Net-zero settlement moved OperatorWallet');
    assert(Number(scalar(paymentSql('SELECT balance FROM platform_wallets'))) === platformBalanceBefore, 'Net-zero settlement moved PlatformWallet');
    assert(count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE reference_id='${settlementId}'`)) === 0, 'Net-zero settlement created OperatorWallet transaction');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${settlementId}'`)) === 0, 'Net-zero settlement created PlatformWallet transaction');
    assert(count(paymentSql(`SELECT count(*) FROM outbox_events WHERE event_type='payment.trip_settlement.completed' AND payload->>'settlementId'='${settlementId}'`)) === 0, 'Net-zero settlement published completed event');
    assert(count(paymentSql('SELECT count(*) FROM operator_wallets WHERE balance < 0')) === 0, 'Negative OperatorWallet exists');
  });

  await scenario(16, 'OperatorWallet subscription money path', async () => {
    assert(count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND reference_id='${state.subscriptionPaymentId}' AND type='DEBIT'`)) === 1, 'Subscription OperatorWallet debit missing');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND reference_id='${state.subscriptionPaymentId}' AND type='CREDIT'`)) === 1, 'Subscription PlatformWallet credit missing');
    assert(count(identitySql(`SELECT count(*) FROM operator_subscriptions WHERE operator_id='${ids.operatorA}' AND status='ACTIVE' AND plan_id='${ids.paidPlan}'`)) === 1, 'Identity subscription not activated');
  });

  await scenario(17, 'Subscription auth and validation', async () => {
    const validWallet = { planId: ids.paidPlan, billingPeriod: 'MONTHLY', paymentMethod: 'WALLET', returnUrl: null };
    const noKey = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorB, body: validWallet });
    assert(noKey.status === 422 && noKey.json?.error?.code === 'IDEMPOTENCY_KEY_REQUIRED', 'Missing idempotency key error drifted');
    const staff = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.staff, key: 'day38-staff-forbidden', body: validWallet });
    assert(staff.status === 403, 'Operator staff was allowed to upgrade subscription');
    const passenger = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.passenger, key: 'day38-passenger-forbidden', body: validWallet });
    assert(passenger.status === 403, 'Passenger was allowed to upgrade subscription');
    const invalidMethod = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorB, key: 'day38-invalid-method', body: { ...validWallet, paymentMethod: 'INVALID' } });
    assert(invalidMethod.status === 422, 'Invalid subscription payment method was accepted');
    const missingReturn = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorB, key: 'day38-vnpay-return-missing', body: { ...validWallet, paymentMethod: 'VNPAY' } });
    assert(missingReturn.status === 422, 'VNPay without returnUrl was accepted');
    const walletReturn = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorB, key: 'day38-wallet-return-forbidden', body: { ...validWallet, returnUrl: 'https://operator.day38.test/return' } });
    assert(walletReturn.status === 422, 'WALLET with returnUrl was accepted');

    paymentSql(`UPDATE operator_wallets SET balance=0 WHERE operator_id='${ids.operatorB}'`);
    const paymentCountBefore = count(paymentSql(`SELECT count(*) FROM payments WHERE operator_id='${ids.operatorB}'`));
    const insufficient = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorB, key: 'day38-wallet-insufficient', body: validWallet });
    assert(insufficient.status === 402 && insufficient.json?.error?.code === 'WALLET_INSUFFICIENT_BALANCE', `Insufficient balance contract drifted: ${JSON.stringify(insufficient)}`);
    assert(count(paymentSql(`SELECT count(*) FROM payments WHERE operator_id='${ids.operatorB}'`)) === paymentCountBefore, 'Insufficient subscription charge persisted Payment');
    assert(count(identitySql(`SELECT count(*) FROM operator_subscriptions WHERE id='${ids.subscriptionB}' AND status='ACTIVE' AND plan_id='${ids.starterPlan}'`)) === 1, 'Insufficient charge changed subscription');
    assert(count(identitySql(`SELECT count(*) FROM subscription_upgrade_attempts WHERE operator_id='${ids.operatorB}' AND status='FAILED'`)) === 1, 'Deterministic payment failure did not close upgrade attempt');
    paymentSql(`UPDATE operator_wallets SET balance=1000000 WHERE operator_id='${ids.operatorB}'`);
  });

  await scenario(18, 'Subscription replay and single charge', async () => {
    const replay = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorA, key: 'day38-wallet-subscription-a', body: { planId: ids.paidPlan, billingPeriod: 'MONTHLY', paymentMethod: 'WALLET', returnUrl: null } });
    assert(replay.status === 200 && replay.json?.data?.paymentId === state.subscriptionPaymentId, 'Subscription replay response drifted');
    assert(count(paymentSql(`SELECT count(*) FROM payments WHERE id='${state.subscriptionPaymentId}'`)) === 1, 'Subscription replay duplicated payment');
    const mismatch = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorA, key: 'day38-wallet-subscription-a', body: { planId: ids.paidPlan, billingPeriod: 'YEARLY', paymentMethod: 'WALLET', returnUrl: null } });
    assert(mismatch.status === 422 && mismatch.json?.error?.code === 'IDEMPOTENCY_KEY_MISMATCH', `Subscription idempotency mismatch drifted: ${JSON.stringify(mismatch)}`);

    const debitBefore = count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE operator_id='${ids.operatorA}' AND reference_type='SUBSCRIPTION_PAYMENT'`));
    const concurrentBody = { planId: ids.paidPlan, billingPeriod: 'MONTHLY', paymentMethod: 'WALLET', returnUrl: null };
    const concurrent = await Promise.all([
      api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorA, key: 'day38-wallet-concurrent-a', body: concurrentBody }),
      api(gateway, 'POST', '/v1/operator/subscription/upgrade', { token: state.tokens.operatorA, key: 'day38-wallet-concurrent-b', body: concurrentBody }),
    ]);
    assert(concurrent.filter((result) => result.status === 200).length === 1, `Concurrent subscription charge winner count drifted: ${JSON.stringify(concurrent)}`);
    assert(concurrent.filter((result) => result.status === 409 && result.json?.error?.code === 'SUBSCRIPTION_PAYMENT_PENDING').length === 1, `Concurrent subscription loser contract drifted: ${JSON.stringify(concurrent)}`);
    assert(count(paymentSql(`SELECT count(*) FROM operator_wallet_transactions WHERE operator_id='${ids.operatorA}' AND reference_type='SUBSCRIPTION_PAYMENT'`)) === debitBefore + 1, 'Concurrent subscription requests double charged OperatorWallet');
  });

  await scenario(19, 'Canonical invoice trigger is method-independent', async () => {
    const eventType = scalar(paymentSql(`SELECT event_type FROM outbox_events WHERE payload->>'paymentId'='${state.subscriptionPaymentId}' AND event_type='payment.subscription.payment_succeeded' LIMIT 1`));
    assert(eventType === 'payment.subscription.payment_succeeded', 'Canonical subscription invoice trigger missing');
    assert(count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${state.subscriptionPaymentId}'`)) === 1, 'Payment has more than one invoice');

    const platformCreditsBefore = count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND reference_id IN (SELECT id FROM payments WHERE operator_id='${ids.operatorB}')`));
    const vnpay = await api(gateway, 'POST', '/v1/operator/subscription/upgrade', {
      token: state.tokens.operatorB,
      key: 'day38-vnpay-subscription-b',
      body: { planId: ids.paidPlan, billingPeriod: 'MONTHLY', paymentMethod: 'VNPAY', returnUrl: 'https://operator.day38.test/return' },
    });
    assert(vnpay.status === 202 && vnpay.json?.data?.paymentRedirectUrl, `VNPay subscription create failed: ${JSON.stringify(vnpay)}`);
    state.vnpaySubscriptionPaymentId = vnpay.json.data.paymentId;
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND reference_id IN (SELECT id FROM payments WHERE operator_id='${ids.operatorB}')`)) === platformCreditsBefore, 'VNPay credited PlatformWallet before callback');
    const callback = await confirmSubscriptionVnPay(vnpay.json.data.paymentRedirectUrl, '3800000019');
    assert(callback.status === 200 && (callback.json?.rspCode ?? callback.json?.RspCode) === '00', `VNPay subscription callback failed: ${JSON.stringify(callback)}`);
    await poll(() => count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${state.vnpaySubscriptionPaymentId}'`)) === 1, 'VNPay subscription did not trigger canonical invoice');
    await poll(() => count(identitySql(`SELECT count(*) FROM operator_subscriptions WHERE id='${ids.subscriptionB}' AND status='ACTIVE' AND plan_id='${ids.paidPlan}'`)) === 1, 'VNPay subscription did not activate Identity');
    assert(count(paymentSql(`SELECT count(*) FROM outbox_events WHERE event_type='payment.subscription.payment_succeeded' AND payload->>'paymentId' IN ('${state.subscriptionPaymentId}','${state.vnpaySubscriptionPaymentId}')`)) === 2, 'WALLET/VNPay did not share canonical event contract');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND reference_id='${state.vnpaySubscriptionPaymentId}'`)) === 1, 'VNPay subscription platform credit duplicated');
  });

  await scenario(20, 'Invoice uniqueness, counter, and PDF content', async () => {
    const invoiceNumber = scalar(paymentSql(`SELECT invoice_number FROM invoices WHERE id='${state.invoiceId}'`));
    assert(/^VR-INV-\d{6}-\d{6}$/.test(invoiceNumber), `Invoice number invalid: ${invoiceNumber}`);
    assert(count(paymentSql(`SELECT count(*) FROM invoice_number_counters WHERE period_key=substring('${invoiceNumber}' from 8 for 6) AND last_value BETWEEN 1 AND 999999`)) === 1, 'Invoice counter missing/out of range');
    assert(count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${state.subscriptionPaymentId}'`)) === 1, 'UNIQUE(payment_id) invariant failed');
    const pdfSize = Number(run('docker', ['exec', containers.payment, 'stat', '-c', '%s', `/tmp/day38-invoices/invoices/${ids.operatorA}/${state.invoiceId}.pdf`]));
    assert(pdfSize > 1000, `PDF unexpectedly small: ${pdfSize}`);
  });

  await scenario(21, 'Invoice stale/attempt terminal invariants', async () => {
    const constraint = scalar(paymentSql("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='chk_invoices_pdf_attempts'"));
    assert(constraint.includes('<= 5') && constraint.includes('>= 0'), 'Five-attempt DB guard missing');
    const retryId = '38000000-0000-4000-8000-000000000092';
    paymentSql(`INSERT INTO invoices (id,invoice_number,operator_id,operator_subscription_id,payment_id,amount,period_from,period_to,status,pdf_generation_status,pdf_generation_attempts,metadata) SELECT '${retryId}','VR-INV-209901-999997','${ids.operatorA}','${ids.subscriptionA}',id,amount,now(),now()+interval '1 month','DRAFT','PENDING',0,context FROM payments WHERE id='${state.bookingPaymentId}' ON CONFLICT (id) DO NOTHING`);
    for (let attempt = 1; attempt <= 5; attempt += 1) {
      if (attempt === 1) {
        await triggerJob('invoice-pdf', retryId);
      } else {
        paymentSql(`UPDATE invoices SET pdf_generation_next_retry_at=now()-interval '1 second' WHERE id='${retryId}'`);
        await triggerJob('invoice-reconciliation');
      }
      await poll(() => scalar(paymentSql(`SELECT pdf_generation_status||':'||pdf_generation_attempts FROM invoices WHERE id='${retryId}'`)) === `FAILED:${attempt}`, `Invoice retry attempt ${attempt} did not fail deterministically`);
      if (attempt < 5) {
        const expectedDelay = [1, 5, 15, 30][attempt - 1];
        const delaySeconds = Number(scalar(paymentSql(`SELECT extract(epoch FROM (pdf_generation_next_retry_at-updated_at))::int FROM invoices WHERE id='${retryId}'`)));
        assert(delaySeconds >= expectedDelay * 60 - 5 && delaySeconds <= expectedDelay * 60 + 5, `Invoice backoff ${attempt} drifted: ${delaySeconds}s`);
      } else {
        assert(scalar(paymentSql(`SELECT coalesce(pdf_generation_next_retry_at::text,'NULL') FROM invoices WHERE id='${retryId}'`)) === 'NULL', 'Attempt 5 retained next retry');
      }
    }

    const staleId = '38000000-0000-4000-8000-000000000093';
    paymentSql(`INSERT INTO invoices (id,invoice_number,operator_id,operator_subscription_id,payment_id,amount,period_from,period_to,status,pdf_generation_status,pdf_generation_attempts,pdf_generation_started_at,metadata) SELECT '${staleId}','VR-INV-209901-999998','${ids.operatorA}','${ids.subscriptionA}',id,amount,now(),now()+interval '1 month','DRAFT','PROCESSING',5,now()-interval '16 minutes',context FROM payments WHERE id='${state.parcelPaymentId}' ON CONFLICT (id) DO NOTHING`);
    await triggerJob('invoice-reconciliation');
    await poll(() => scalar(paymentSql(`SELECT pdf_generation_status||':'||coalesce(pdf_generation_next_retry_at::text,'NULL') FROM invoices WHERE id='${staleId}'`)) === 'FAILED:NULL', 'Stale attempt 5 did not become terminal FAILED');
  });

  await scenario(22, 'Admin invoice retry idempotency and guards', async () => {
    const retryPaymentId = '38000000-0000-4000-8000-000000000094';
    const retryInvoiceId = '38000000-0000-4000-8000-000000000095';
    paymentSql(`
      INSERT INTO payments (id,reference_type,reference_id,operator_id,amount,method,status,idempotency_key,succeeded_at,context)
      SELECT '${retryPaymentId}',reference_type,'${retryPaymentId}',operator_id,amount,'WALLET','SUCCEEDED','day38-admin-retry-payment',now(),context
      FROM payments WHERE id='${state.subscriptionPaymentId}' ON CONFLICT (id) DO NOTHING;
      INSERT INTO invoices (id,invoice_number,operator_id,operator_subscription_id,payment_id,amount,period_from,period_to,status,pdf_generation_status,pdf_generation_attempts,pdf_generation_next_retry_at,pdf_generation_last_error,metadata)
      SELECT '${retryInvoiceId}','VR-INV-209901-999996',operator_id,operator_subscription_id,'${retryPaymentId}',amount,period_from,period_to,'DRAFT','FAILED',1,now()+interval '30 minutes','E2E_RETRY',metadata
      FROM invoices WHERE id='${state.invoiceId}' ON CONFLICT (id) DO NOTHING;
    `);
    run('docker', ['exec', containers.payment, 'sh', '-c', 'touch /tmp/day38-invoices/.fail-next-upload']);
    const raced = await Promise.all([
      api(gateway, 'POST', `/v1/admin/invoices/${retryInvoiceId}/retry`, { token: state.tokens.system, key: 'day38-admin-retry-a' }),
      api(gateway, 'POST', `/v1/admin/invoices/${retryInvoiceId}/retry`, { token: state.tokens.systemB, key: 'day38-admin-retry-b' }),
    ]);
    const winner = raced.find((result) => result.status === 202);
    const loser = raced.find((result) => result.status === 409);
    assert(winner && loser?.json?.error?.code === 'INVOICE_RETRY_ALREADY_PENDING', `Admin retry race drifted: ${JSON.stringify(raced)}`);
    const winnerKey = raced[0].status === 202 ? 'day38-admin-retry-a' : 'day38-admin-retry-b';
    const winnerToken = raced[0].status === 202 ? state.tokens.system : state.tokens.systemB;
    const sameKey = await api(gateway, 'POST', `/v1/admin/invoices/${retryInvoiceId}/retry`, { token: winnerToken, key: winnerKey });
    assert(sameKey.status === 202, `Admin retry same-key replay drifted: ${JSON.stringify(sameKey)}`);
    await poll(() => scalar(paymentSql(`SELECT pdf_generation_status||':'||pdf_generation_attempts FROM invoices WHERE id='${retryInvoiceId}'`)) === 'FAILED:2', 'Admin retry worker did not consume exactly one attempt');

    const issued = await api(gateway, 'POST', `/v1/admin/invoices/${state.invoiceId}/retry`, { token: state.tokens.system, key: 'day38-issued-retry' });
    assert(issued.status === 409 && issued.json?.error?.code === 'INVOICE_RETRY_NOT_ALLOWED', `Issued invoice retry was accepted: ${JSON.stringify(issued)}`);
    const terminal = await api(gateway, 'POST', '/v1/admin/invoices/38000000-0000-4000-8000-000000000092/retry', { token: state.tokens.system, key: 'day38-terminal-retry' });
    assert(terminal.status === 409 && terminal.json?.error?.code === 'INVOICE_RETRY_NOT_ALLOWED', 'Attempt-5 invoice retry was accepted');
  });

  await scenario(23, 'Invoice download tenant authorization, TTL, and rate limit', async () => {
    const first = await api(gateway, 'GET', `/v1/operator/invoices/${state.invoiceId}/download`, { token: state.tokens.operatorA });
    const second = await api(gateway, 'GET', `/v1/operator/invoices/${state.invoiceId}/download`, { token: state.tokens.operatorA });
    assert(first.status === 200 && second.status === 200, 'Invoice download failed');
    assert(first.json?.data?.downloadUrl !== second.json?.data?.downloadUrl, 'Signed URL was reused instead of regenerated');
    const ttlMs = Date.parse(first.json.data.expiresAt) - Date.now();
    assert(ttlMs > 0 && ttlMs <= 60 * 60 * 1000 + 5000, `Signed URL TTL invalid: ${ttlMs}`);
    const crossTenant = await api(gateway, 'GET', `/v1/operator/invoices/${state.invoiceId}/download`, { token: state.tokens.operatorB });
    assert(crossTenant.status === 404, 'Cross-tenant invoice download leaked existence');
    const burst = [];
    for (let i = 0; i < 9; i += 1) burst.push(await api(gateway, 'GET', `/v1/operator/invoices/${state.invoiceId}/download`, { token: state.tokens.operatorA }));
    assert(burst.some((x) => x.status === 429), 'Per-user/invoice download rate limit did not engage');
  });

  await scenario(24, 'Operator/Admin read API tenant isolation', async () => {
    for (const pathname of ['/v1/operator/wallet', '/v1/operator/wallet/transactions?page=1&pageSize=10', '/v1/operator/ledger?page=1&pageSize=10', '/v1/operator/trip-settlements?page=1&pageSize=10', '/v1/operator/invoices?page=1&pageSize=10']) {
      const response = await api(gateway, 'GET', pathname, { token: state.tokens.operatorA });
      assert(response.status === 200 && response.json?.success === true, `Operator API failed: ${pathname}`);
    }
    const other = await api(gateway, 'GET', '/v1/operator/invoices?page=1&pageSize=100', { token: state.tokens.operatorB });
    assert(other.status === 200 && !JSON.stringify(other.json).includes(state.invoiceId), 'Operator B read Operator A invoice');
    const admin = await api(gateway, 'GET', '/v1/admin/platform-wallet', { token: state.tokens.system });
    assert(admin.status === 200, 'Admin platform wallet read failed');
  });

  await scenario(25, 'Notification/email and PII redaction', async () => {
    await poll(() => count(notificationSql(`SELECT count(*) FROM notifications WHERE type='INVOICE_ISSUED' AND data->>'invoiceId'='${state.invoiceId}'`)) === 1, 'Invoice notification missing', 120_000);
    assert(count(notificationSql(`SELECT count(*) FROM email_deliveries WHERE template_key='INVOICE_NOTICE' AND status='SENT'`)) >= 1, 'Invoice email delivery missing');
    await poll(() => count(notificationSql(`SELECT count(*) FROM notification_deliveries d JOIN notifications n ON n.id=d.notification_id WHERE n.type='INVOICE_ISSUED' AND n.data->>'invoiceId'='${state.invoiceId}' AND d.status='SENT' AND d.provider_message_id IS NOT NULL`)) === 1, 'Invoice push delivery missing');
    const data = scalar(notificationSql(`SELECT data::text FROM notifications WHERE type='INVOICE_ISSUED' AND data->>'invoiceId'='${state.invoiceId}'`));
    assert(data.includes('invoiceWebUrl') && !data.includes('downloadApiUrl') && !data.includes('X-Goog-Signature'), 'Notification leaked protected/signed download URL');
    await poll(() => count(notificationSql(`SELECT count(*) FROM notifications WHERE type='WALLET_CREDITED' AND data->>'settlementId'='${state.settlementId}' AND (data->>'netAmount')::bigint=${state.tripAExpectedSettlement}`)) === 1, 'Settlement notification missing canonical netAmount');

    const invoiceEvent = JSON.parse(scalar(paymentSql(`SELECT payload::text FROM outbox_events WHERE event_type='payment.invoice.issued' AND payload->>'invoiceId'='${state.invoiceId}' LIMIT 1`)));
    const beforeReplay = count(notificationSql(`SELECT count(*) FROM notifications WHERE type='INVOICE_ISSUED' AND data->>'invoiceId'='${state.invoiceId}'`));
    await publish('payment.invoice.issued', invoiceEvent, invoiceEvent.eventId);
    await new Promise((resolve) => setTimeout(resolve, 1500));
    assert(count(notificationSql(`SELECT count(*) FROM notifications WHERE type='INVOICE_ISSUED' AND data->>'invoiceId'='${state.invoiceId}'`)) === beforeReplay, 'Invoice event replay duplicated notification');
    const logs = run('docker', ['logs', containers.notification, '--since', '10m']);
    assert(!logs.includes('billing-a@day38.test') && !logs.includes('X-Goog-Signature'), 'Structured logs leaked email or signed URL');
  });

  await scenario(26, 'End-to-end reconciliation and replay audit', async () => {
    const legacyContextPaymentId = '38000000-0000-4000-8000-000000000096';
    const legacyRevenuePaymentId = '38000000-0000-4000-8000-000000000097';
    const legacyInvoicePaymentId = '38000000-0000-4000-8000-000000000098';
    const phaseAPaymentId = '38000000-0000-4000-8000-000000000099';
    const phaseATxnRef = 'day38-legacy-phase-a';
    paymentSql(`
      INSERT INTO payments (id,reference_type,reference_id,user_id,amount,method,status,idempotency_key,succeeded_at,context)
      VALUES ('${legacyContextPaymentId}','BOOKING','${legacyContextPaymentId}','${ids.passenger}',10000,'VNPAY','SUCCEEDED','day38-legacy-context','2000-01-01T00:00:00Z','{}'::jsonb)
      ON CONFLICT (id) DO NOTHING;
      INSERT INTO payments (id,reference_type,reference_id,user_id,amount,method,status,idempotency_key,succeeded_at,context,created_at,updated_at)
      SELECT '${legacyRevenuePaymentId}',reference_type,'${legacyRevenuePaymentId}',user_id,amount,method,'SUCCEEDED','day38-legacy-revenue','2000-01-01T00:00:00Z',context,'2000-01-01T00:00:00Z','2000-01-01T00:00:00Z'
      FROM payments WHERE id='${state.bookingPaymentId}' ON CONFLICT (id) DO NOTHING;
      INSERT INTO payments (id,reference_type,reference_id,operator_id,amount,method,status,idempotency_key,succeeded_at,context)
      SELECT '${legacyInvoicePaymentId}',reference_type,'${legacyInvoicePaymentId}',operator_id,amount,method,'SUCCEEDED','day38-legacy-invoice','2000-01-01T00:00:00Z',context
      FROM payments WHERE id='${state.subscriptionPaymentId}' ON CONFLICT (id) DO NOTHING;
      INSERT INTO payments
        (id,reference_type,reference_id,user_id,amount,method,status,vnpay_txn_ref,idempotency_key,payment_redirect_url,context,created_at,updated_at)
      SELECT '${phaseAPaymentId}','BOOKING','${ids.legacyBooking}',user_id,80000,'VNPAY','PENDING_REDIRECT','${phaseATxnRef}',
        'day38-legacy-phase-a','https://sandbox.vnpayment.vn/paymentv2/vpcpay.html','{}'::jsonb,
        '2000-01-01T00:00:00Z','2000-01-01T00:00:00Z'
      FROM payments WHERE id='${state.bookingGroupPaymentId}' ON CONFLICT (id) DO NOTHING;
    `);

    const platformBeforePhaseA = Number(scalar(paymentSql('SELECT balance FROM platform_wallets')));
    const legacyRedirect = new URL(scalar(paymentSql(`SELECT payment_redirect_url FROM payments WHERE id='${state.bookingGroupPaymentId}'`)));
    legacyRedirect.searchParams.set('vnp_Amount', '8000000');
    legacyRedirect.searchParams.set('vnp_TxnRef', phaseATxnRef);
    const phaseAIpn = await confirmBookingVnPay(legacyRedirect.toString(), '3800000026');
    const phaseAReplay = await confirmBookingVnPay(legacyRedirect.toString(), '3800000026');
    assert(phaseAIpn.status === 200 && (phaseAIpn.json?.rspCode ?? phaseAIpn.json?.RspCode) === '00', `Legacy Phase-A callback failed: ${JSON.stringify(phaseAIpn)}`);
    assert(phaseAReplay.status === 200 && (phaseAReplay.json?.rspCode ?? phaseAReplay.json?.RspCode) === '00', 'Legacy Phase-A callback replay failed');
    assert(count(paymentSql(`SELECT count(*) FROM payments WHERE id='${phaseAPaymentId}' AND status='SUCCEEDED' AND context='{}'::jsonb AND context_reconciliation_required=true`)) === 1, 'Legacy Phase-A payment did not settle into reconciliation state');
    assert(Number(scalar(paymentSql('SELECT balance FROM platform_wallets'))) === platformBeforePhaseA + 80000, 'Legacy Phase-A callback platform credit mismatch');
    assert(count(paymentSql(`SELECT count(*) FROM platform_wallet_transactions WHERE reference_id='${ids.legacyBooking}' AND amount=80000`)) === 1, 'Legacy Phase-A callback replay duplicated platform hold');
    assert(count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE source_event_id='${phaseAPaymentId}'`)) === 0, 'Legacy Phase-A callback wrote ledger before context hydration');

    await triggerJob('context-backfill');
    await poll(() => count(paymentSql(`SELECT count(*) FROM payments WHERE id='${legacyContextPaymentId}' AND context_reconciliation_required=true AND context='{}'::jsonb`)) === 1, 'Unknown legacy payment was not quarantined by context backfill');
    await poll(() => count(paymentSql(`SELECT count(*) FROM payments WHERE id='${phaseAPaymentId}' AND context<>'{}'::jsonb AND context_reconciliation_required=false`)) === 1, 'Legacy Phase-A context was not hydrated from Booking');
    await triggerJob('revenue-backfill');
    await triggerJob('invoice-backfill');
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE source_event_id='${legacyRevenuePaymentId}'`)) === 1, 'Legacy revenue ledger backfill missing deterministic source row');
    await poll(() => count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE source_event_id='${phaseAPaymentId}' AND reference_id='${ids.legacyBooking}' AND amount=80000`)) === 1, 'Legacy Phase-A revenue ledger backfill missing');
    assert(Number(scalar(paymentSql('SELECT balance FROM platform_wallets'))) === platformBeforePhaseA + 80000, 'Legacy revenue backfill credited PlatformWallet twice');
    await poll(() => count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${legacyInvoicePaymentId}'`)) === 1, 'Legacy subscription invoice backfill missing');
    await poll(() => scalar(paymentSql(`SELECT status FROM invoices WHERE payment_id='${legacyInvoicePaymentId}'`)) === 'ISSUED', 'Legacy subscription invoice PDF was not issued', 120_000);
    const legacyInvoiceId = scalar(paymentSql(`SELECT id FROM invoices WHERE payment_id='${legacyInvoicePaymentId}'`));
    await poll(() => count(notificationSql(`SELECT count(*) FROM notifications WHERE type='INVOICE_ISSUED' AND data->>'invoiceId'='${legacyInvoiceId}'`)) === 1, 'Legacy subscription invoice notification missing', 120_000);

    const before = {
      ledger: count(paymentSql('SELECT count(*) FROM operator_ledger_entries')),
      invoices: count(paymentSql('SELECT count(*) FROM invoices')),
      notifications: count(notificationSql('SELECT count(*) FROM notifications')),
    };
    await publish('trip.trip.completed', { eventId: '38000000-0000-4000-8000-000000000084', occurredAt: new Date().toISOString(), tripId: ids.tripA, operatorId: ids.operatorA, terminalAt: new Date().toISOString(), hasSubstitution: false }, '38000000-0000-4000-8000-000000000084');
    await triggerJob('context-backfill');
    await triggerJob('revenue-backfill');
    await triggerJob('invoice-backfill');
    await new Promise((resolve) => setTimeout(resolve, 2500));
    assert(count(paymentSql(`SELECT count(*) FROM operator_trip_settlements WHERE trip_id='${ids.tripA}'`)) === 1, 'Replay duplicated settlement');
    assert(count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${state.subscriptionPaymentId}'`)) === 1, 'Backfill duplicated invoice');
    assert(count(paymentSql(`SELECT count(*) FROM invoices WHERE payment_id='${legacyInvoicePaymentId}'`)) === 1, 'Invoice backfill replay duplicated legacy invoice');
    assert(count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE source_event_id='${legacyRevenuePaymentId}'`)) === 1, 'Revenue backfill replay duplicated ledger');
    assert(count(paymentSql(`SELECT count(*) FROM operator_ledger_entries WHERE source_event_id='${phaseAPaymentId}'`)) === 1, 'Phase-A revenue backfill replay duplicated ledger');
    assert(count(paymentSql('SELECT count(*) FROM operator_ledger_entries')) === before.ledger, 'Replay duplicated ledger');
    assert(count(notificationSql('SELECT count(*) FROM notifications')) === before.notifications, 'Replay duplicated notification');
    assert(count(paymentSql("SELECT count(*) FROM outbox_events WHERE status <> 'PUBLISHED' AND created_at < now()-interval '30 seconds'")) === 0, 'Payment Outbox has stale unpublished events');
    const ready = await api(payment, 'GET', '/internal/v1/payments/context-readiness', { internalToken: await internalJwt() });
    const readiness = ready.json?.data ?? ready.json;
    assert(ready.status === 200 && readiness?.readyForPhaseB === true, `Payment context readiness failed: ${JSON.stringify(ready)}`);
    assert(readiness.pendingRedirectWithoutContext === 0 && readiness.succeededWithoutContext === 0, 'Phase-B readiness still has unresolved legacy payments');
    assert(readiness.quarantined >= 1, 'Readiness did not report quarantined legacy payment');
  });

  const gates = {
    'seed/bootstrap': [1],
    'legacy-upgrade-backfill': [1, 26],
    'payment-context': [2, 3, 4],
    'platform-hold-ledger': [2, 5, 6],
    'trip-terminal-marker': [7, 8],
    'eligibility-weekly-settlement': [10, 11],
    'insufficient-balance-recovery': [13],
    'operator-wallet-subscription': [16, 17, 18],
    'invoice-pdf-retry': [9, 19, 20, 21, 22, 23],
    'operator-admin-api': [14, 15, 24],
    'notification-email-redaction': [25],
    'race-idempotency': [3, 12, 14, 18, 22],
    'database-reconciliation': [26],
  };
  for (const [gate, scenarios] of Object.entries(gates)) {
    assert(scenarios.every((n) => results.some((r) => r.scenario === `E2E-${String(n).padStart(2, '0')}` && r.passed)), `Gate ${gate} has incomplete scenarios`);
    console.log(`${gate} PASS`);
  }
}

let failed;
try {
  await runAcceptance();
} catch (error) {
  failed = error;
  results.push({ scenario: 'HARNESS', name: error instanceof Error ? error.message : String(error), passed: false });
  console.error(error instanceof Error ? error.stack : error);
  if (!useDev) {
    for (const service of ['identity', 'trip', 'payment', 'notification', 'gateway']) {
      try {
        const diagnostic = run('docker', ['logs', `day38-e2e-${service}`, '--tail', '120']);
        console.error(`DIAGNOSTIC ${service}\n${diagnostic}`);
      } catch {
        // A service may not have reached container creation; cleanup still runs below.
      }
    }
  }
} finally {
  if (!useDev) {
    try {
      run('docker', [...compose, 'down', '-v', '--remove-orphans']);
      console.log('cleanup PASS');
    } catch (error) {
      failed ??= error;
      results.push({ scenario: 'CLEANUP', name: String(error), passed: false });
    }
  } else {
    console.log('cleanup PASS (development stack preserved by explicit opt-in)');
  }
}

console.log(JSON.stringify({ suite: 'day38-invoice-settlement-e2e', scenariosPassed: results.filter((x) => x.passed).length, results }, null, 2));
process.exitCode = failed || results.length < 26 || results.some((x) => !x.passed) ? 1 : 0;
