import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const container = 'vietride-idempotency-migrations-e2e';
const port = process.env.IDEMPOTENCY_MIGRATION_POSTGRES_PORT ?? '55489';
const postgresUser = 'vietride_migration_test';
const postgresPassword = 'vietride_migration_test_only';
const postgresImage = 'pgvector/pgvector:pg16';

const dotnetServices = [
  {
    name: 'identity',
    project: 'apps/identity/src/VietRide.Identity.Infrastructure',
    envKey: 'IDENTITY_DESIGN_CONNECTION',
    schema: 'vietride_identity',
    previous: '20260721190033_RemoveLegacySubscriptionWarningFlag',
    migration: '20260722093524_CreateIntegrationInboxTable',
    table: 'integration_inbox',
    seedTable: 'outbox_events',
  },
  {
    name: 'trip',
    project: 'apps/trip/src/VietRide.Trip.Infrastructure',
    envKey: 'TRIP_DESIGN_CONNECTION',
    schema: 'vietride_trip',
    previous: '20260718145841_AddPlatformTripStatsProjection',
    migration: '20260722093804_AddIntegrationInbox',
    table: 'integration_inbox',
    seedTable: 'outbox_events',
  },
  {
    name: 'booking',
    project: 'apps/booking/src/VietRide.Booking.Infrastructure',
    envKey: 'BOOKING_DESIGN_CONNECTION',
    schema: 'vietride_booking',
    previous: '20260718145339_AddPlatformBookingStatsProjection',
    migration: '20260722093941_AddIntegrationInbox',
    table: 'integration_inbox',
    seedTable: 'outbox_events',
  },
  {
    name: 'payment',
    project: 'apps/payment/src/VietRide.Payment.Infrastructure',
    envKey: 'PAYMENT_DESIGN_CONNECTION',
    schema: 'vietride_payment',
    previous: '20260721181513_FinalizeSubscriptionPaymentSessionDeadline',
    migration: '20260722094252_AddProcessedIntegrationEventPayloadHash',
    table: 'processed_integration_events',
    seedTable: 'processed_integration_events',
  },
  {
    name: 'parcel',
    project: 'apps/parcel/src/VietRide.Parcel.Infrastructure',
    envKey: 'PARCEL_DESIGN_CONNECTION',
    schema: 'vietride_parcel',
    previous: '20260718150224_AddPlatformParcelStatsProjection',
    migration: '20260722094132_AddIntegrationInbox',
    table: 'integration_inbox',
    seedTable: 'outbox_events',
  },
];

const prismaServices = [
  {
    name: 'notification',
    schema: 'vietride_notification',
    prismaSchema: 'apps/notification/prisma/schema.prisma',
    migrations: 'apps/notification/prisma/migrations',
    latest: '20260722095000_add_processed_messages',
    table: 'processed_messages',
    seed: `INSERT INTO vietride_notification.notifications
      (id,user_id,type,title,body,dedupe_key,created_at)
      VALUES ('47400000-0000-4000-8000-000000000001','47400000-0000-4000-8000-000000000002',
      'BOOKING_CONFIRMED','migration seed','preserve me','migration-seed',now());`,
    seedCheck: `SELECT count(*) FROM vietride_notification.notifications
      WHERE id='47400000-0000-4000-8000-000000000001' AND body='preserve me';`,
  },
  {
    name: 'tracking',
    schema: 'vietride_tracking',
    prismaSchema: 'apps/tracking/prisma/schema.prisma',
    migrations: 'apps/tracking/prisma/migrations',
    latest: '20260722095200_add_gps_natural_identity',
    table: 'gps_trails',
    seed: `INSERT INTO vietride_tracking.gps_trails
      (id,trip_id,latitude,longitude,recorded_at,created_at)
      VALUES ('47500000-0000-4000-8000-000000000001','47500000-0000-4000-8000-000000000002',
      10.75,106.65,'2026-07-23T00:00:00Z',now());`,
    seedCheck: `SELECT count(*) FROM vietride_tracking.gps_trails
      WHERE id='47500000-0000-4000-8000-000000000001';`,
  },
  {
    name: 'rag',
    schema: 'vietride_rag',
    prismaSchema: 'apps/rag/prisma/schema.prisma',
    migrations: 'apps/rag/prisma/migrations',
    latest: '20260722095100_add_idempotency_operations',
    table: 'idempotency_operations',
    seed: `INSERT INTO vietride_rag.knowledge_documents
      (id,title,storage_path,file_name,mime_type,file_size,file_type,access_level,category,
       document_type,audience_roles,uploaded_by_user_id,status,ingest_status,created_at,updated_at)
      VALUES ('47600000-0000-4000-8000-000000000001','migration seed','migration/seed',
      'seed.txt','text/plain',9,'TXT','PUBLIC','CUSTOMER_SUPPORT','GUIDE',ARRAY[]::text[],
      '47600000-0000-4000-8000-000000000002','PENDING_REVIEW','PENDING',now(),now());`,
    seedCheck: `SELECT count(*) FROM vietride_rag.knowledge_documents
      WHERE id='47600000-0000-4000-8000-000000000001' AND title='migration seed';`,
  },
];

function executable(command) {
  return process.platform === 'win32' && command === 'dotnet' ? 'dotnet.exe' : command;
}

function run(command, args, options = {}) {
  const result = spawnSync(executable(command), args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    input: options.input,
    stdio: options.inherit ? 'inherit' : 'pipe',
  });
  if (result.status !== 0 && !options.allowFailure) {
    throw new Error(
      `${command} ${args.join(' ')} failed (${result.status ?? result.error?.code ?? result.signal}):\n` +
        `${result.stderr || result.stdout || result.error?.message}`,
    );
  }
  return result;
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function sql(database, statement, options = {}) {
  return run(
    'docker',
    [
      'exec',
      '-i',
      container,
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-qAt',
      '-U',
      postgresUser,
      '-d',
      database,
    ],
    { input: statement, allowFailure: options.allowFailure },
  );
}

function scalar(database, statement) {
  return sql(database, statement).stdout.trim();
}

function createDatabase(database) {
  sql('postgres', `DROP DATABASE IF EXISTS ${database}; CREATE DATABASE ${database};`);
}

function connection(database) {
  return `Host=127.0.0.1;Port=${port};Database=${database};Username=${postgresUser};Password=${postgresPassword}`;
}

function databaseUrl(database) {
  return `postgresql://${postgresUser}:${postgresPassword}@127.0.0.1:${port}/${database}`;
}

function waitForPostgres() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    const result = run(
      'docker',
      ['exec', container, 'pg_isready', '-U', postgresUser, '-d', 'postgres'],
      { allowFailure: true },
    );
    if (result.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 500);
  }
  throw new Error('Isolated migration PostgreSQL did not become ready.');
}

function migrateEf(service, database, target) {
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
    { env: { [service.envKey]: connection(database) } },
  );
}

function assertTable(database, schema, table) {
  assert(
    scalar(
      database,
      `SELECT count(*) FROM information_schema.tables WHERE table_schema='${schema}' AND table_name='${table}';`,
    ) === '1',
    `${schema}.${table} was not created`,
  );
}

function verifyDotnetService(service) {
  const fresh = `idem_fresh_${service.name}`;
  createDatabase(fresh);
  migrateEf(service, fresh);
  assertTable(fresh, service.schema, service.table);
  assert(
    scalar(
      fresh,
      `SELECT count(*) FROM ${service.schema}.\"__ef_migrations_history\" WHERE \"MigrationId\"='${service.migration}';`,
    ) === '1',
    `${service.name} latest migration is missing from history`,
  );

  const upgrade = `idem_upgrade_${service.name}`;
  createDatabase(upgrade);
  migrateEf(service, upgrade, service.previous);
  const seedId = `4700000${dotnetServices.indexOf(service)}-0000-4000-8000-000000000001`;
  if (service.name === 'payment') {
    sql(
      upgrade,
      `INSERT INTO ${service.schema}.processed_integration_events
       (id,consumer,event_id,processed_at,created_at)
       VALUES ('${seedId}','migration-test','${seedId}',now(),now());`,
    );
  } else {
    sql(
      upgrade,
      `INSERT INTO ${service.schema}.${service.seedTable}
       (id,event_type,payload,status,retry_count,created_at)
       VALUES ('${seedId}','migration.test','{}','PENDING',0,now());`,
    );
  }
  migrateEf(service, upgrade);
  assertTable(upgrade, service.schema, service.table);
  assert(
    scalar(
      upgrade,
      `SELECT count(*) FROM ${service.schema}.${service.seedTable} WHERE id='${seedId}';`,
    ) === '1',
    `${service.name} upgrade lost the pre-existing row`,
  );
  if (service.name === 'payment') {
    assert(
      scalar(
        upgrade,
        `SELECT count(*) FROM ${service.schema}.processed_integration_events
         WHERE id='${seedId}' AND payload_hash IS NULL;`,
      ) === '1',
      'payment upgrade did not preserve the legacy processed event with a null hash',
    );
  }
  console.log(`PASS | ${service.name} fresh + data-preserving upgrade migration`);
}

function migrationDirectories(service, includeLatest) {
  return fs
    .readdirSync(path.join(root, service.migrations), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .filter((name) => includeLatest || name !== service.latest)
    .sort();
}

function applyPrismaSql(database, service, includeLatest) {
  for (const directory of migrationDirectories(service, includeLatest)) {
    const migration = fs.readFileSync(
      path.join(root, service.migrations, directory, 'migration.sql'),
      'utf8',
    );
    sql(database, migration);
  }
}

function deployPrisma(database, service) {
  const url = databaseUrl(database);
  run(
    process.execPath,
    [
      path.join(root, 'node_modules/prisma/build/index.js'),
      'migrate',
      'deploy',
      `--schema=${service.prismaSchema}`,
    ],
    {
      env: {
        DATABASE_URL: url,
        TRACKING_DATABASE_URL: url,
        NOTIFICATION_DATABASE_URL: url,
        RAG_DATABASE_URL: url,
      },
    },
  );
}

function verifyPrismaService(service) {
  const fresh = `idem_fresh_${service.name}`;
  createDatabase(fresh);
  deployPrisma(fresh, service);
  assertTable(fresh, service.schema, service.table);

  const upgrade = `idem_upgrade_${service.name}`;
  createDatabase(upgrade);
  applyPrismaSql(upgrade, service, false);
  sql(upgrade, service.seed);
  const latestSql = fs.readFileSync(
    path.join(root, service.migrations, service.latest, 'migration.sql'),
    'utf8',
  );
  sql(upgrade, latestSql);
  assertTable(upgrade, service.schema, service.table);
  assert(
    scalar(upgrade, service.seedCheck) === '1',
    `${service.name} upgrade lost the pre-existing row`,
  );
  console.log(`PASS | ${service.name} fresh + data-preserving upgrade migration`);
}

function verifyTrackingDuplicateGate() {
  const service = prismaServices.find((candidate) => candidate.name === 'tracking');
  const database = 'idem_upgrade_tracking_duplicates';
  createDatabase(database);
  applyPrismaSql(database, service, false);
  sql(
    database,
    `INSERT INTO vietride_tracking.gps_trails
      (id,trip_id,latitude,longitude,recorded_at,created_at) VALUES
      ('47700000-0000-4000-8000-000000000001','47700000-0000-4000-8000-000000000003',10,106,'2026-07-23T00:00:00Z',now()),
      ('47700000-0000-4000-8000-000000000002','47700000-0000-4000-8000-000000000003',11,107,'2026-07-23T00:00:00Z',now());`,
  );
  const latestSql = fs.readFileSync(
    path.join(root, service.migrations, service.latest, 'migration.sql'),
    'utf8',
  );
  const result = sql(database, latestSql, { allowFailure: true });
  const output = `${result.stderr}\n${result.stdout}`;
  assert(result.status !== 0, 'Tracking duplicate precheck silently accepted duplicate GPS rows');
  assert(
    output.includes('duplicate (trip_id, recorded_at) rows exist'),
    `Tracking duplicate precheck failed without the expected actionable message:\n${output}`,
  );
  assert(
    scalar(database, 'SELECT count(*) FROM vietride_tracking.gps_trails;') === '2',
    'Tracking duplicate precheck deleted or rewrote legacy rows',
  );
  console.log('PASS | tracking duplicate precheck fails clearly without deleting data');
}

function cleanup() {
  run('docker', ['rm', '-f', container], { allowFailure: true });
}

try {
  const started = run(
    'docker',
    [
      'run',
      '--rm',
      '-d',
      '--name',
      container,
      '-e',
      `POSTGRES_USER=${postgresUser}`,
      '-e',
      `POSTGRES_PASSWORD=${postgresPassword}`,
      '-e',
      'POSTGRES_DB=postgres',
      '-p',
      `127.0.0.1:${port}:5432`,
      postgresImage,
    ],
    { allowFailure: true },
  );
  if (started.status !== 0) {
    throw new Error(
      `Could not start isolated migration PostgreSQL:\n${started.stderr || started.stdout}`,
    );
  }
  waitForPostgres();
  for (const service of dotnetServices) verifyDotnetService(service);
  for (const service of prismaServices) verifyPrismaService(service);
  verifyTrackingDuplicateGate();
  console.log('PASS | all 8 idempotency migration fresh/upgrade gates');
} catch (error) {
  console.error(`FAIL | ${error.stack || error.message}`);
  process.exitCode = 1;
} finally {
  cleanup();
}
