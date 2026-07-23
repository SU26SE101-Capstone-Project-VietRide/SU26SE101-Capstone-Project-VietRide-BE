import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const noBuild = process.argv.includes('--no-build');
const reuseImages = process.argv.includes('--reuse-images') || process.env.E2E_REUSE_IMAGES === '1';
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
const e2eEnv = {
  POSTGRES_PORT: '55437',
  REDIS_PORT: '56379',
  RABBITMQ_PORT: '55672',
  RABBITMQ_MGMT_PORT: '55673',
  IDENTITY_PORT: '55001',
  TRIP_PORT: '55002',
  BOOKING_PORT: '55003',
  PAYMENT_PORT: '55004',
  PARCEL_PORT: '55005',
  NOTIFICATION_PORT: '55012',
  GATEWAY_PORT: '55300',
};
const gateway = 'http://localhost:55300';
const postgresUser = process.env.POSTGRES_USER ?? 'vietride';
const id = (suffix) => `43000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
const ids = {
  operator: id(1),
  admin: id(2),
  assistant: id(3),
  driver: id(4),
  passenger: id(5),
  origin: id(101),
  destination: id(102),
  route: id(103),
  vehicleType: id(104),
  vehicle: id(105),
  trip: id(106),
  parcelAdditional: id(201),
  parcelRefund: id(202),
  cargoAdditional: id(301),
  cargoRefund: id(302),
  originalPayment: id(401),
  platformWallet: id(402),
};

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...e2eEnv, ...options.env },
    stdio: options.stdio ?? ['ignore', 'pipe', 'pipe'],
    maxBuffer: 32 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  }
  return result.stdout?.trim() ?? '';
}

function sql(database, schema, statement) {
  return run('docker', [
    'exec',
    'day37-e2e-postgres',
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
}

function scalar(value) {
  return String(value).split(/\r?\n/u).filter(Boolean).at(-1)?.trim() ?? '';
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
      .split(/\r?\n/u)
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

async function poll(fn, message, timeoutMs = 120_000) {
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
    operatorId,
    operator_id: operatorId,
    email: `${role.toLowerCase()}-${userId.slice(-4)}@idempotency.test`,
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
  return { status: response.status, json: await response.json().catch(() => null) };
}

function errorCode(response) {
  return response.json?.errorCode ?? response.json?.error?.code ?? response.json?.code;
}

function expectMismatch(response) {
  assert(
    response.status === 422 && errorCode(response) === 'IDEMPOTENCY_KEY_MISMATCH',
    `Expected 422 IDEMPOTENCY_KEY_MISMATCH, got ${response.status}: ${JSON.stringify(response.json)}`,
  );
}

function seedFixtures() {
  console.log('STEP | seed Identity fixtures');
  identitySql(`
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active,parcel_no_show_policy)
    VALUES ('${ids.operator}','Idempotency Operator','IDEM-BRN','IDEM-TAX','operator@idempotency.test','+84910043001','APPROVED',now(),true,'{"noShowFeePercent":0,"additionalPaymentTimeoutMinutes":30}'::jsonb)
    ON CONFLICT (id) DO UPDATE SET registration_status='APPROVED',is_active=true,deleted_at=NULL;
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.admin}','admin@idempotency.test','+84910043002','Idempotency Admin','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
      ('${ids.assistant}','assistant@idempotency.test','+84910043003','Idempotency Assistant','ASSISTANT','ACTIVE','${ids.operator}'),
      ('${ids.driver}','driver@idempotency.test','+84910043004','Idempotency Driver','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.passenger}','passenger@idempotency.test','+84910043005','Idempotency Passenger','PASSENGER','ACTIVE',NULL)
    ON CONFLICT (id) DO UPDATE SET role=EXCLUDED.role,status=EXCLUDED.status,operator_id=EXCLUDED.operator_id,deleted_at=NULL;
  `);

  console.log('STEP | seed Trip fixtures');
  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.origin,
    name: 'Idempotency Origin',
    slug: 'idempotency-origin',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.77,
    longitude: 106.7,
    supports_shuttle: false,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.destination,
    name: 'Idempotency Destination',
    slug: 'idempotency-destination',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.8,
    longitude: 106.75,
    supports_shuttle: false,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'routes', {
    id: ids.route,
    operator_id: ids.operator,
    name: 'Idempotency Route',
    origin_station_id: ids.origin,
    destination_station_id: ids.destination,
    base_fare: 100000,
    total_distance_km: 50,
    estimated_duration_minutes: 120,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicle_types', {
    id: ids.vehicleType,
    code: 'IDEMPOTENCY_BUS',
    display_name: 'Idempotency Bus',
    default_seat_count: 20,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicles', {
    id: ids.vehicle,
    operator_id: ids.operator,
    vehicle_type_id: ids.vehicleType,
    license_plate: '51B-430.43',
    seat_layout_json: '{}',
    total_seats: 20,
    max_cargo_weight_kg: 100,
    max_cargo_volume_m3: 10,
    status: 'ACTIVE',
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'trips', {
    id: ids.trip,
    operator_id: ids.operator,
    route_id: ids.route,
    vehicle_id: ids.vehicle,
    driver_user_id: ids.driver,
    assistant_user_id: ids.assistant,
    departure_date_time: new Date(Date.now() + 3_600_000).toISOString(),
    estimated_arrival_time: new Date(Date.now() + 10_800_000).toISOString(),
    status: 'SCHEDULED',
    source: 'MANUAL',
    base_fare: 100000,
    max_cargo_weight_kg: 100,
    max_cargo_volume_m3: 10,
    estimated_passenger_luggage_kg: 0,
    reserved_parcel_weight_kg: 3,
    reserved_parcel_volume_m3: 0.002,
    total_loaded_weight_kg: 0,
    total_loaded_volume_m3: 0,
  });

  for (const [cargoId, parcelId, weight] of [
    [ids.cargoAdditional, ids.parcelAdditional, 1],
    [ids.cargoRefund, ids.parcelRefund, 2],
  ]) {
    insertCompatible('vietride_trip', 'vietride_trip', 'trip_cargo_parcels', {
      id: cargoId,
      trip_id: ids.trip,
      parcel_id: parcelId,
      weight_kg: weight,
      volume_m3: 0.001,
      state: 'RESERVED',
    });
  }

  console.log('STEP | seed Parcel fixtures');
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcel_route_fares', {
    route_id: ids.route,
    size_category: 'MEDIUM',
    operator_id: ids.operator,
    price_vnd: 100000,
    price_per_chargeable_kg_vnd: 100000,
    minimum_price_vnd: 0,
    effective_from: new Date(Date.now() - 3_600_000).toISOString(),
  });
  const parcelBase = {
    sender_user_id: ids.passenger,
    recipient_name: 'Idempotency Recipient',
    recipient_phone: '+84910043999',
    recipient_email: 'recipient@idempotency.test',
    operator_id: ids.operator,
    trip_id: ids.trip,
    size_category: 'MEDIUM',
    estimated_length_cm: 10,
    estimated_width_cm: 10,
    estimated_height_cm: 10,
    estimated_volume_m3: 0.001,
    estimated_dim_weight_kg: 0.2,
    delivery_method: 'TERMINAL_PICKUP',
    deposit_percent: 100,
    discount_amount: 0,
    additional_amount: 0,
    refund_amount: 0,
    status: 'PENDING',
  };
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.parcelAdditional,
    parcel_code: 'VRP-IDEM-A',
    estimated_weight_kg: 1,
    estimated_chargeable_weight_kg: 1,
    total_price_vnd: 100000,
    deposit_amount: 100000,
    original_deposit_amount: 100000,
  });
  insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
    ...parcelBase,
    id: ids.parcelRefund,
    parcel_code: 'VRP-IDEM-B',
    estimated_weight_kg: 2,
    estimated_chargeable_weight_kg: 2,
    total_price_vnd: 200000,
    deposit_amount: 200000,
    original_deposit_amount: 200000,
  });

  const context = JSON.stringify({
    version: 1,
    allocations: [
      {
        referenceId: ids.parcelRefund,
        referenceType: 'PARCEL',
        operatorId: ids.operator,
        tripId: ids.trip,
        grossAmount: 200000,
        voucherVietRideFundedAmount: 0,
        voucherOperatorFundedAmount: 0,
      },
    ],
  });
  insertCompatible('vietride_payment', 'vietride_payment', 'payments', {
    id: ids.originalPayment,
    reference_type: 'PARCEL',
    reference_id: ids.parcelRefund,
    user_id: ids.passenger,
    amount: 200000,
    method: 'WALLET',
    status: 'SUCCEEDED',
    succeeded_at: new Date(Date.now() - 60_000).toISOString(),
    context,
    context_reconciliation_required: false,
  });
  console.log('STEP | seed Payment wallet fixtures');
  paymentSql(`
    INSERT INTO wallets (user_id,balance,currency,row_version)
    VALUES ('${ids.passenger}',500000,'VND',0)
    ON CONFLICT (user_id) DO UPDATE SET balance=500000,row_version=0,updated_at=now();
    INSERT INTO platform_wallets (id,balance,currency,row_version)
    SELECT '${ids.platformWallet}',200000,'VND',0
    WHERE NOT EXISTS (SELECT 1 FROM platform_wallets);
    UPDATE platform_wallets SET balance=200000,row_version=0,updated_at=now();
  `);
  console.log('PASS | focused Parcel fixtures seeded');
}

async function runAcceptance() {
  const assistantToken = await userJwt(ids.assistant, 'ASSISTANT', ids.operator);
  const adminToken = await userJwt(ids.admin, 'OPERATOR_ADMIN', ids.operator);
  const additionalBody = {
    actualLengthCm: 10,
    actualWidthCm: 10,
    actualHeightCm: 10,
    actualWeightKg: 2,
    actualSizeCategory: 'MEDIUM',
    paymentMethod: 'WALLET',
  };
  const additionalKey = randomUUID();
  const additional = await api('POST', `/v1/assistant/parcels/${ids.parcelAdditional}/reweigh`, {
    token: assistantToken,
    key: additionalKey,
    body: additionalBody,
  });
  assert(
    additional.status === 200 &&
      additional.json?.data?.status === 'PENDING_ADDITIONAL_PAYMENT' &&
      additional.json?.data?.additionalAmount === 100000,
    `Additional-payment reweigh failed: ${JSON.stringify(additional)}`,
  );
  const additionalReplay = await api(
    'POST',
    `/v1/assistant/parcels/${ids.parcelAdditional}/reweigh`,
    { token: assistantToken, key: additionalKey, body: additionalBody },
  );
  assert(
    additionalReplay.status === 200 &&
      JSON.stringify(additionalReplay.json) === JSON.stringify(additional.json),
    'Additional-payment same-key replay changed the response',
  );
  expectMismatch(
    await api('POST', `/v1/assistant/parcels/${ids.parcelAdditional}/reweigh`, {
      token: assistantToken,
      key: additionalKey,
      body: { ...additionalBody, actualWeightKg: 3 },
    }),
  );
  assert(
    count(
      paymentSql(
        `SELECT count(*) FROM payments WHERE reference_type='PARCEL_ADDITIONAL' AND reference_id='${ids.parcelAdditional}'`,
      ),
    ) === 1,
    'Additional-payment replay created duplicate Payment rows',
  );
  assert(
    scalar(
      tripSql(
        `SELECT count(*)||':'||max(weight_kg)::text FROM trip_cargo_parcels WHERE trip_id='${ids.trip}' AND parcel_id='${ids.parcelAdditional}'`,
      ),
    ) === '1:2.00',
    'Trip cargo remeasure was not applied exactly once',
  );
  console.log('PASS | Parcel reweigh additional payment same-key replay + mismatch');

  const refundBody = {
    actualLengthCm: 10,
    actualWidthCm: 10,
    actualHeightCm: 10,
    actualWeightKg: 1,
    actualSizeCategory: 'MEDIUM',
    paymentMethod: 'WALLET',
  };
  const refundReweighKey = randomUUID();
  const refundReweigh = await api('POST', `/v1/assistant/parcels/${ids.parcelRefund}/reweigh`, {
    token: assistantToken,
    key: refundReweighKey,
    body: refundBody,
  });
  assert(
    refundReweigh.status === 200 &&
      refundReweigh.json?.data?.status === 'PENDING_OPERATOR_ACTION' &&
      refundReweigh.json?.data?.refundAmount === 100000,
    `Refund reweigh failed: ${JSON.stringify(refundReweigh)}`,
  );
  const refundReweighReplay = await api(
    'POST',
    `/v1/assistant/parcels/${ids.parcelRefund}/reweigh`,
    { token: assistantToken, key: refundReweighKey, body: refundBody },
  );
  assert(
    refundReweighReplay.status === 200 &&
      JSON.stringify(refundReweighReplay.json) === JSON.stringify(refundReweigh.json),
    'Refund reweigh same-key replay changed the response',
  );

  const confirmKey = randomUUID();
  const confirmBody = { reason: 'Focused idempotency E2E' };
  const confirm = await api('POST', `/v1/operator/parcels/${ids.parcelRefund}/confirm-refund`, {
    token: adminToken,
    key: confirmKey,
    body: confirmBody,
  });
  assert(confirm.status === 200, `Confirm refund failed: ${JSON.stringify(confirm)}`);
  const confirmReplay = await api(
    'POST',
    `/v1/operator/parcels/${ids.parcelRefund}/confirm-refund`,
    { token: adminToken, key: confirmKey, body: confirmBody },
  );
  assert(
    confirmReplay.status === 200 &&
      JSON.stringify(confirmReplay.json) === JSON.stringify(confirm.json),
    'Confirm-refund same-key replay changed the response',
  );
  expectMismatch(
    await api('POST', `/v1/operator/parcels/${ids.parcelRefund}/confirm-refund`, {
      token: adminToken,
      key: confirmKey,
      body: { reason: 'Different payload' },
    }),
  );

  await poll(
    () =>
      count(
        paymentSql(
          `SELECT count(*) FROM wallet_transactions WHERE reference_type='PARCEL_REFUND' AND reference_id='${ids.parcelRefund}'`,
        ),
      ) === 1,
    'Payment did not consume parcel.refund.initiated exactly once',
  );
  assert(
    count(
      parcelSql(
        `SELECT count(*) FROM outbox_events WHERE event_type='parcel.refund.initiated' AND payload->>'parcelId'='${ids.parcelRefund}'`,
      ),
    ) === 1,
    'Confirm-refund replay emitted duplicate refund events',
  );
  assert(
    count(
      paymentSql(
        `SELECT count(*) FROM platform_wallet_transactions WHERE reference_type='PARCEL_REFUND' AND reference_id='${ids.parcelRefund}'`,
      ),
    ) === 1,
    'Refund replay debited the platform wallet more than once',
  );
  assert(
    scalar(paymentSql(`SELECT balance FROM wallets WHERE user_id='${ids.passenger}'`)) === '500000',
    'Passenger wallet balance does not reflect one debit and one refund',
  );
  assert(
    scalar(paymentSql('SELECT balance FROM platform_wallets')) === '200000',
    'Platform wallet balance does not reflect one charge and one refund',
  );
  console.log('PASS | Parcel refund RabbitMQ -> Payment wallet exactly once');
  console.log('PASS | parcel/payment/trip idempotency focused system E2E');
}

let failed;
try {
  run('docker', [...compose, 'down', '-v', '--remove-orphans']);
  if (reuseImages && !noBuild) {
    run('docker', [...compose, '--parallel', '1', 'build', 'payment', 'parcel'], {
      stdio: 'inherit',
    });
  }
  const noBuildArgs = noBuild || reuseImages ? ['--no-build'] : ['--build'];
  run(
    'docker',
    [...compose, 'up', '-d', ...noBuildArgs, '--wait', 'postgres', 'redis', 'rabbitmq'],
    {
      stdio: 'inherit',
    },
  );
  run(
    'docker',
    [...compose, 'up', '-d', ...noBuildArgs, '--wait', 'identity', 'trip', 'payment', 'parcel'],
    { stdio: 'inherit' },
  );
  run('docker', [...compose, 'up', '-d', '--no-build', '--no-deps', '--wait', 'gateway'], {
    stdio: 'inherit',
  });
  console.log('PASS | isolated Parcel compose health | http://localhost:55300');
  seedFixtures();
  await runAcceptance();
} catch (error) {
  failed = error;
  console.error(error instanceof Error ? error.stack : error);
  for (const service of ['trip', 'payment', 'parcel', 'gateway']) {
    try {
      const logs = run('docker', [...compose, 'logs', '--no-color', '--tail', '160', service]);
      const relevantLogs = logs
        .split(/\r?\n/u)
        .filter((line) =>
          /HTTP (POST|PATCH|PUT|DELETE)|idempotency|reweigh|refund|exception|error|failed/iu.test(
            line,
          ),
        )
        .slice(-80)
        .join('\n');
      console.error(
        `DIAGNOSTIC ${service}\n${relevantLogs || '(no relevant mutation/error logs)'}`,
      );
    } catch {
      // A failed container may not have logs; cleanup remains mandatory.
    }
  }
  try {
    run(
      'docker',
      [
        'exec',
        'day37-e2e-rabbitmq',
        'rabbitmqctl',
        'list_queues',
        'name',
        'messages_ready',
        'messages_unacknowledged',
      ],
      { stdio: 'inherit' },
    );
  } catch {
    // RabbitMQ may not have started.
  }
} finally {
  try {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
    console.log('PASS | isolated Parcel compose cleanup');
  } catch (error) {
    failed ??= error;
    console.error(`FAIL | isolated Parcel compose cleanup | ${String(error)}`);
  }
}

process.exitCode = failed ? 1 : 0;
