import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const gateway = 'http://localhost:57680';
const services = {
  identity: 'http://localhost:57001',
  trip: 'http://localhost:57002',
  parcel: 'http://localhost:57005',
};
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day32-e2e.yml',
];
const e2eEnv = {
  POSTGRES_USER: 'day32_e2e',
  POSTGRES_PASSWORD: 'day32_e2e_postgres_only',
  POSTGRES_PORT: '55432',
  REDIS_PORT: '56372',
  RABBITMQ_USER: 'day32_e2e',
  RABBITMQ_PASSWORD: 'day32_e2e_rabbit_only',
  RABBITMQ_PORT: '55632',
  RABBITMQ_MGMT_PORT: '55633',
  IDENTITY_PORT: '57001',
  TRIP_PORT: '57002',
  PARCEL_PORT: '57005',
  GATEWAY_PORT: '57680',
  INTERNAL_JWT_SECRET: 'day32-e2e-internal-jwt-secret-32-bytes-minimum',
  GOOGLE_OAUTH_CLIENT_ID: '',
  GOOGLE_OAUTH_CLIENT_SECRET: '',
  SYSTEM_ADMIN_BOOTSTRAP_EMAIL: 'system@day32.test',
  SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: 'Day32-E2E-Only-Password-123!',
};
const containers = {
  postgres: 'day32-e2e-postgres',
  identity: 'day32-e2e-identity',
  trip: 'day32-e2e-trip',
  parcel: 'day32-e2e-parcel',
  gateway: 'day32-e2e-gateway',
};
const id = (suffix) => `32000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
const ids = {
  operator: id(1),
  admin: id(2),
  sender: id(3),
  driver: id(4),
  assistant: id(5),
  origin: id(101),
  destination: id(102),
  route: id(103),
  vehicleType: id(104),
  vehicle: id(105),
  successSource: id(201),
  successTarget: id(202),
  crashSource: id(203),
  crashTarget: id(204),
  raceSource: id(205),
  raceTarget: id(206),
  successParcel: id(301),
  crashParcel: id(302),
  raceParcel: id(303),
  successCargo: id(401),
  crashCargo: id(402),
  raceCargo: id(403),
};
const results = [];

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
    e2eEnv.POSTGRES_USER,
    '-d',
    database,
    '-qAtc',
    `SET search_path TO ${schema},public; ${statement}`,
  ]);
}

const identitySql = (statement) => sql('vietride_identity', 'vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', 'vietride_trip', statement);
const parcelSql = (statement) => sql('vietride_parcel', 'vietride_parcel', statement);

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

async function poll(fn, message, timeoutMs = 300_000) {
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

async function waitFor(url) {
  await poll(async () => {
    try {
      return (await fetch(url)).ok;
    } catch {
      return false;
    }
  }, `Timed out waiting for ${url}`);
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

function idemKey(label) {
  const hash = createHash('sha256').update(`day32:${label}`).digest('hex');
  return `${hash.slice(0, 8)}-${hash.slice(8, 12)}-4${hash.slice(13, 16)}-8${hash.slice(17, 20)}-${hash.slice(20, 32)}`;
}

async function userJwt() {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const key = await importPKCS8(settings.IdentityJwt.PrivateKey, 'RS256');
  return new SignJWT({
    role: 'OPERATOR_ADMIN',
    operatorId: ids.operator,
    operator_id: ids.operator,
    email: 'admin@day32.test',
    hasPhone: true,
  })
    .setProtectedHeader({ alg: 'RS256', kid: settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(ids.admin)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
}

async function internalJwt() {
  return new SignJWT({
    callerService: 'parcel',
    role: 'SERVICE',
  })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setSubject('vietride-system')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(e2eEnv.INTERNAL_JWT_SECRET));
}

async function fetchWithTransportRetry(url, init, maxAttempts = 4) {
  let lastError;
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      return await fetch(url, init);
    } catch (error) {
      lastError = error;
      if (attempt < maxAttempts) {
        await new Promise((resolve) => setTimeout(resolve, attempt * 200));
      }
    }
  }
  throw lastError;
}

async function api(method, pathname, { token, body, key } = {}) {
  const response = await fetchWithTransportRetry(`${gateway}${pathname}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const json = await response.json().catch(() => null);
  return { status: response.status, json };
}

async function directTripTransfer(operationId, parcelId, sourceTripId, targetTripId) {
  const response = await fetchWithTransportRetry(
    `${services.trip}/internal/v1/trips/${sourceTripId}/cargo/transfer`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': operationId,
        'X-Internal-Auth': `Bearer ${await internalJwt()}`,
      },
      body: JSON.stringify({
        parcelId,
        targetTripId,
        targetState: 'RESERVED',
        allowCapacityOverflow: false,
      }),
    },
  );
  const json = await response.json().catch(() => null);
  assert(response.status === 200, `Direct Trip transfer failed: ${response.status} ${JSON.stringify(json)}`);
  return json;
}

function seed() {
  identitySql(`
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
    VALUES ('${ids.operator}','Day 32 Operator','D32-BRN','D32-TAX','operator@day32.test','+84910032001','APPROVED',now(),true)
    ON CONFLICT (id) DO UPDATE SET registration_status='APPROVED',is_active=true,deleted_at=NULL;
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.admin}','admin@day32.test','+84910032101','Day 32 Admin','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
      ('${ids.sender}','sender@day32.test','+84910032102','Day 32 Sender','PASSENGER','ACTIVE',NULL),
      ('${ids.driver}','driver@day32.test','+84910032103','Day 32 Driver','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.assistant}','assistant@day32.test','+84910032104','Day 32 Assistant','ASSISTANT','ACTIVE','${ids.operator}')
    ON CONFLICT (id) DO UPDATE SET role=EXCLUDED.role,status=EXCLUDED.status,operator_id=EXCLUDED.operator_id,deleted_at=NULL;
  `);

  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.origin,
    name: 'Day 32 Origin',
    slug: 'day32-origin',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.77,
    longitude: 106.7,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'stations', {
    id: ids.destination,
    name: 'Day 32 Destination',
    slug: 'day32-destination',
    city: 'HCM',
    province: 'HCM',
    latitude: 10.8,
    longitude: 106.75,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'routes', {
    id: ids.route,
    operator_id: ids.operator,
    name: 'Day 32 Route',
    origin_station_id: ids.origin,
    destination_station_id: ids.destination,
    base_fare: 100000,
    total_distance_km: 50,
    estimated_duration_minutes: 120,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicle_types', {
    id: ids.vehicleType,
    code: 'DAY32_BUS',
    display_name: 'Day 32 Bus',
    default_seat_count: 20,
    is_active: true,
  });
  insertCompatible('vietride_trip', 'vietride_trip', 'vehicles', {
    id: ids.vehicle,
    operator_id: ids.operator,
    vehicle_type_id: ids.vehicleType,
    license_plate: '51B-320.32',
    seat_layout_json: '{}',
    total_seats: 20,
    max_cargo_weight_kg: 1000,
    max_cargo_volume_m3: 20,
    status: 'ACTIVE',
    is_active: true,
  });

  const now = Date.now();
  const tripIds = [
    ids.successSource,
    ids.successTarget,
    ids.crashSource,
    ids.crashTarget,
    ids.raceSource,
    ids.raceTarget,
  ];
  for (const [index, tripId] of tripIds.entries()) {
    insertCompatible('vietride_trip', 'vietride_trip', 'trips', {
      id: tripId,
      operator_id: ids.operator,
      route_id: ids.route,
      vehicle_id: ids.vehicle,
      driver_user_id: ids.driver,
      assistant_user_id: ids.assistant,
      departure_date_time: new Date(now + (index + 1) * 60_000).toISOString(),
      estimated_arrival_time: new Date(now + (index + 121) * 60_000).toISOString(),
      status: 'SCHEDULED',
      source: 'MANUAL',
      base_fare: 100000,
      max_cargo_weight_kg: 1000,
      max_cargo_volume_m3: 20,
      reserved_parcel_weight_kg: index % 2 === 0 ? 10 : 0,
      reserved_parcel_volume_m3: index % 2 === 0 ? 0.5 : 0,
      total_loaded_weight_kg: 0,
      total_loaded_volume_m3: 0,
    });
  }

  const parcelBase = {
    sender_user_id: ids.sender,
    recipient_user_id: ids.admin,
    recipient_name: 'Day 32 Recipient',
    recipient_phone: '+84910032999',
    recipient_email: 'recipient@day32.test',
    operator_id: ids.operator,
    size_category: 'MEDIUM',
    estimated_size_category: 'MEDIUM',
    estimated_weight_kg: 10,
    estimated_volume_m3: 0.5,
    deposit_amount: 150000,
    original_deposit_amount: 150000,
    total_price_vnd: 200000,
    estimated_gross_price_vnd: 200000,
    final_gross_price_vnd: 200000,
    estimated_total_price_vnd: 200000,
    final_total_price_vnd: 200000,
    deposit_required_vnd: 150000,
    deposit_paid_vnd: 150000,
    balance_required_vnd: 50000,
    balance_paid_vnd: 50000,
    refunded_amount_vnd: 25000,
    refund_due_vnd: 25000,
    status: 'PENDING_OPERATOR_ACTION',
  };
  for (const [index, [parcelId, tripId]] of [
    [ids.successParcel, ids.successSource],
    [ids.crashParcel, ids.crashSource],
    [ids.raceParcel, ids.raceSource],
  ].entries()) {
    insertCompatible('vietride_parcel', 'vietride_parcel', 'parcels', {
      ...parcelBase,
      id: parcelId,
      parcel_code: `VRP-D32${String(index + 1).padStart(3, '0')}`,
      trip_id: tripId,
      pending_action_reason: 'TRIP_CANCELLED',
    });
  }
  for (const [cargoId, tripId, parcelId] of [
    [ids.successCargo, ids.successSource, ids.successParcel],
    [ids.crashCargo, ids.crashSource, ids.crashParcel],
    [ids.raceCargo, ids.raceSource, ids.raceParcel],
  ]) {
    insertCompatible('vietride_trip', 'vietride_trip', 'trip_cargo_parcels', {
      id: cargoId,
      trip_id: tripId,
      parcel_id: parcelId,
      weight_kg: 10,
      volume_m3: 0.5,
      state: 'RESERVED',
    });
  }
}

function assertTransferred(parcelId, sourceTripId, targetTripId, operationId) {
  assert(
    scalar(parcelSql(`SELECT status::text || ':' || trip_id::text FROM parcels WHERE id='${parcelId}'`)) ===
      `RESERVED:${targetTripId}`,
    `Parcel ${parcelId} was not finalized on target Trip`,
  );
  assert(
    scalar(parcelSql(`SELECT status || ':' || operation_type FROM parcel_cargo_recovery_operations WHERE id='${operationId}'`)) ===
      'COMPLETED:TRANSFER',
    `Transfer operation ${operationId} was not completed`,
  );
  assert(
    scalar(tripSql(`SELECT state FROM trip_cargo_parcels WHERE trip_id='${sourceTripId}' AND parcel_id='${parcelId}'`)) ===
      'RELEASED',
    `Source cargo ${parcelId} was not released`,
  );
  assert(
    scalar(tripSql(`SELECT state FROM trip_cargo_parcels WHERE trip_id='${targetTripId}' AND parcel_id='${parcelId}'`)) ===
      'RESERVED',
    `Target cargo ${parcelId} was not reserved`,
  );
  assert(
    scalar(tripSql(`SELECT reserved_parcel_weight_kg::text || ':' || reserved_parcel_volume_m3::text FROM trips WHERE id='${sourceTripId}'`)) ===
      '0.00:0.0000',
    `Source Trip counters are incoherent for ${parcelId}`,
  );
  assert(
    scalar(tripSql(`SELECT reserved_parcel_weight_kg::text || ':' || reserved_parcel_volume_m3::text FROM trips WHERE id='${targetTripId}'`)) ===
      '10.00:0.5000',
    `Target Trip counters are incoherent for ${parcelId}`,
  );
}

async function runAcceptance() {
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
    'parcel',
    'gateway',
  ]);
  await Promise.all([
    waitFor(`${gateway}/health`),
    waitFor(`${gateway}/ready`),
    waitFor(`${services.identity}/health`),
    waitFor(`${services.trip}/health`),
    waitFor(`${services.parcel}/health`),
  ]);
  seed();
  const token = await userJwt();
  console.log('isolated migrations + seed PASS');

  await scenario(1, 'Public transfer is atomic, idempotent and exactly-once', async () => {
    const key = idemKey('success-transfer');
    const body = { targetTripId: ids.successTarget, reason: 'Move to recovery Trip' };
    const first = await api(
      'POST',
      `/v1/operator/parcels/${ids.successParcel}/request-transfer`,
      { token, key, body },
    );
    assert(first.status === 200, `Transfer failed: ${JSON.stringify(first)}`);
    assert(first.json?.data?.status === 'RESERVED', `Unexpected transfer response: ${JSON.stringify(first.json)}`);
    assertTransferred(ids.successParcel, ids.successSource, ids.successTarget, key);

    const replay = await api(
      'POST',
      `/v1/operator/parcels/${ids.successParcel}/request-transfer`,
      { token, key, body },
    );
    assert(replay.status === 200, `Transfer replay failed: ${JSON.stringify(replay)}`);
    assert(
      count(parcelSql(`SELECT count(*) FROM parcel_cargo_recovery_operations WHERE parcel_id='${ids.successParcel}'`)) === 1,
      'Transfer replay created a duplicate recovery operation',
    );
    assert(
      count(tripSql(`SELECT count(*) FROM trip_cargo_parcels WHERE parcel_id='${ids.successParcel}'`)) === 2,
      'Transfer replay created a duplicate cargo ledger',
    );
    assert(
      count(parcelSql(`SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.transfer_initiated' AND payload->>'parcelId'='${ids.successParcel}'`)) === 1,
      'Transfer replay emitted duplicate outbox events',
    );
  });

  await scenario(2, 'Crash after Trip commit is recovered by stable operation replay', async () => {
    const operationId = idemKey('crash-operation');
    parcelSql(`
      INSERT INTO parcel_cargo_recovery_operations
        (id,parcel_id,operator_id,operation_type,status,source_trip_id,target_trip_id,target_state,
         actor_user_id,reason,refund_amount_vnd,refund_due_vnd,source_status,is_status_override,
         claimed_at,created_at,updated_at)
      VALUES
        ('${operationId}','${ids.crashParcel}','${ids.operator}','TRANSFER','PENDING',
         '${ids.crashSource}','${ids.crashTarget}','RESERVED','${ids.admin}','Crash replay',
         0,0,'PENDING_OPERATOR_ACTION',false,now(),now(),now());
    `);
    await directTripTransfer(operationId, ids.crashParcel, ids.crashSource, ids.crashTarget);
    assert(
      scalar(parcelSql(`SELECT status::text || ':' || trip_id::text FROM parcels WHERE id='${ids.crashParcel}'`)) ===
        `PENDING_OPERATOR_ACTION:${ids.crashSource}`,
      'Crash precondition did not leave Parcel behind Trip',
    );

    const replay = await api(
      'POST',
      `/v1/operator/parcels/${ids.crashParcel}/request-transfer`,
      {
        token,
        key: idemKey('crash-public-retry'),
        body: { targetTripId: ids.crashTarget, reason: 'Crash replay' },
      },
    );
    assert(replay.status === 200, `Crash replay failed: ${JSON.stringify(replay)}`);
    assertTransferred(ids.crashParcel, ids.crashSource, ids.crashTarget, operationId);
    assert(
      count(parcelSql(`SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.transfer_initiated' AND payload->>'parcelId'='${ids.crashParcel}'`)) === 1,
      'Crash replay did not preserve exactly-once Parcel side effects',
    );
  });

  await scenario(3, 'Concurrent transfer and return allow exactly one durable winner', async () => {
    const transferKey = idemKey('race-transfer');
    const returnKey = idemKey('race-return');
    const [transfer, returned] = await Promise.all([
      api('POST', `/v1/operator/parcels/${ids.raceParcel}/request-transfer`, {
        token,
        key: transferKey,
        body: { targetTripId: ids.raceTarget, reason: 'Race transfer' },
      }),
      api('POST', `/v1/operator/parcels/${ids.raceParcel}/return`, {
        token,
        key: returnKey,
        body: { returnReason: 'Race return' },
      }),
    ]);
    const successes = [transfer, returned].filter((response) => response.status === 200);
    const conflicts = [transfer, returned].filter((response) => response.status === 409);
    assert(
      successes.length === 1 && conflicts.length === 1,
      `Expected one winner and one conflict: transfer=${JSON.stringify(transfer)}, return=${JSON.stringify(returned)}`,
    );
    assert(
      count(parcelSql(`SELECT count(*) FROM parcel_cargo_recovery_operations WHERE parcel_id='${ids.raceParcel}' AND status='COMPLETED'`)) === 1,
      'Race did not produce exactly one completed operation',
    );
    assert(
      count(parcelSql(`SELECT count(*) FROM parcel_cargo_recovery_operations WHERE parcel_id='${ids.raceParcel}' AND status='PENDING'`)) === 0,
      'Race left a pending operation',
    );

    const winner = scalar(
      parcelSql(`SELECT operation_type || ':' || refund_amount_vnd::text || ':' || refund_due_vnd::text FROM parcel_cargo_recovery_operations WHERE parcel_id='${ids.raceParcel}' AND status='COMPLETED'`),
    );
    if (winner.startsWith('TRANSFER:')) {
      assertTransferred(ids.raceParcel, ids.raceSource, ids.raceTarget, transferKey);
      assert(
        count(parcelSql(`SELECT count(*) FROM outbox_events WHERE event_type='parcel.refund.initiated' AND payload->>'parcelId'='${ids.raceParcel}'`)) === 0,
        'Transfer winner emitted a refund',
      );
    } else {
      assert(winner === 'RETURN:175000:200000', `Return did not freeze the authoritative refund: ${winner}`);
      assert(
        scalar(parcelSql(`SELECT status::text || ':' || refund_due_vnd::text FROM parcels WHERE id='${ids.raceParcel}'`)) ===
          'RETURNED:200000',
        'Return winner did not finalize Parcel/refund due',
      );
      assert(
        scalar(tripSql(`SELECT state FROM trip_cargo_parcels WHERE trip_id='${ids.raceSource}' AND parcel_id='${ids.raceParcel}'`)) ===
          'RELEASED',
        'Return winner did not release source cargo',
      );
      assert(
        count(parcelSql(`SELECT count(*) FROM outbox_events WHERE event_type='parcel.parcel.returned' AND payload->>'parcelId'='${ids.raceParcel}'`)) === 1 &&
          count(parcelSql(`SELECT count(*) FROM outbox_events WHERE event_type='parcel.refund.initiated' AND payload->>'parcelId'='${ids.raceParcel}'`)) === 1,
        'Return winner did not emit exactly-once return/refund events',
      );
    }
  });

  await scenario(4, 'Database constraints and migrations match the recovery contract', async () => {
    assert(
      scalar(parcelSql(`SELECT count(*) FILTER (WHERE indexname='uq_parcel_cargo_recovery_operations_active_parcel')::text || ':' || count(*) FILTER (WHERE indexname='idx_parcel_cargo_recovery_operations_stale')::text FROM pg_indexes WHERE schemaname='vietride_parcel' AND tablename='parcel_cargo_recovery_operations'`)) ===
        '1:1',
      'Recovery indexes are missing or duplicated',
    );
    assert(
      count(parcelSql(`SELECT count(*) FROM parcel_cargo_recovery_operations WHERE status='PENDING'`)) === 0,
      'Acceptance run left pending recovery work',
    );
    const raceStatus = scalar(
      parcelSql(`SELECT status::text FROM parcels WHERE id='${ids.raceParcel}'`),
    );
    const expectedActiveCargo = raceStatus === 'RESERVED' ? 3 : 2;
    assert(
      count(tripSql(`SELECT count(*) FROM trip_cargo_parcels WHERE state <> 'RELEASED'`)) ===
        expectedActiveCargo,
      `Cargo ledger has an unexpected number of active rows for race winner ${raceStatus}`,
    );
    assert(
      count(tripSql(`SELECT count(*) FROM (SELECT parcel_id FROM trip_cargo_parcels WHERE state <> 'RELEASED' GROUP BY parcel_id HAVING count(*) > 1) duplicates`)) === 0,
      'A Parcel has more than one active Trip cargo ledger',
    );
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
  for (const service of ['identity', 'trip', 'parcel', 'gateway']) {
    try {
      console.error(
        `DIAGNOSTIC ${service}\n${run('docker', ['logs', containers[service], '--tail', '160'])}`,
      );
    } catch {
      // Container creation may have failed; cleanup remains mandatory.
    }
  }
} finally {
  try {
    composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
    console.log('cleanup PASS');
  } catch (error) {
    failed ??= error;
    results.push({ scenario: 'CLEANUP', name: String(error), passed: false });
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'day32-cargo-recovery-e2e',
      scenariosPassed: results.filter((result) => result.passed).length,
      results,
    },
    null,
    2,
  ),
);
process.exitCode =
  failed || results.length < 4 || results.some((result) => !result.passed) ? 1 : 0;
