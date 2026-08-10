import { spawnSync } from 'node:child_process';
import { inflateRawSync } from 'node:zlib';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import amqp from 'amqplib';
import { importPKCS8, SignJWT } from 'jose';

const root = process.cwd();
const requestedScope = process.env.DAY41_43_SCOPE ?? process.argv[2] ?? 'all';
const scope = requestedScope.replace(/^--scope=/, '').toLowerCase();
const useDev = process.env.DAY41_43_E2E_USE_DEV_STACK === '1';
const isolatedEnv = {
  POSTGRES_USER: 'day4143_e2e',
  POSTGRES_PASSWORD: 'day4143_e2e_postgres_only',
  POSTGRES_PORT: '55443',
  REDIS_PORT: '56384',
  RABBITMQ_USER: 'day4143_e2e',
  RABBITMQ_PASSWORD: 'day4143_e2e_rabbit_only',
  RABBITMQ_PORT: '55742',
  RABBITMQ_MGMT_PORT: '55743',
  IDENTITY_PORT: '59101',
  TRIP_PORT: '59102',
  BOOKING_PORT: '59103',
  PAYMENT_PORT: '59104',
  PARCEL_PORT: '59105',
  TRACKING_PORT: '59111',
  GATEWAY_PORT: '59430',
  INTERNAL_JWT_SECRET: 'day41-43-e2e-internal-jwt-secret-at-least-32-bytes',
  EMAIL_PROVIDER: 'LOG',
  GOOGLE_OAUTH_CLIENT_ID: '',
  GOOGLE_OAUTH_CLIENT_SECRET: '',
  SYSTEM_ADMIN_BOOTSTRAP_EMAIL: 'bootstrap@day4143.test',
  SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: 'Day41-43-E2E-Only-Password-123!',
  SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME: 'Day 41-43 E2E Admin',
  FCM_DRY_RUN: 'true',
};
const env = useDev ? {} : isolatedEnv;
const urls = {
  gateway: process.env.DAY41_43_GATEWAY_URL ?? (useDev ? 'http://localhost:3000' : 'http://localhost:59430'),
  identity: process.env.DAY41_43_IDENTITY_URL ?? (useDev ? 'http://localhost:5001' : 'http://localhost:59101'),
  trip: process.env.DAY41_43_TRIP_URL ?? (useDev ? 'http://localhost:5002' : 'http://localhost:59102'),
  booking: process.env.DAY41_43_BOOKING_URL ?? (useDev ? 'http://localhost:5003' : 'http://localhost:59103'),
  payment: process.env.DAY41_43_PAYMENT_URL ?? (useDev ? 'http://localhost:5004' : 'http://localhost:59104'),
  parcel: process.env.DAY41_43_PARCEL_URL ?? (useDev ? 'http://localhost:5005' : 'http://localhost:59105'),
  tracking: process.env.DAY41_43_TRACKING_URL ?? (useDev ? 'http://localhost:3001' : 'http://localhost:59111'),
};
const containers = {
  postgres: useDev ? 'vietride_postgres' : 'day41-43-e2e-postgres',
  redis: useDev ? 'vietride_redis' : 'day41-43-e2e-redis',
  rabbitmq: useDev ? 'vietride_rabbitmq' : 'day41-43-e2e-rabbitmq',
};
const postgresUser = useDev ? process.env.POSTGRES_USER ?? 'vietride' : isolatedEnv.POSTGRES_USER;
const postgresPassword = useDev ? process.env.POSTGRES_PASSWORD ?? 'vietride_dev' : isolatedEnv.POSTGRES_PASSWORD;
const rabbitUser = useDev ? process.env.RABBITMQ_USER ?? 'vietride' : isolatedEnv.RABBITMQ_USER;
const rabbitPassword = useDev ? process.env.RABBITMQ_PASSWORD ?? 'vietride_dev' : isolatedEnv.RABBITMQ_PASSWORD;
const compose = [
  'compose',
  ...(fs.existsSync(path.join(root, '.env')) ? ['--env-file', '.env'] : []),
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day41-43-e2e.yml',
];
const serviceNames = ['identity', 'trip', 'booking', 'payment', 'parcel', 'tracking', 'gateway'];
const revenueServiceNames = ['identity', 'trip', 'booking', 'payment', 'parcel', 'gateway'];
const activeServiceNames = scope === 'revenue' ? revenueServiceNames : serviceNames;
const operatorA = '41430000-0000-4000-8000-000000000001';
const operatorB = '41430000-0000-4000-8000-000000000002';
const systemAdmin = '41430000-0000-4000-8000-000000000010';
const operatorAdminA = '41430000-0000-4000-8000-000000000011';
const operatorAdminB = '41430000-0000-4000-8000-000000000012';
const passenger = '41430000-0000-4000-8000-000000000013';
const driverA = '41430000-0000-4000-8000-000000000014';
const driverB = '41430000-0000-4000-8000-000000000015';
const driverTenantB = '41430000-0000-4000-8030-000000000000';
const stationOrigin = '41430000-0000-4000-8000-000000000101';
const stationDestination = '41430000-0000-4000-8000-000000000102';
const routeId = '41430000-0000-4000-8000-000000000103';
const vehicleTypeId = '41430000-0000-4000-8000-000000000104';
const vehicleId = '41430000-0000-4000-8000-000000000105';
const baseTrip = '41430000-0000-4000-8000-000000000106';
const routeB = '41430000-0000-4000-8030-000000000001';
const vehicleB = '41430000-0000-4000-8030-000000000002';
const tripB = '41430000-0000-4000-8030-000000000003';
const bookingB = '41430000-0000-4000-8030-000000000004';
const cancellationB = '41430000-0000-4000-8030-000000000005';
const parcelB = '41430000-0000-4000-8030-000000000006';
const bookingRevenueB = '41430000-0000-4000-8030-000000000007';
const parcelRevenueB = '41430000-0000-4000-8030-000000000008';
const refundB = '41430000-0000-4000-8030-000000000009';
const cancellationRevenueB = '41430000-0000-4000-8030-000000000013';
const ids = { operatorA, operatorB, systemAdmin, operatorAdminA, operatorAdminB, passenger };
const tokens = {};
let stackStarted = false;
const summary = new Set();
const reportMetrics = [];

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
    maxBuffer: 64 * 1024 * 1024,
    windowsHide: true,
  });
  if (result.error || result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout || result.error}`);
  }
  return result.stdout.trim();
}

function composeRun(args) {
  return run('docker', [...compose, ...args], { env });
}

function containerName(service) {
  return useDev ? `vietride_${service}` : `day41-43-e2e-${service}`;
}

function parseMemoryBytes(value) {
  const match = /^([0-9.]+)\s*([KMGT]?i?B)$/i.exec(value.trim());
  if (!match) throw new Error(`Unsupported docker memory value: ${value}`);
  const factors = {
    B: 1,
    KB: 1_000,
    KIB: 1_024,
    MB: 1_000_000,
    MIB: 1_048_576,
    GB: 1_000_000_000,
    GIB: 1_073_741_824,
    TB: 1_000_000_000_000,
    TIB: 1_099_511_627_776,
  };
  return Math.round(Number(match[1]) * factors[match[2].toUpperCase()]);
}

function sampleContainerMemoryBytes(service) {
  const usage = run('docker', [
    'stats',
    '--no-stream',
    '--format',
    '{{.MemUsage}}',
    containerName(service),
  ]).split('/')[0];
  return parseMemoryBytes(usage);
}

function reportTempFileCount(service) {
  const files = run('docker', [
    'exec',
    containerName(service),
    'find',
    '/tmp',
    '-maxdepth',
    '1',
    '-type',
    'f',
    '-name',
    'vietride-report-*.xlsx',
    '-print',
  ]);
  return files ? files.split(/\r?\n/).filter(Boolean).length : 0;
}

async function measureReport(name, service, action) {
  let peakMemoryBytes = sampleContainerMemoryBytes(service);
  const sample = () => {
    try {
      peakMemoryBytes = Math.max(peakMemoryBytes, sampleContainerMemoryBytes(service));
    } catch {
      // The final sample below remains authoritative if an intermediate sample races restart.
    }
  };
  const timer = setInterval(sample, 250);
  const startedAt = performance.now();
  try {
    const result = await action();
    sample();
    reportMetrics.push({
      report: name,
      service,
      durationMs: Math.round(performance.now() - startedAt),
      fileBytes: result.buffer.length,
      sampledPeakMemoryBytes: peakMemoryBytes,
    });
    return result;
  } finally {
    clearInterval(timer);
  }
}

function writeReportMetricsArtifact() {
  const directory = path.join(root, 'artifacts', 'day41-43');
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(
    path.join(directory, 'day41-xlsx-performance.json'),
    `${JSON.stringify({ generatedAt: new Date().toISOString(), reports: reportMetrics }, null, 2)}\n`,
    'utf8',
  );
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
const trackingSql = (statement) => sql('vietride_tracking', 'vietride_tracking', statement);
const redis = (...args) => run('docker', ['exec', containers.redis, 'redis-cli', ...args]);

function clearPlatformReportCache() {
  const keys = redis('--scan', '--pattern', 'platform-report:*').split(/\r?\n/).filter(Boolean);
  if (keys.length > 0) redis('DEL', ...keys);
  return keys.length;
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function scalar(value) {
  return String(value).split(/\r?\n/).filter(Boolean).at(-1)?.trim() ?? '';
}

function count(value) {
  return Number(scalar(value));
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function poll(fn, message, timeoutMs = 180_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await fn();
      if (last) return last;
    } catch (error) {
      last = error;
    }
    await sleep(750);
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

async function scenario(name, fn) {
  await fn();
  summary.add(name);
  console.log(`${name} PASS`);
}

async function recycleIsolatedApps(services, label) {
  composeRun(['--profile', 'app', 'stop', ...services]);
  composeRun(['--profile', 'app', 'up', '-d', '--no-deps', ...services]);
  await Promise.all(
    services.map((service) => waitFor(`${urls[service]}/${service === 'gateway' ? 'health' : 'ready'}`)),
  );
  console.log(`app containers recycled for ${label} memory isolation`);
}

async function userJwt(userId, role, operatorId) {
  const settings = JSON.parse(fs.readFileSync(
    path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
    'utf8',
  ));
  const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY ?? settings.IdentityJwt.PrivateKey, 'RS256');
  return new SignJWT({
    role,
    ...(operatorId ? { operatorId, operator_id: operatorId } : {}),
    ...(role === 'PASSENGER' ? { hasPhone: true } : {}),
    email: `${role.toLowerCase()}-${userId.slice(-4)}@day4143.test`,
  })
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID ?? settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(userId)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
}

async function internalJwt() {
  const secret = process.env.INTERNAL_JWT_SECRET ?? isolatedEnv.INTERNAL_JWT_SECRET;
  return new SignJWT({ role: 'SYSTEM_ADMIN' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setSubject(systemAdmin)
    .setIssuedAt()
    .setExpirationTime('2m')
    .sign(new TextEncoder().encode(secret));
}

async function api(method, pathname, { token, body, key, signal } = {}) {
  let response;
  const maxAttempts = method === 'GET' ? 3 : 1;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      response = await fetch(`${urls.gateway}${pathname}`, {
        method,
        headers: {
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
          ...(key ? { 'Idempotency-Key': key } : {}),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal,
      });
      break;
    } catch (error) {
      if (signal?.aborted) throw error;
      if (attempt < maxAttempts) {
        console.warn(`${method} ${pathname} transport retry ${attempt}/${maxAttempts - 1}`);
        await new Promise((resolve) => setTimeout(resolve, 500 * attempt));
        continue;
      }

      const cause = error?.cause;
      const detail = [cause?.code, cause?.message, error?.message].filter(Boolean).join(': ');
      throw new Error(`${method} ${pathname} transport failed: ${detail || String(error)}`, { cause: error });
    }
  }
  const buffer = Buffer.from(await response.arrayBuffer());
  const text = buffer.toString('utf8');
  let json;
  try { json = JSON.parse(text); } catch { json = null; }
  return { response, buffer, text, json };
}

async function directApi(service, pathname, { token, body, key, method = 'GET' } = {}) {
  const response = await fetch(`${urls[service]}${pathname}`, {
    method,
    headers: {
      ...(token ? { 'X-Internal-Auth': `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json;
  try { json = JSON.parse(text); } catch { json = null; }
  return { response, text, json };
}

function responseData(result) {
  return result.json?.data ?? result.json;
}

function ictDate(value = new Date()) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(value);
}

function assertVietnamPeriod(period, from, to, label) {
  assert(period?.from === from, `${label} period.from mismatch: ${period?.from}`);
  assert(period?.to === to, `${label} period.to mismatch: ${period?.to}`);
  assert(period?.timezone === 'Asia/Ho_Chi_Minh', `${label} timezone mismatch: ${period?.timezone}`);
}

function findMoneyFields(value, prefix = '') {
  if (Array.isArray(value)) {
    return value.flatMap((item, index) => findMoneyFields(item, `${prefix}[${index}]`));
  }
  if (!value || typeof value !== 'object') return [];
  return Object.entries(value).flatMap(([key, child]) => {
    const pathName = prefix ? `${prefix}.${key}` : key;
    const current = /(revenue|amount|fare|refund|settlement|wallet|ledger|payout|paid)/i.test(key)
      ? [pathName]
      : [];
    return current.concat(findMoneyFields(child, pathName));
  });
}

function errorCode(result) {
  return result.json?.error?.code ?? result.json?.errorCode ?? result.json?.code;
}

function expectError(result, statuses, code) {
  assert(statuses.includes(result.response.status), `Expected ${statuses}, got ${result.response.status}: ${result.text}`);
  assert(errorCode(result) === code, `Expected ${code}, got ${errorCode(result)}: ${result.text}`);
}

function reportDateRange(daysBack, now = Date.now()) {
  return {
    from: new Date(now - daysBack * 24 * 60 * 60 * 1000).toISOString().replace(/\.\d{3}Z$/, 'Z'),
    to: new Date(now + 24 * 60 * 60 * 1000).toISOString().replace(/\.\d{3}Z$/, 'Z'),
  };
}

function assertPlatformReportPeriod(result, expectedRange, label) {
  const period = result.json?.data?.period;
  assert(
    period?.from === expectedRange.from,
    `${label} period.from mismatch: expected ${expectedRange.from}, got ${period?.from}`,
  );
  assert(
    period?.to === expectedRange.to,
    `${label} period.to mismatch: expected ${expectedRange.to}, got ${period?.to}`,
  );
  assert(
    period?.timezone === 'UTC',
    `${label} period.timezone mismatch: expected UTC, got ${period?.timezone}`,
  );
  return { from: period.from, to: period.to, timezone: period.timezone };
}

function reportPath(name, range) {
  return `/v1/operator/reports/${name}/export?from=${encodeURIComponent(range.from.slice(0, 10))}&to=${encodeURIComponent(range.to.slice(0, 10))}`;
}

function parseZipEntries(buffer) {
  const eocd = buffer.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  assert(eocd >= 0, 'XLSX end-of-central-directory record is missing');
  const countEntries = buffer.readUInt16LE(eocd + 10);
  const directoryOffset = buffer.readUInt32LE(eocd + 16);
  const entries = new Map();
  let cursor = directoryOffset;
  for (let index = 0; index < countEntries; index += 1) {
    assert(buffer.readUInt32LE(cursor) === 0x02014b50, 'Invalid XLSX central-directory entry');
    const method = buffer.readUInt16LE(cursor + 10);
    const compressedSize = buffer.readUInt32LE(cursor + 20);
    const nameLength = buffer.readUInt16LE(cursor + 28);
    const extraLength = buffer.readUInt16LE(cursor + 30);
    const commentLength = buffer.readUInt16LE(cursor + 32);
    const localOffset = buffer.readUInt32LE(cursor + 42);
    const name = buffer.subarray(cursor + 46, cursor + 46 + nameLength).toString('utf8');
    const localNameLength = buffer.readUInt16LE(localOffset + 26);
    const localExtraLength = buffer.readUInt16LE(localOffset + 28);
    const dataStart = localOffset + 30 + localNameLength + localExtraLength;
    const compressed = buffer.subarray(dataStart, dataStart + compressedSize);
    const content = method === 0 ? compressed : method === 8 ? inflateRawSync(compressed) : null;
    assert(content, `Unsupported XLSX compression method ${method}`);
    entries.set(name, content);
    cursor += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

function inspectWorkbook(buffer, sheetName, headers, minimumDataRows = 0) {
  const entries = parseZipEntries(buffer);
  const workbook = entries.get('xl/workbook.xml');
  assert(workbook, 'XLSX workbook.xml is missing');
  const xml = [...entries.entries()]
    .filter(([name]) => name.endsWith('.xml'))
    .map(([, value]) => value.toString('utf8'))
    .join('\n');
  assert(xml.includes(`name="${sheetName}"`), `XLSX sheet ${sheetName} is missing`);
  for (const header of headers) assert(xml.includes(header), `XLSX header ${header} is missing`);
  const sheetXml = [...entries.entries()]
    .filter(([name]) => name.startsWith('xl/worksheets/sheet'))
    .map(([, value]) => value.toString('utf8'))
    .join('\n');
  assert(sheetXml.length > 0, 'XLSX worksheet XML is missing');
  const rows = (sheetXml.match(/<(?:[A-Za-z_][\w.-]*:)?row(?:\s|>)/g) ?? []).length;
  assert(rows >= minimumDataRows + 1, `XLSX has ${rows - 1} data rows; expected at least ${minimumDataRows}`);
  return { rows, bytes: buffer.length, xml };
}

function assertExactTenantWorkbook(workbook, reportName, expectedIds, forbiddenIdentifiers) {
  assert(
    workbook.rows === expectedIds.length + 1,
    `${reportName} tenant B row count drifted: expected ${expectedIds.length}, got ${workbook.rows - 1}`,
  );
  for (const id of expectedIds) {
    assert(workbook.xml.includes(id), `${reportName} tenant B workbook is missing ${id}`);
  }
  for (const identifier of forbiddenIdentifiers) {
    assert(!workbook.xml.includes(identifier), `${reportName} leaked tenant A aggregate ${identifier}`);
  }
}

function seed() {
  identitySql(`
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active,deleted_at)
    VALUES
      ('${operatorA}','Day 41-43 Operator A','D4143-A-BRN','D4143-A-TAX','a@day4143.test','+84910041431','APPROVED',now(),true,NULL),
      ('${operatorB}','Day 41-43 Operator B','D4143-B-BRN','D4143-B-TAX','b@day4143.test','+84910041432','APPROVED',now(),true,NULL)
    ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,registration_status='APPROVED',is_active=true,deleted_at=NULL;
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active,deleted_at)
    SELECT ('41430000-0000-4000-8000-' || lpad(g::text,12,'0'))::uuid,
           'Day 42 Benchmark Operator ' || g,
           'D4143-BRN-' || lpad(g::text,2,'0'),
           'D4143-TAX-' || lpad(g::text,2,'0'),
           'benchmark-' || g || '@day4143.test',
           '+8491400' || lpad(g::text,4,'0'),
           'APPROVED',now(),true,NULL
    FROM generate_series(3,20) AS g
    ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,registration_status='APPROVED',is_active=true,deleted_at=NULL;
  `);

  tripSql(`
    INSERT INTO stations (id,name,slug,address_street,city,ward,latitude,longitude,supports_shuttle,is_active,deleted_at)
    VALUES
      ('${stationOrigin}','Day 41-43 Origin','day4143-origin','Origin address','HCM','Ward 1',10.7700,106.7000,true,true,NULL),
      ('${stationDestination}','Day 41-43 Destination','day4143-destination','Destination address','HCM','Ward 2',10.7800,106.7100,false,true,NULL)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,is_active)
    VALUES
      ('${routeId}','${operatorA}','Day 41-43 route','${stationOrigin}','${stationDestination}',100000,true),
      ('${routeB}','${operatorB}','Day 41 tenant B route','${stationOrigin}','${stationDestination}',100000,true)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_active)
    VALUES ('${vehicleTypeId}','DAY4143','Day 41-43 vehicle',2,true)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active)
    VALUES
      ('${vehicleId}','${operatorA}','${vehicleTypeId}','51B-4143','{"rows":[]}',2,'ACTIVE',true),
      ('${vehicleB}','${operatorB}','${vehicleTypeId}','51B-4143B','{"rows":[]}',2,'ACTIVE',true)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO trips (id,operator_id,route_id,vehicle_id,seat_layout_snapshot_json,driver_user_id,departure_date_time,estimated_arrival_time,completed_at,status,source,base_fare)
    VALUES
      ('${baseTrip}','${operatorA}','${routeId}','${vehicleId}','{"rows":[]}','${driverA}',now()-interval '4 hours',now()-interval '3 hours',now()-interval '3 hours','COMPLETED','MANUAL',100000),
      ('${tripB}','${operatorB}','${routeB}','${vehicleB}','{"rows":[]}','${driverTenantB}',now()-interval '2 hours',now()-interval '1 hour',now()-interval '1 hour','COMPLETED','MANUAL',100000)
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO trips (id,operator_id,route_id,vehicle_id,seat_layout_snapshot_json,driver_user_id,departure_date_time,estimated_arrival_time,completed_at,status,source,base_fare)
    SELECT ('41430000-0000-4000-8001-' || lpad(g::text,12,'0'))::uuid,
           '${operatorA}','${routeId}','${vehicleId}','{"rows":[]}'::jsonb,'${driverB}',
           now() - interval '1 hour' - make_interval(secs => g),
           now() - interval '30 minutes' - make_interval(secs => g),
           now() - interval '30 minutes' - make_interval(secs => g),
           'COMPLETED','MANUAL',100000
    FROM generate_series(1,10000) AS g
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    SELECT gen_random_uuid(), t.id, seat.seat_number, 'STANDARD', seat.status::trip_seat_status
    FROM trips t
    CROSS JOIN (VALUES ('D01','BOOKED'),('D02','AVAILABLE')) AS seat(seat_number,status)
    WHERE t.operator_id='${operatorA}' AND t.id::text LIKE '41430000-0000-4000-8001-%'
    ON CONFLICT (trip_id,seat_number) DO NOTHING;
    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    VALUES
      (gen_random_uuid(),'${tripB}','B01','STANDARD','BOOKED'),
      (gen_random_uuid(),'${tripB}','B02','STANDARD','AVAILABLE')
    ON CONFLICT (trip_id,seat_number) DO NOTHING;
  `);

  bookingSql(`
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,confirmed_at,completed_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8002-' || lpad(g::text,12,'0'))::uuid,
           'VR-20260718-' || lpad(g::text,8,'0'),'${passenger}','${baseTrip}','${operatorA}','${stationOrigin}','${stationDestination}',100000,0,100000,'COMPLETED',now()-interval '90 minutes',now()-interval '30 minutes',now()-interval '1 hour',now()
    FROM generate_series(1,10000) AS g
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,confirmed_at,completed_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8020-' || lpad(g::text,12,'0'))::uuid,
           'VR-20260718-' || lpad((g + 100000)::text,8,'0'),'${passenger}','${baseTrip}',
           ('41430000-0000-4000-8000-' || lpad(((g - 1) % 18 + 3)::text,12,'0'))::uuid,
           '${stationOrigin}','${stationDestination}',100000,0,100000,'COMPLETED',
           now()-interval '90 minutes',now()-interval '30 minutes',now()-interval '1 hour',now()
    FROM generate_series(1,90000) AS g
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,cancellation_reason,confirmed_at,cancelled_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8003-' || lpad(g::text,12,'0'))::uuid,
           'VR-20260718-' || lpad((g + 200000)::text,8,'0'),'${passenger}','${baseTrip}','${operatorA}','${stationOrigin}','${stationDestination}',100000,0,100000,'CANCELLED','USER_INITIATED',
           now()-interval '31 days 2 minutes',now()-interval '31 days',now()-interval '32 days',now()-interval '31 days'
    FROM generate_series(1,10000) AS g
    ON CONFLICT (id) DO UPDATE SET
      status=EXCLUDED.status,
      cancellation_reason=EXCLUDED.cancellation_reason,
      confirmed_at=EXCLUDED.confirmed_at,
      cancelled_at=EXCLUDED.cancelled_at,
      created_at=EXCLUDED.created_at,
      updated_at=EXCLUDED.updated_at;
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,confirmed_at,completed_at,created_at,updated_at)
    VALUES ('${bookingB}','VR-DAY41-B-COMPLETED','${passenger}','${tripB}','${operatorB}','${stationOrigin}','${stationDestination}',100000,0,100000,'COMPLETED',now()-interval '90 minutes',now()-interval '30 minutes',now()-interval '2 hours',now())
    ON CONFLICT (id) DO UPDATE SET operator_id=EXCLUDED.operator_id,trip_id=EXCLUDED.trip_id,status=EXCLUDED.status,created_at=EXCLUDED.created_at,completed_at=EXCLUDED.completed_at,updated_at=EXCLUDED.updated_at;
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,cancellation_reason,confirmed_at,cancelled_at,created_at,updated_at)
    VALUES ('${cancellationB}','VR-DAY41-B-CANCELLED','${passenger}','${tripB}','${operatorB}','${stationOrigin}','${stationDestination}',100000,0,100000,'CANCELLED','USER_INITIATED',now()-interval '90 minutes',now()-interval '30 minutes',now()-interval '2 hours',now())
    ON CONFLICT (id) DO UPDATE SET operator_id=EXCLUDED.operator_id,trip_id=EXCLUDED.trip_id,status=EXCLUDED.status,cancellation_reason=EXCLUDED.cancellation_reason,created_at=EXCLUDED.created_at,cancelled_at=EXCLUDED.cancelled_at,updated_at=EXCLUDED.updated_at;
    INSERT INTO passengers (id,booking_id,seat_number,boarding_status)
    SELECT gen_random_uuid(), b.id, 'D01', 'PENDING'
    FROM bookings b
    WHERE b.operator_id='${operatorA}' AND (b.id::text LIKE '41430000-0000-4000-8002-%' OR b.id::text LIKE '41430000-0000-4000-8003-%')
    ON CONFLICT (booking_id,seat_number) DO NOTHING;
  `);

  parcelSql(`
    INSERT INTO parcels (id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,size_category,estimated_size_category,estimated_weight_kg,delivery_method,total_price_vnd,deposit_percent,deposit_amount,original_deposit_amount,discount_amount,additional_amount,refund_amount,status,confirmed_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8004-' || lpad(g::text,12,'0'))::uuid,
           'VRP4143-' || lpad(g::text,6,'0'),'${passenger}','Recipient ' || g,'+84910041430','${operatorA}','${baseTrip}','SMALL','SMALL',1,'TERMINAL_PICKUP',60000,100,60000,60000,0,0,0,'DELIVERY_CONFIRMED',now()-interval '45 minutes',now()-interval '1 hour',now()
    FROM generate_series(1,10000) AS g
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO parcels (id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,size_category,estimated_size_category,estimated_weight_kg,delivery_method,total_price_vnd,deposit_percent,deposit_amount,original_deposit_amount,discount_amount,additional_amount,refund_amount,status,confirmed_at,created_at,updated_at)
    VALUES ('${parcelB}','VRP-DAY41-B','${passenger}','Tenant B Recipient','+84910041432','${operatorB}','${tripB}','SMALL','SMALL',1,'TERMINAL_PICKUP',60000,100,60000,60000,0,0,0,'DELIVERY_CONFIRMED',now()-interval '45 minutes',now()-interval '1 hour',now())
    ON CONFLICT (id) DO UPDATE SET operator_id=EXCLUDED.operator_id,trip_id=EXCLUDED.trip_id,status=EXCLUDED.status,created_at=EXCLUDED.created_at,confirmed_at=EXCLUDED.confirmed_at,updated_at=EXCLUDED.updated_at;
    INSERT INTO parcels (id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,size_category,estimated_size_category,estimated_weight_kg,delivery_method,total_price_vnd,deposit_percent,deposit_amount,original_deposit_amount,discount_amount,additional_amount,refund_amount,status,confirmed_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8021-' || lpad(g::text,12,'0'))::uuid,
           'VRP4143-BM-' || lpad(g::text,6,'0'),'${passenger}','Benchmark Recipient ' || g,'+84910041430',
           ('41430000-0000-4000-8000-' || lpad(((g - 1) % 18 + 3)::text,12,'0'))::uuid,
           '${baseTrip}','SMALL','SMALL',1,'TERMINAL_PICKUP',60000,100,60000,60000,0,0,0,
           'DELIVERY_CONFIRMED',now()-interval '45 minutes',now()-interval '1 hour',now()
    FROM generate_series(1,40000) AS g
    ON CONFLICT (id) DO NOTHING;
  `);

  paymentSql(`
    INSERT INTO payments (id,reference_type,reference_id,user_id,operator_id,amount,method,status,succeeded_at,created_at,updated_at)
    SELECT ('41430000-0000-4000-8024-' || lpad(g::text,12,'0'))::uuid,
           'BOOKING',('41430000-0000-4000-8025-' || lpad(g::text,12,'0'))::uuid,
           '${passenger}',NULL,100000,'WALLET','SUCCEEDED',now()-interval '30 minutes',now()-interval '1 hour',now()
    FROM generate_series(1,100000) AS g
    ON CONFLICT (id) DO NOTHING;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),'${operatorA}','${baseTrip}','BOOKING_REVENUE',100000,'BOOKING',('41430000-0000-4000-8002-' || lpad(g::text,12,'0'))::uuid,('41430000-0000-4000-8005-' || lpad(g::text,12,'0'))::uuid,'Day 41-43 booking revenue',now()-interval '30 minutes'
    FROM generate_series(1,10000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    VALUES
      ('${bookingRevenueB}','${operatorB}','${tripB}','BOOKING_REVENUE',100000,'BOOKING','${bookingB}','41430000-0000-4000-8030-000000000010','Day 41 tenant B booking revenue',now()-interval '30 minutes'),
      ('${parcelRevenueB}','${operatorB}','${tripB}','PARCEL_REVENUE',60000,'PARCEL','${parcelB}','41430000-0000-4000-8030-000000000011','Day 41 tenant B parcel revenue',now()-interval '29 minutes'),
      ('${cancellationRevenueB}','${operatorB}','${tripB}','BOOKING_REVENUE',100000,'BOOKING','${cancellationB}','41430000-0000-4000-8030-000000000014','Day 41 tenant B cancelled-booking revenue',now()-interval '31 days 1 minute'),
      ('${refundB}','${operatorB}','${tripB}','BOOKING_REFUND',-100000,'BOOKING','${cancellationB}','41430000-0000-4000-8030-000000000012','Day 41 tenant B booking refund',now()-interval '31 days')
    ON CONFLICT (source_event_id,entry_type,reference_id) DO UPDATE SET operator_id=EXCLUDED.operator_id,trip_id=EXCLUDED.trip_id,amount=EXCLUDED.amount,created_at=EXCLUDED.created_at;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),
           ('41430000-0000-4000-8000-' || lpad(((g - 1) % 18 + 3)::text,12,'0'))::uuid,
           '${baseTrip}','BOOKING_REVENUE',100000,'BOOKING',
           ('41430000-0000-4000-8020-' || lpad(g::text,12,'0'))::uuid,
           ('41430000-0000-4000-8022-' || lpad(g::text,12,'0'))::uuid,
           'Day 42 benchmark booking revenue',now()-interval '30 minutes'
    FROM generate_series(1,90000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),'${operatorA}','${baseTrip}','PARCEL_REVENUE',60000,'PARCEL',('41430000-0000-4000-8004-' || lpad(g::text,12,'0'))::uuid,('41430000-0000-4000-8006-' || lpad(g::text,12,'0'))::uuid,'Day 41-43 parcel revenue',now()-interval '30 minutes'
    FROM generate_series(1,10000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),
           ('41430000-0000-4000-8000-' || lpad(((g - 1) % 18 + 3)::text,12,'0'))::uuid,
           '${baseTrip}','PARCEL_REVENUE',60000,'PARCEL',
           ('41430000-0000-4000-8021-' || lpad(g::text,12,'0'))::uuid,
           ('41430000-0000-4000-8023-' || lpad(g::text,12,'0'))::uuid,
           'Day 42 benchmark parcel revenue',now()-interval '30 minutes'
    FROM generate_series(1,40000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
    DELETE FROM operator_ledger_entries
    WHERE source_event_id::text LIKE '41430000-0000-4000-8007-%';
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),'${operatorA}','${baseTrip}','BOOKING_REVENUE',100000,'BOOKING',('41430000-0000-4000-8003-' || lpad(g::text,12,'0'))::uuid,('41430000-0000-4000-8027-' || lpad(g::text,12,'0'))::uuid,'Day 42 cancelled-booking revenue',now()-interval '31 days 1 minute'
    FROM generate_series(1,10000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO UPDATE SET
      operator_id=EXCLUDED.operator_id,
      trip_id=EXCLUDED.trip_id,
      amount=EXCLUDED.amount,
      note=EXCLUDED.note,
      created_at=EXCLUDED.created_at;
    INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
    SELECT gen_random_uuid(),'${operatorA}','${baseTrip}','BOOKING_REFUND',-100000,'BOOKING',('41430000-0000-4000-8003-' || lpad(g::text,12,'0'))::uuid,('41430000-0000-4000-8007-' || lpad(g::text,12,'0'))::uuid,'Day 42 cancelled-booking refund',now()-interval '31 days'
    FROM generate_series(1,10000) AS g
    ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
  `);

  const services = [
    ['identity', identitySql],
    ['trip', tripSql],
    ['booking', bookingSql],
    ['payment', paymentSql],
    ['parcel', parcelSql],
  ];
  for (const [index, [service, write]] of services.entries()) {
    const eventId = `41430000-0000-4000-8010-${String(index + 1).padStart(12, '0')}`;
    write(`INSERT INTO outbox_dlq (event_id,event_type,payload,retry_count,last_error,created_at,terminal_at) VALUES ('${eventId}','${service}.day4143.failed','{"service":"${service}"}',6,'day4143 deterministic terminal failure','2026-07-18T09:59:00Z','2026-07-18T10:00:00Z') ON CONFLICT (event_id) DO NOTHING;`);
  }
  if (scope !== 'revenue') {
    trackingSql(`INSERT INTO outbox_dlq (event_id,event_type,payload,retry_count,last_error,created_at,terminal_at) VALUES ('41430000-0000-4000-8010-000000000006','tracking.day4143.failed','{"service":"tracking"}',6,'day4143 deterministic terminal failure','2026-07-18T09:59:00Z','2026-07-18T10:00:00Z') ON CONFLICT (event_id) DO NOTHING;`);
  }
}

async function runExcelScenario() {
  const range = reportDateRange(32);
  const reports = [
    ['bookings', 'booking', 'Bookings', ['booking_id', 'booking_code', 'trip_id', 'status'], 10000, [bookingB, cancellationB]],
    ['parcels', 'parcel', 'Parcels', ['parcel_id', 'parcel_code', 'trip_id', 'status'], 10000, [parcelB]],
    ['revenue', 'payment', 'Revenue', ['entry_id', 'entry_type', 'reference_type', 'amount_vnd'], 10000, [bookingRevenueB, parcelRevenueB, cancellationRevenueB]],
    ['occupancy', 'trip', 'Occupancy', ['trip_id', 'route_id', 'status', 'occupancy_percent'], 10000, [tripB]],
    ['cancellation', 'booking', 'Cancellations', ['booking_id', 'booking_code', 'cancelled_at'], 10000, [cancellationB]],
    ['refunds', 'payment', 'Refunds', ['entry_id', 'entry_type', 'reference_type', 'amount_vnd'], 10000, [refundB]],
  ];
  const tenantAIdentifiers = [operatorA, routeId, baseTrip, '41430000-0000-4000-8001-', '41430000-0000-4000-8002-', '41430000-0000-4000-8003-', '41430000-0000-4000-8004-'];
  for (const [name, service, sheet, headers, minimumRows, tenantBIds] of reports) {
    const result = await measureReport(
      name,
      service,
      () => api('GET', reportPath(name, range), { token: tokens.operatorA }),
    );
    assert(result.response.ok, `${name} export failed: ${result.text}`);
    assert(result.response.headers.get('content-type')?.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'), `${name} MIME drifted`);
    assert((result.response.headers.get('content-disposition') ?? '').includes('.xlsx'), `${name} disposition drifted`);
    inspectWorkbook(result.buffer, sheet, headers, minimumRows);

    const tenantB = await api('GET', reportPath(name, range), { token: tokens.operatorB });
    assert(tenantB.response.ok, `${name} tenant B export failed: ${tenantB.text}`);
    const tenantWorkbook = inspectWorkbook(tenantB.buffer, sheet, headers, 0);
    assertExactTenantWorkbook(tenantWorkbook, name, tenantBIds, tenantAIdentifiers);
    assert(reportTempFileCount(service) === 0, `${name} left a report temp file after response disposal`);
  }
  const empty = await api('GET', `/v1/operator/reports/bookings/export?from=2030-01-01&to=2030-01-01`, { token: tokens.operatorA });
  assert(empty.response.ok, `Empty workbook failed: ${empty.text}`);
  inspectWorkbook(empty.buffer, 'Bookings', ['booking_id', 'booking_code'], 0);
  const invalid = await api('GET', `/v1/operator/reports/bookings/export?from=2026-01-01&to=2026-04-10`, { token: tokens.operatorA });
  expectError(invalid, [422], 'REPORT_RANGE_INVALID');
  const missingOperator = await api('GET', reportPath('bookings', range), { token: tokens.systemAdmin });
  expectError(missingOperator, [403], 'FORBIDDEN');
  const unauthenticated = await api('GET', reportPath('bookings', range));
  assert(unauthenticated.response.status === 401, unauthenticated.text);
  const csv = await api('GET', '/v1/operator/parcels/reports/export?format=csv', { token: tokens.operatorA });
  assert(csv.response.ok, `Legacy Parcel CSV failed: ${csv.text}`);
  assert(csv.response.headers.get('content-type')?.includes('text/csv'), 'Legacy Parcel CSV content type drifted');

  const abort = new AbortController();
  const abortedDownload = fetch(`${urls.gateway}${reportPath('bookings', range)}`, {
    headers: { Authorization: `Bearer ${tokens.operatorA}` },
    signal: abort.signal,
  }).then((response) => response.arrayBuffer());
  setTimeout(() => abort.abort(), 10);
  try { await abortedDownload; } catch (error) {
    assert(error?.name === 'AbortError', `Unexpected client abort error: ${error}`);
  }
  await poll(
    () => reportTempFileCount('booking') === 0,
    'Client abort left a Booking report temp file',
    5_000,
  );
  writeReportMetricsArtifact();
  console.log('gateway REST PASS');
  console.log('six XLSX + 10k + tenant isolation PASS');
  summary.add('six XLSX + 10k + tenant isolation PASS');
}

async function runRevenueScenario() {
  const checkedRequests = [];
  const to = ictDate();
  const year = Number(to.slice(0, 4));
  const month = to.slice(0, 7);
  const from = `${month}-01`;
  const exportFrom = ictDate(new Date(Date.now() - 90 * 24 * 60 * 60 * 1000));
  const rangeQuery = `from=${from}&to=${to}`;
  const internal = await internalJwt();

  clearPlatformReportCache();
  const revenueCacheKeys = redis('--scan', '--pattern', 'revenue:v2:*').split(/\r?\n/).filter(Boolean);
  if (revenueCacheKeys.length > 0) redis('DEL', ...revenueCacheKeys);

  const internalAdminResult = await directApi(
    'payment',
    `/internal/v1/revenue/admin-summary?${rangeQuery}`,
    { token: internal },
  );
  assert(internalAdminResult.response.status === 200, `Internal admin revenue failed: ${internalAdminResult.text}`);
  const internalAdmin = responseData(internalAdminResult);
  assertVietnamPeriod(internalAdmin.period, from, to, 'Internal admin revenue');
  assert(
    internalAdmin.netTransportRevenueVnd
      === internalAdmin.netTicketRevenueVnd + internalAdmin.netParcelRevenueVnd,
    `Internal admin transport revenue did not reconcile: ${internalAdminResult.text}`,
  );
  assert(
    internalAdmin.totalProjectRevenueVnd
      === internalAdmin.netTransportRevenueVnd + internalAdmin.subscriptionRevenueVnd,
    `Internal admin project revenue did not reconcile: ${internalAdminResult.text}`,
  );
  checkedRequests.push('GET Payment internal admin summary');

  const internalOperatorResult = await directApi(
    'payment',
    `/internal/v1/revenue/operators/${operatorA}/summary?${rangeQuery}`,
    { token: internal },
  );
  assert(internalOperatorResult.response.status === 200, `Internal operator revenue failed: ${internalOperatorResult.text}`);
  const internalOperator = responseData(internalOperatorResult);
  assertVietnamPeriod(internalOperator.period, from, to, 'Internal operator revenue');
  assert(internalOperator.operatorId === operatorA, `Internal operator tenant drifted: ${internalOperatorResult.text}`);
  assert(
    internalOperator.netRevenueVnd
      === internalOperator.netTicketRevenueVnd + internalOperator.netParcelRevenueVnd,
    `Internal operator revenue did not reconcile: ${internalOperatorResult.text}`,
  );
  assert(
    internalOperator.netParcelRevenueVnd
      === internalOperator.grossParcelRevenueVnd + internalOperator.parcelRefundsVnd,
    `Internal operator Parcel revenue did not reconcile: ${internalOperatorResult.text}`,
  );
  checkedRequests.push('GET Payment internal operator summary');

  const ledgerCountBeforeDryRun = count(paymentSql('SELECT count(*) FROM operator_ledger_entries;'));
  const dryRun = await directApi(
    'payment',
    '/internal/v1/revenue/backfills/parcel-voucher-reversals?dryRun=true',
    { token: internal, key: randomUUID(), method: 'POST' },
  );
  assert(dryRun.response.status === 200, `Parcel voucher reversal dry-run failed: ${dryRun.text}`);
  const dryRunData = responseData(dryRun);
  for (const field of [
    'scannedRefundCount',
    'candidateCount',
    'skippedExistingCount',
    'legacyUnclassifiedCount',
    'totalAdjustmentVnd',
    'appliedCount',
  ]) {
    assert(Number.isInteger(dryRunData?.[field]), `Backfill dry-run field ${field} drifted: ${dryRun.text}`);
  }
  assert(dryRunData.appliedCount === 0, `Backfill dry-run applied rows: ${dryRun.text}`);
  assert(
    count(paymentSql('SELECT count(*) FROM operator_ledger_entries;')) === ledgerCountBeforeDryRun,
    'Backfill dry-run mutated operator ledger rows',
  );
  checkedRequests.push('POST Payment voucher reversal backfill dry-run');

  const adminAnalyticsResult = await api(
    'GET',
    `/v1/admin/revenue/analytics?${rangeQuery}&groupBy=month&top=5`,
    { token: tokens.systemAdmin },
  );
  assert(adminAnalyticsResult.response.status === 200, `Admin revenue analytics failed: ${adminAnalyticsResult.text}`);
  const adminAnalytics = responseData(adminAnalyticsResult);
  assertVietnamPeriod(adminAnalytics.period, from, to, 'Admin revenue analytics');
  const adminRevenue = adminAnalytics.summary.revenue;
  const adminSettlement = adminAnalytics.summary.settlement;
  assert(adminRevenue.totalProjectRevenueVnd.currentValue === internalAdmin.totalProjectRevenueVnd, 'Admin total project revenue drifted from Payment summary');
  assert(adminRevenue.netTransportRevenueVnd.currentValue === internalAdmin.netTransportRevenueVnd, 'Admin transport revenue drifted from Payment summary');
  assert(adminRevenue.netTicketRevenueVnd.currentValue === internalAdmin.netTicketRevenueVnd, 'Admin ticket revenue drifted from Payment summary');
  assert(adminRevenue.netParcelRevenueVnd.currentValue === internalAdmin.netParcelRevenueVnd, 'Admin Parcel revenue drifted from Payment summary');
  assert(adminRevenue.subscriptionRevenueVnd.currentValue === internalAdmin.subscriptionRevenueVnd, 'Admin subscription revenue drifted from Payment summary');
  assert(adminSettlement.paidToOperatorsVnd.currentValue === internalAdmin.paidToOperatorsVnd, 'Admin payout drifted from Payment summary');
  assert(
    adminRevenue.totalProjectRevenueVnd.currentValue
      === adminRevenue.netTransportRevenueVnd.currentValue + adminRevenue.subscriptionRevenueVnd.currentValue,
    'Admin analytics incorrectly mixed payout into project revenue',
  );
  if (adminRevenue.totalProjectRevenueVnd.previousValue === 0
      && adminRevenue.totalProjectRevenueVnd.currentValue > 0) {
    assert(adminRevenue.totalProjectRevenueVnd.changePercent === null, 'Admin zero-baseline growth must be null');
  }
  checkedRequests.push('GET Gateway admin revenue analytics');

  const operatorMonthResult = await api(
    'GET',
    `/v1/operator/revenue/analytics?month=${month}`,
    { token: tokens.operatorA },
  );
  assert(operatorMonthResult.response.status === 200, `Operator month revenue failed: ${operatorMonthResult.text}`);
  const operatorMonth = responseData(operatorMonthResult);
  assert(operatorMonth.period.month === month, `Operator month period drifted: ${operatorMonthResult.text}`);
  assert(operatorMonth.period.timezone === 'Asia/Ho_Chi_Minh', `Operator month timezone drifted: ${operatorMonthResult.text}`);
  assert(operatorMonth.summary.netRevenueVnd.currentValue === internalOperator.netRevenueVnd, 'Operator month total drifted from internal summary');
  assert(operatorMonth.summary.netTicketRevenueVnd.currentValue === internalOperator.netTicketRevenueVnd, 'Operator month ticket revenue drifted');
  assert(operatorMonth.summary.netParcelRevenueVnd.currentValue === internalOperator.netParcelRevenueVnd, 'Operator month Parcel revenue drifted');
  checkedRequests.push('GET Gateway operator revenue month analytics');

  const operatorYearResult = await api(
    'GET',
    `/v1/operator/revenue/analytics?year=${year}&groupBy=month`,
    { token: tokens.operatorA },
  );
  assert(operatorYearResult.response.status === 200, `Operator year revenue failed: ${operatorYearResult.text}`);
  const operatorYear = responseData(operatorYearResult);
  assert(operatorYear.period.year === year && operatorYear.period.groupBy === 'month', `Operator year period drifted: ${operatorYearResult.text}`);
  assert(operatorYear.period.timezone === 'Asia/Ho_Chi_Minh', `Operator year timezone drifted: ${operatorYearResult.text}`);
  assert(operatorYear.monthly.length === 12, `Operator year must return 12 month buckets: ${operatorYearResult.text}`);
  assert(operatorYear.summary.netRevenueVnd.previousValue === 0, `Operator previous-year zero baseline drifted: ${operatorYearResult.text}`);
  assert(operatorYear.summary.netRevenueVnd.changePercent === null, `Operator zero-baseline growth must be null: ${operatorYearResult.text}`);
  checkedRequests.push('GET Gateway operator revenue year analytics');

  const dashboardResult = await api(
    'GET',
    `/v1/admin/dashboard/summary?${rangeQuery}`,
    { token: tokens.systemAdmin },
  );
  assert(dashboardResult.response.status === 200, `Admin dashboard failed: ${dashboardResult.text}`);
  const dashboard = responseData(dashboardResult);
  assertVietnamPeriod(dashboard.period, from, to, 'Admin dashboard');
  assert(dashboard.totalProjectRevenueVnd.currentValue === internalAdmin.totalProjectRevenueVnd, 'Dashboard total project revenue drifted');
  assert(dashboard.netTransportRevenueVnd.currentValue === internalAdmin.netTransportRevenueVnd, 'Dashboard transport revenue drifted');
  assert(dashboard.netTicketRevenueVnd.currentValue === internalAdmin.netTicketRevenueVnd, 'Dashboard ticket revenue drifted');
  assert(dashboard.netParcelRevenueVnd.currentValue === internalAdmin.netParcelRevenueVnd, 'Dashboard Parcel revenue drifted');
  assert(dashboard.subscriptionRevenueVnd.currentValue === internalAdmin.subscriptionRevenueVnd, 'Dashboard subscription revenue drifted');
  if (dashboard.totalProjectRevenueVnd.previousValue === 0
      && dashboard.totalProjectRevenueVnd.currentValue > 0) {
    assert(dashboard.totalProjectRevenueVnd.changePercent === null, 'Dashboard zero-baseline growth must be null');
  }
  checkedRequests.push('GET Gateway admin dashboard summary');

  const adminBookingStats = await api(
    'GET',
    `/v1/admin/booking-stats/aggregate?${rangeQuery}&groupBy=operator`,
    { token: tokens.systemAdmin },
  );
  assert(adminBookingStats.response.status === 200, `Admin booking stats failed: ${adminBookingStats.text}`);
  assert(findMoneyFields(responseData(adminBookingStats)).length === 0, `Admin booking stats leaked money fields: ${findMoneyFields(responseData(adminBookingStats)).join(', ')}`);
  checkedRequests.push('GET Gateway admin booking stats without money');

  const operatorBookingStats = await api(
    'GET',
    `/v1/operator/booking-stats?${rangeQuery}&groupBy=date`,
    { token: tokens.operatorA },
  );
  assert(operatorBookingStats.response.status === 200, `Operator booking stats failed: ${operatorBookingStats.text}`);
  assert(findMoneyFields(responseData(operatorBookingStats)).length === 0, `Operator booking stats leaked money fields: ${findMoneyFields(responseData(operatorBookingStats)).join(', ')}`);
  checkedRequests.push('GET Gateway operator booking stats without money');

  const platformResult = await api(
    'GET',
    `/v1/admin/reports/platform?${rangeQuery}`,
    { token: tokens.systemAdmin },
  );
  assert(platformResult.response.status === 200, `Platform report failed: ${platformResult.text}`);
  const platform = responseData(platformResult);
  assertVietnamPeriod(platform.period, from, to, 'Platform report');
  assert(platform.totals.netTransportRevenueVnd === platform.totals.netTicketRevenueVnd + platform.totals.netParcelRevenueVnd, 'Platform transport revenue did not reconcile');
  assert(platform.totals.netTicketRevenueVnd === internalAdmin.netTicketRevenueVnd, 'Platform ticket revenue drifted from Payment');
  assert(platform.totals.netParcelRevenueVnd === internalAdmin.netParcelRevenueVnd, 'Platform Parcel revenue drifted from Payment');
  checkedRequests.push('GET Gateway admin platform report');

  const cacheKeys = redis('--scan', '--pattern', 'platform-report:v3:*').split(/\r?\n/).filter(Boolean);
  assert(cacheKeys.length > 0, 'Platform v3 cache key missing');
  assert(cacheKeys.every((key) => {
    const ttl = Number(redis('TTL', key));
    return ttl > 0 && ttl <= 60;
  }), 'Platform v3 cache TTL must be at most 60 seconds');

  const parcelSummaryResult = await api(
    'GET',
    `/v1/operator/parcels/reports/summary?${rangeQuery}`,
    { token: tokens.operatorA },
  );
  assert(parcelSummaryResult.response.status === 200, `Parcel report summary failed: ${parcelSummaryResult.text}`);
  const parcelSummary = responseData(parcelSummaryResult);
  assert(parcelSummary.grossParcelRevenueVnd === internalOperator.grossParcelRevenueVnd, 'Parcel gross revenue drifted from Payment');
  assert(parcelSummary.parcelRefundsVnd === internalOperator.parcelRefundsVnd, 'Parcel refunds drifted from Payment');
  assert(parcelSummary.netParcelRevenueVnd === internalOperator.netParcelRevenueVnd, 'Parcel net revenue drifted from Payment');
  checkedRequests.push('GET Gateway operator Parcel report summary');

  const parcelCsvResult = await api(
    'GET',
    `/v1/operator/parcels/reports/export?${rangeQuery}&format=csv`,
    { token: tokens.operatorA },
  );
  assert(parcelCsvResult.response.status === 200, `Parcel CSV export failed: ${parcelCsvResult.text}`);
  assert(parcelCsvResult.response.headers.get('content-type')?.includes('text/csv'), 'Parcel CSV content type drifted');
  const csvLines = parcelCsvResult.text.replace(/^\uFEFF/, '').trim().split(/\r?\n/);
  assert(csvLines.length === 2, `Parcel CSV row count drifted: ${parcelCsvResult.text}`);
  const csvHeaders = csvLines[0].split(',');
  const csvValues = csvLines[1].split(',');
  const csv = Object.fromEntries(csvHeaders.map((header, index) => [header, csvValues[index]]));
  assert(Number(csv.grossParcelRevenueVnd) === parcelSummary.grossParcelRevenueVnd, 'Parcel CSV gross revenue drifted from summary');
  assert(Number(csv.parcelRefundsVnd) === parcelSummary.parcelRefundsVnd, 'Parcel CSV refunds drifted from summary');
  assert(Number(csv.netParcelRevenueVnd) === parcelSummary.netParcelRevenueVnd, 'Parcel CSV net revenue drifted from summary');
  checkedRequests.push('GET Gateway operator Parcel report CSV');

  const exportRange = { from: exportFrom, to };
  const revenueExport = await api('GET', reportPath('revenue', exportRange), { token: tokens.operatorA });
  assert(revenueExport.response.status === 200, `Payment revenue XLSX failed: ${revenueExport.text}`);
  inspectWorkbook(revenueExport.buffer, 'Revenue', ['entry_id', 'entry_type', 'reference_type', 'amount_vnd'], 1);
  checkedRequests.push('GET Gateway Payment revenue XLSX');

  const refundExport = await api('GET', reportPath('refunds', exportRange), { token: tokens.operatorA });
  assert(refundExport.response.status === 200, `Payment refund XLSX failed: ${refundExport.text}`);
  inspectWorkbook(refundExport.buffer, 'Refunds', ['entry_id', 'entry_type', 'reference_type', 'amount_vnd'], 1);
  checkedRequests.push('GET Gateway Payment refund XLSX');

  let paymentStopped = false;
  try {
    clearPlatformReportCache();
    const cachedRevenueKeys = redis('--scan', '--pattern', 'revenue:v2:*').split(/\r?\n/).filter(Boolean);
    if (cachedRevenueKeys.length > 0) redis('DEL', ...cachedRevenueKeys);
    composeRun(['--profile', 'app', 'stop', 'payment']);
    paymentStopped = true;

    const unavailableAnalytics = await api(
      'GET',
      `/v1/admin/revenue/analytics?${rangeQuery}&groupBy=month&top=5`,
      { token: tokens.systemAdmin },
    );
    assert([502, 503].includes(unavailableAnalytics.response.status), `Payment analytics did not fail closed: ${unavailableAnalytics.text}`);
    checkedRequests.push('GET Gateway admin revenue while Payment down');

    const unavailableDashboard = await api(
      'GET',
      `/v1/admin/dashboard/summary?${rangeQuery}`,
      { token: tokens.systemAdmin },
    );
    expectError(unavailableDashboard, [503], 'UPSTREAM_UNAVAILABLE');
    checkedRequests.push('GET Gateway dashboard fail-closed while Payment down');

    const unavailablePlatform = await api(
      'GET',
      `/v1/admin/reports/platform?${rangeQuery}`,
      { token: tokens.systemAdmin },
    );
    expectError(unavailablePlatform, [503], 'UPSTREAM_UNAVAILABLE');
    checkedRequests.push('GET Gateway platform report fail-closed while Payment down');

    const unavailableParcel = await api(
      'GET',
      `/v1/operator/parcels/reports/summary?${rangeQuery}`,
      { token: tokens.operatorA },
    );
    expectError(unavailableParcel, [503], 'UPSTREAM_UNAVAILABLE');
    checkedRequests.push('GET Gateway Parcel report fail-closed while Payment down');

    const statsWithoutPayment = await api(
      'GET',
      `/v1/operator/booking-stats?${rangeQuery}&groupBy=date`,
      { token: tokens.operatorA },
    );
    assert(statsWithoutPayment.response.status === 200, `Booking stats incorrectly depended on Payment: ${statsWithoutPayment.text}`);
    assert(findMoneyFields(responseData(statsWithoutPayment)).length === 0, 'Booking stats leaked money while Payment was down');
    checkedRequests.push('GET Gateway booking stats remains non-financial while Payment down');
  } finally {
    if (paymentStopped) {
      composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'payment']);
      await waitFor(`${urls.payment}/ready`);
    }
  }

  const recovered = await poll(async () => {
    const result = await directApi(
      'payment',
      `/internal/v1/revenue/admin-summary?${rangeQuery}`,
      { token: await internalJwt() },
    );
    return result.response.status === 200 ? result : false;
  }, 'Payment revenue API did not recover after restart', 90_000);
  assert(responseData(recovered).totalProjectRevenueVnd === internalAdmin.totalProjectRevenueVnd, 'Recovered Payment total drifted');
  checkedRequests.push('GET Payment internal admin summary after restart');

  for (const name of checkedRequests) console.log(`live API PASS: ${name}`);
  console.log(`${checkedRequests.length} real HTTP revenue checks PASS`);
  summary.add(`${checkedRequests.length} real HTTP revenue checks PASS`);
}

async function runPlatformScenario() {
  const platformNow = Date.now();
  const range = reportDateRange(28, platformNow);
  const expectedFrom = new Date(platformNow - 28 * 24 * 60 * 60 * 1000).toISOString().replace(/\.\d{3}Z$/, 'Z');
  const expectedTo = new Date(platformNow + 24 * 60 * 60 * 1000).toISOString().replace(/\.\d{3}Z$/, 'Z');
  assert(range.from === expectedFrom, `29-day platform from boundary drifted: ${range.from}`);
  assert(range.to === expectedTo, `29-day platform exclusive to boundary drifted: ${range.to}`);
  const rangeDurationDays = (Date.parse(range.to) - Date.parse(range.from)) / (24 * 60 * 60 * 1000);
  assert(rangeDurationDays === 29, `29-day platform range drifted: ${rangeDurationDays} days`);
  const pathname = `/v1/admin/reports/platform?from=${encodeURIComponent(range.from)}&to=${encodeURIComponent(range.to)}`;
  assert(count(bookingSql("SELECT count(*) FROM bookings WHERE status='COMPLETED';")) >= 100000, 'Day 42 booking benchmark fixture is incomplete');
  assert(count(paymentSql("SELECT count(*) FROM payments WHERE status='SUCCEEDED';")) >= 100000, 'Day 42 payment benchmark fixture is incomplete');
  assert(count(parcelSql("SELECT count(*) FROM parcels WHERE status='DELIVERY_CONFIRMED';")) >= 50000, 'Day 42 parcel benchmark fixture is incomplete');
  assert(count(tripSql("SELECT count(*) FROM trips WHERE status='COMPLETED';")) >= 10000, 'Day 42 trip benchmark fixture is incomplete');
  const legacyPaymentOwner = await fetch(`${urls.payment}${pathname}`, {
    headers: { Authorization: `Bearer ${tokens.systemAdmin}` },
  });
  assert(
    legacyPaymentOwner.status === 404,
    `Payment still exposes the Booking-owned public platform report: ${legacyPaymentOwner.status}`,
  );

  const warmupTo = new Date(Date.parse(range.to) - 1000).toISOString().replace(/\.\d{3}Z$/, 'Z');
  const warmupPathname = `/v1/admin/reports/platform?from=${encodeURIComponent(range.from)}&to=${encodeURIComponent(warmupTo)}`;
  const warmup = await api('GET', warmupPathname, { token: tokens.systemAdmin });
  assert(warmup.response.status === 200, `Platform report warm-up failed: ${warmup.text}`);
  assertPlatformReportPeriod(
    warmup,
    { from: range.from, to: warmupTo },
    'Platform report warm-up',
  );
  for (const key of redis('--scan', '--pattern', 'platform-report:*').split(/\r?\n/).filter(Boolean)) {
    redis('DEL', key);
  }

  const coldStartedAt = performance.now();
  const first = await api('GET', pathname, { token: tokens.systemAdmin });
  const coldDurationMs = Math.round(performance.now() - coldStartedAt);
  assert(first.response.status === 200 && first.json?.success === true, `Platform report failed: ${first.text}`);
  assertPlatformReportPeriod(first, range, 'Cold 29-day platform report');
  assert(first.json.data.byOperator.length === 20, `Platform benchmark operator union drifted: ${first.text}`);
  assert(first.json.data.totals.netRevenueVnd === 13_000_160_000, `Ledger total mismatch: ${first.text}`);
  assert(coldDurationMs < 2000, `Cold platform report exceeded 2s SLO: ${coldDurationMs}ms`);
  const keys = redis('--scan', '--pattern', 'platform-report:v2:*').split(/\r?\n/).filter(Boolean);
  assert(keys.length > 0, 'Platform cache key missing');
  assert(keys.some((key) => Number(redis('TTL', key)) > 0 && Number(redis('TTL', key)) <= 300), 'Platform cache TTL missing');
  const warmStartedAt = performance.now();
  const cached = await api('GET', pathname, { token: tokens.systemAdmin });
  const warmDurationMs = Math.round(performance.now() - warmStartedAt);
  assert(cached.response.status === 200, `Warm platform cache failed: ${cached.text}`);
  assertPlatformReportPeriod(cached, range, 'Warm 29-day platform report');
  assert(warmDurationMs < 2000, `Warm platform report exceeded 2s SLO: ${warmDurationMs}ms`);
  let parcelStopped = false;
  try {
    parcelStopped = true;
    composeRun(['--profile', 'app', 'stop', 'parcel']);
    const warm = await api('GET', pathname, { token: tokens.systemAdmin });
    assert(warm.response.status === 200, `Warm cache did not survive parcel outage: ${warm.text}`);
    assertPlatformReportPeriod(warm, range, 'Warm outage 29-day platform report');
    for (const key of keys) redis('DEL', key);
    const cold = await api('GET', pathname, { token: tokens.systemAdmin });
    expectError(cold, [503], 'UPSTREAM_UNAVAILABLE');
  } finally {
    if (parcelStopped) {
      composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'parcel']);
      await waitFor(`${urls.parcel}/ready`);
    }
  }

  const ledgerOnlyEventId = '41430000-0000-4000-8026-000000000001';
  try {
    paymentSql(`
      INSERT INTO operator_ledger_entries (id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at)
      VALUES (gen_random_uuid(),'${operatorA}','${baseTrip}','ADJUSTMENT',1000,'BOOKING','${baseTrip}','${ledgerOnlyEventId}','Day 42 ledger-only revenue probe',now())
      ON CONFLICT (source_event_id,entry_type,reference_id) DO NOTHING;
    `);
    const ledgerOnly = await api('GET', pathname, { token: tokens.systemAdmin });
    assert(ledgerOnly.response.status === 200, `Ledger-only revenue was rejected: ${ledgerOnly.text}`);
    assert(
      ledgerOnly.json?.data?.totals?.netRevenueVnd === 13_000_161_000,
      `Ledger-only revenue total drifted: ${ledgerOnly.text}`,
    );
  } finally {
    try {
      clearPlatformReportCache();
    } finally {
      paymentSql(`DELETE FROM operator_ledger_entries WHERE source_event_id='${ledgerOnlyEventId}';`);
    }
  }
  const recovered = await api('GET', pathname, { token: tokens.systemAdmin });
  assert(recovered.response.status === 200, `Platform report did not recover after reconciliation repair: ${recovered.text}`);
  assertPlatformReportPeriod(recovered, range, 'Recovered 29-day platform report');
  assert(
    recovered.json?.data?.totals?.netRevenueVnd === 13_000_160_000,
    `Platform report did not recover the ledger-only total: ${recovered.text}`,
  );

  const projectionChecks = [
    {
      service: 'booking',
      sql: bookingSql,
      projection: 'platform_booking_stats',
      source: 'bookings',
      sourcePredicate: "status='COMPLETED'",
      deleteSql: "DELETE FROM platform_booking_stats WHERE booking_id='41430000-0000-4000-8002-000000000001';",
      rebuildSql: 'SELECT rebuild_platform_booking_stats(); SELECT rebuild_platform_booking_stats();',
    },
    {
      service: 'trip',
      sql: tripSql,
      projection: 'platform_trip_stats',
      source: 'trips',
      sourcePredicate: "status='COMPLETED'",
      deleteSql: "DELETE FROM platform_trip_stats WHERE trip_id='41430000-0000-4000-8001-000000000001';",
      rebuildSql: 'SELECT rebuild_platform_trip_stats(); SELECT rebuild_platform_trip_stats();',
    },
    {
      service: 'parcel',
      sql: parcelSql,
      projection: 'platform_parcel_stats',
      source: 'parcels',
      sourcePredicate: "status='DELIVERY_CONFIRMED'",
      deleteSql: "DELETE FROM platform_parcel_stats WHERE parcel_id='41430000-0000-4000-8004-000000000001';",
      rebuildSql: 'SELECT rebuild_platform_parcel_stats(); SELECT rebuild_platform_parcel_stats();',
    },
  ];
  for (const check of projectionChecks) {
    for (const key of redis('--scan', '--pattern', 'platform-report:*').split(/\r?\n/).filter(Boolean)) {
      redis('DEL', key);
    }
    check.sql(check.deleteSql);
    const projectionMismatch = await api('GET', pathname, { token: tokens.systemAdmin });
    expectError(projectionMismatch, [503], 'UPSTREAM_UNAVAILABLE');
    assert(
      redis('--scan', '--pattern', 'platform-report:*').trim() === '',
      `${check.service} projection mismatch was cached`,
    );
    check.sql(check.rebuildSql);
    const projectedCount = count(check.sql(`SELECT count(*) FROM ${check.projection};`));
    const liveCount = count(check.sql(`SELECT count(*) FROM ${check.source} WHERE ${check.sourcePredicate};`));
    assert(projectedCount === liveCount, `${check.service} idempotent projection backfill drifted`);
    const projectionRecovered = await api('GET', pathname, { token: tokens.systemAdmin });
    assert(
      projectionRecovered.response.status === 200,
      `${check.service} projection did not recover after backfill: ${projectionRecovered.text}`,
    );
    assertPlatformReportPeriod(
      projectionRecovered,
      range,
      `${check.service} projection-recovered 29-day platform report`,
    );
  }

  for (const key of redis('--scan', '--pattern', 'platform-report:*').split(/\r?\n/).filter(Boolean)) {
    redis('DEL', key);
  }
  const threeMonthRange = reportDateRange(91);
  const threeMonthRangeDays = (
    Date.parse(threeMonthRange.to) - Date.parse(threeMonthRange.from)
  ) / (24 * 60 * 60 * 1000);
  assert(threeMonthRangeDays === 92, `Three-month platform range drifted: ${threeMonthRangeDays} days`);
  const threeMonthPathname = `/v1/admin/reports/platform?from=${encodeURIComponent(threeMonthRange.from)}&to=${encodeURIComponent(threeMonthRange.to)}`;
  const threeMonthStartedAt = performance.now();
  const threeMonth = await api('GET', threeMonthPathname, {
    token: tokens.systemAdmin,
    signal: AbortSignal.timeout(10_000),
  });
  const threeMonthDurationMs = Math.round(performance.now() - threeMonthStartedAt);
  assert(
    threeMonth.response.status === 200 && threeMonth.json?.success === true,
    `Three-month platform report timed out or failed: ${threeMonth.text}`,
  );
  const threeMonthPeriod = assertPlatformReportPeriod(
    threeMonth,
    threeMonthRange,
    'Three-month platform report',
  );
  assert(
    threeMonth.json.data.byOperator.length === 20,
    `Three-month platform operator union drifted: ${threeMonth.text}`,
  );
  const expectedThreeMonthTotals = {
    completedBookingCount: 100_001,
    completedTripCount: 10_002,
    deliveredParcelCount: 50_001,
    bookingRevenueVnd: 10_000_100_000,
    parcelRevenueVnd: 3_000_060_000,
    netRevenueVnd: 13_000_160_000,
  };
  for (const [metric, expected] of Object.entries(expectedThreeMonthTotals)) {
    assert(
      threeMonth.json.data.totals[metric] === expected,
      `Three-month platform ${metric} mismatch: ${threeMonth.text}`,
    );
    const reconciled = threeMonth.json.data.byOperator.reduce(
      (total, operator) => total + operator[metric],
      0,
    );
    assert(
      reconciled === expected,
      `Three-month platform ${metric} did not reconcile by operator: ${threeMonth.text}`,
    );
  }

  const artifactDirectory = path.join(root, 'artifacts', 'day41-43');
  fs.mkdirSync(artifactDirectory, { recursive: true });
  fs.writeFileSync(
    path.join(artifactDirectory, 'day42-platform-performance.json'),
    `${JSON.stringify({
      generatedAt: new Date().toISOString(),
      fixture: { operators: 20, bookings: 100000, payments: 100000, parcels: 50000, trips: 10000 },
      coldDurationMs,
      warmDurationMs,
      threeMonthRange: threeMonthPeriod,
      threeMonthDurationMs,
      threeMonthStatus: threeMonth.response.status,
      sampledMemoryBytes: {
        booking: sampleContainerMemoryBytes('booking'),
        payment: sampleContainerMemoryBytes('payment'),
        parcel: sampleContainerMemoryBytes('parcel'),
      },
    }, null, 2)}\n`,
    'utf8',
  );
  console.log('platform aggregate + Redis cache PASS');
}

async function runReliabilityScenario() {
  const internal = await internalJwt();
  const dlq = await api('GET', '/v1/admin/outbox/dlq?pageSize=100', { token: tokens.systemAdmin });
  assert(dlq.response.status === 200 && dlq.json?.data?.items?.length >= 6, `DLQ facade missing sources: ${dlq.text}`);
  assert(new Set(dlq.json.data.items.map((item) => item.service)).size >= 6, `DLQ service aggregation incomplete: ${dlq.text}`);

  const pagedEventIds = [];
  let cursor = null;
  for (let page = 0; page < 3; page += 1) {
    const query = cursor
      ? `/v1/admin/outbox/dlq?pageSize=2&cursor=${encodeURIComponent(cursor)}`
      : '/v1/admin/outbox/dlq?pageSize=2';
    const response = await api('GET', query, { token: tokens.systemAdmin });
    assert(response.response.status === 200, `DLQ cursor page ${page + 1} failed: ${response.text}`);
    assert(response.json.data.items.length === 2, `DLQ cursor page ${page + 1} was incomplete: ${response.text}`);
    pagedEventIds.push(...response.json.data.items.map((item) => item.eventId));
    cursor = response.json.data.nextCursor;
    assert(page === 2 ? cursor === null : typeof cursor === 'string', `DLQ nextCursor drifted on page ${page + 1}`);
  }
  assert(new Set(pagedEventIds).size === 6, 'DLQ composite cursor duplicated or skipped an event');

  const forbidden = await api('GET', '/v1/admin/outbox/dlq', { token: tokens.operatorA });
  assert(forbidden.response.status === 403, forbidden.text);
  const invalid = await api('GET', '/v1/admin/outbox/dlq?pageSize=101', { token: tokens.systemAdmin });
  expectError(invalid, [422], 'VALIDATION_ERROR');

  if (!useDev) {
    composeRun(['--profile', 'app', 'stop', 'tracking']);
    const degraded = await api('GET', '/v1/admin/outbox/dlq?pageSize=100', { token: tokens.systemAdmin });
    assert(degraded.response.status === 200, `DLQ degraded-source response failed: ${degraded.text}`);
    assert(degraded.json.data.unavailableServices.includes('tracking'), `DLQ did not report Tracking unavailable: ${degraded.text}`);
    assert(!Object.hasOwn(degraded.json.data, 'total'), 'DLQ facade fabricated a partial total');
    assert(degraded.json.data.items.some((item) => item.service !== 'tracking'), 'DLQ degraded response lost available sources');
    composeRun(['--profile', 'app', 'up', '-d', '--no-deps', 'tracking']);
    await waitFor(`${urls.tracking}/ready`);
  }

  const jobs = ['identity', 'trip', 'booking', 'payment', 'parcel'];
  for (const service of jobs) {
    const response = await directApi(service, '/internal/jobs/status', { token: internal });
    assert(response.response.status === 200 && Array.isArray(response.json) && response.json.length > 0, `${service} job health failed: ${response.text}`);
    for (const row of response.json) {
      assert(
        JSON.stringify(Object.keys(row).sort()) === JSON.stringify(['jobId', 'lagSeconds', 'lastRun', 'nextRun', 'status'].sort()),
        `${service} job DTO exposed unexpected fields`,
      );
      assert(typeof row.jobId === 'string' && typeof row.status === 'string', `${service} job DTO drifted`);
      assert(row.lastRun === null || Number.isFinite(Date.parse(row.lastRun)), `${service} lastRun is not UTC timestamp/null`);
      assert(row.nextRun === null || Number.isFinite(Date.parse(row.nextRun)), `${service} nextRun is not UTC timestamp/null`);
      if (row.nextRun === null) {
        assert(row.lagSeconds === null, `${service} disabled job must have null lag`);
      } else {
        const expectedLag = Math.max(0, Math.floor((Date.now() - Date.parse(row.nextRun)) / 1000));
        assert(Number.isInteger(row.lagSeconds) && Math.abs(row.lagSeconds - expectedLag) <= 5, `${service} lag formula drifted`);
      }
    }
    const unauthorized = await fetch(`${urls[service]}/internal/jobs/status`);
    assert(unauthorized.status === 401, `${service} job health accepted missing Internal JWT`);
    const userToken = await directApi(service, '/internal/jobs/status', { token: tokens.systemAdmin });
    assert(userToken.response.status === 401, `${service} job health accepted a user JWT as Internal JWT`);
  }

  const mutations = [
    ['/v1/bookings', tokens.passenger],
    ['/v1/wallet/top-up', tokens.passenger],
    ['/v1/parcels', tokens.passenger],
  ];
  for (const [pathname, token] of mutations) {
    const response = await api('POST', pathname, { token, body: {} });
    expectError(response, [400, 422], 'IDEMPOTENCY_KEY_REQUIRED');
  }
  await runRabbitChaos();
  console.log('DLQ + idempotency + Hangfire job health PASS');
}

async function runRabbitChaos() {
  const eventId = '41430000-0000-4000-8020-000000000001';
  const queueConnection = await amqp.connect({ hostname: '127.0.0.1', port: Number(env.RABBITMQ_PORT ?? 55742), username: rabbitUser, password: rabbitPassword });
  const channel = await queueConnection.createChannel();
  const queue = `day4143-chaos-${randomUUID()}`;
  await channel.assertExchange('vietride.events', 'topic', { durable: true });
  await channel.assertQueue(queue, { durable: true });
  await channel.bindQueue(queue, 'vietride.events', 'booking.day4143.chaos');
  await queueConnection.close();
  composeRun(['--profile', 'app', 'stop', 'rabbitmq']);
  bookingSql(`INSERT INTO outbox_events (id,event_type,payload,status,retry_count,last_error,created_at) VALUES ('${eventId}','booking.day4143.chaos','{"chaos":"recover"}','PENDING',0,NULL,now()) ON CONFLICT (id) DO UPDATE SET status='PENDING',retry_count=0,last_error=NULL,published_at=NULL;`);
  await poll(() => count(bookingSql(`SELECT count(*) FROM outbox_events WHERE id='${eventId}' AND retry_count > 0`)) === 1, 'Outbox did not retain retry while RabbitMQ was down', 180_000);
  composeRun(['--profile', 'infra', 'up', '-d', '--wait', 'rabbitmq']);
  await poll(() => scalar(bookingSql(`SELECT status::text FROM outbox_events WHERE id='${eventId}'`)) === 'PUBLISHED', 'Outbox did not drain after RabbitMQ restart', 120_000);
  const received = await poll(() => count(run('docker', ['exec', containers.rabbitmq, 'rabbitmqctl', 'list_queues', 'name', 'messages', '--quiet']).split(/\r?\n/).filter((line) => line.includes(queue)).map((line) => line.trim().split(/\s+/).at(-1)).join('\n')) === 1, 'RabbitMQ did not receive exactly one recovered event', 30_000);
  assert(received, 'Recovered event count was not exactly one');

  const terminalId = '41430000-0000-4000-8020-000000000002';
  composeRun(['--profile', 'app', 'stop', 'rabbitmq']);
  bookingSql(`INSERT INTO outbox_events (id,event_type,payload,status,retry_count,last_error,created_at) VALUES ('${terminalId}','booking.day4143.terminal','{"chaos":"dlq"}','PENDING',5,NULL,now()) ON CONFLICT (id) DO UPDATE SET status='PENDING',retry_count=5,last_error=NULL,published_at=NULL;`);
  await poll(() => count(bookingSql(`SELECT count(*) FROM outbox_dlq WHERE event_id='${terminalId}'`)) === 1, 'Exhausted Outbox event did not enter DLQ', 180_000);
  composeRun(['--profile', 'infra', 'up', '-d', '--wait', 'rabbitmq']);
  const visible = await api('GET', '/v1/admin/outbox/dlq?service=booking&pageSize=100', { token: tokens.systemAdmin });
  assert(visible.response.status === 200 && visible.json.data.items.some((item) => item.eventId === terminalId), `DLQ terminal event was not visible through Identity: ${visible.text}`);
}

function createScratchDatabase(name) {
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
    `DROP DATABASE IF EXISTS ${name};`,
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
    `CREATE DATABASE ${name};`,
  ]);
}

function runMigrationGate() {
  if (useDev && process.env.DAY41_43_E2E_ALLOW_DEV_MIGRATION_GATE !== '1') {
    console.log('migration up/down/reapply SKIP (dev stack safety)');
    return;
  }

  const services = [
    {
      name: 'identity',
      project: 'apps/identity/src/VietRide.Identity.Infrastructure',
      envKey: 'IDENTITY_DESIGN_CONNECTION',
      schema: 'vietride_identity',
      previous: '20260716182216_AddStationAuditActions',
      migrations: ['20260718093745_AddOutboxDlq', '20260718094548_CreateOutboxDlq'],
    },
    {
      name: 'trip',
      project: 'apps/trip/src/VietRide.Trip.Infrastructure',
      envKey: 'TRIP_DESIGN_CONNECTION',
      schema: 'vietride_trip',
      previous: '20260716194532_AddCompletedTripReportIndex',
      migrations: ['20260718094030_AddOutboxDlq', '20260718145841_AddPlatformTripStatsProjection'],
      projectionTable: 'platform_trip_stats',
    },
    {
      name: 'booking',
      project: 'apps/booking/src/VietRide.Booking.Infrastructure',
      envKey: 'BOOKING_DESIGN_CONNECTION',
      schema: 'vietride_booking',
      previous: '20260716191518_AddCompletedBookingReportIndex',
      migrations: ['20260718094125_AddOutboxDlq', '20260718094657_CreateOutboxDlq', '20260718145339_AddPlatformBookingStatsProjection'],
      projectionTable: 'platform_booking_stats',
    },
    {
      name: 'payment',
      project: 'apps/payment/src/VietRide.Payment.Infrastructure',
      envKey: 'PAYMENT_DESIGN_CONNECTION',
      schema: 'vietride_payment',
      previous: '20260713134210_AddInvoiceNumberCounterRangeCheck',
      migrations: ['20260718094147_AddOutboxDlq', '20260718094817_CreateOutboxDlq'],
    },
    {
      name: 'parcel',
      project: 'apps/parcel/src/VietRide.Parcel.Infrastructure',
      envKey: 'PARCEL_DESIGN_CONNECTION',
      schema: 'vietride_parcel',
      previous: '20260716201420_AddConfirmedParcelReportIndex',
      migrations: ['20260718094207_AddOutboxDlq', '20260718094939_CreateOutboxDlq', '20260718150224_AddPlatformParcelStatsProjection'],
      projectionTable: 'platform_parcel_stats',
    },
  ];
  const port = useDev ? process.env.POSTGRES_PORT ?? '5432' : isolatedEnv.POSTGRES_PORT;
  for (const service of services) {
    const database = `day4143_${service.name}_migration`;
    createScratchDatabase(database);
    const connection = `Host=127.0.0.1;Port=${port};Database=${database};Username=${postgresUser};Password=${postgresPassword}`;
    const migrate = (target) => run(
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
      ],
      { env: { [service.envKey]: connection } },
    );
    migrate();
    const applied = sql(database, service.schema, 'SELECT "MigrationId" FROM "__ef_migrations_history" ORDER BY "MigrationId";');
    for (const migration of service.migrations) assert(applied.includes(migration), `${service.name} migration missing: ${migration}`);
    migrate(service.previous);
    assert(count(sql(database, service.schema, `SELECT count(*) FROM information_schema.tables WHERE table_schema='${service.schema}' AND table_name='outbox_dlq';`)) === 0, `${service.name} rollback left outbox_dlq`);
    if (service.projectionTable) {
      assert(count(sql(database, service.schema, `SELECT count(*) FROM information_schema.tables WHERE table_schema='${service.schema}' AND table_name='${service.projectionTable}';`)) === 0, `${service.name} rollback left ${service.projectionTable}`);
    }
    migrate();
    assert(count(sql(database, service.schema, `SELECT count(*) FROM information_schema.tables WHERE table_schema='${service.schema}' AND table_name='outbox_dlq';`)) === 1, `${service.name} reapply did not restore outbox_dlq`);
    assert(count(sql(database, service.schema, `SELECT count(*) FROM pg_indexes WHERE schemaname='${service.schema}' AND tablename='outbox_dlq' AND indexname='idx_outbox_dlq_terminal_event_id' AND indexdef LIKE '%terminal_at%event_id%';`)) === 1, `${service.name} DLQ event cursor index drifted`);
    assert(count(sql(database, service.schema, `SELECT count(*) FROM pg_indexes WHERE schemaname='${service.schema}' AND tablename='outbox_dlq' AND indexname='idx_outbox_dlq_terminal_id';`)) === 0, `${service.name} retained the storage-row cursor index`);
    if (service.projectionTable) {
      assert(count(sql(database, service.schema, `SELECT count(*) FROM information_schema.tables WHERE table_schema='${service.schema}' AND table_name='${service.projectionTable}';`)) === 1, `${service.name} reapply did not restore ${service.projectionTable}`);
    }
  }

  const trackingDatabase = 'day4143_tracking_migration';
  createScratchDatabase(trackingDatabase);
  const databaseUrl = `postgresql://${postgresUser}:${postgresPassword}@127.0.0.1:${port}/${trackingDatabase}`;
  const deployTracking = () => run(
    process.execPath,
    [path.join(root, 'node_modules/prisma/build/index.js'), 'migrate', 'deploy', '--schema=apps/tracking/prisma/schema.prisma'],
    { env: { DATABASE_URL: databaseUrl, TRACKING_DATABASE_URL: databaseUrl } },
  );
  deployTracking();
  assert(count(sql(trackingDatabase, 'public', `SELECT count(*) FROM information_schema.tables WHERE table_schema='vietride_tracking' AND table_name='outbox_dlq';`)) === 1, 'Tracking migration did not create outbox_dlq');
  sql(trackingDatabase, 'public', `DROP TABLE vietride_tracking.outbox_dlq; DELETE FROM public._prisma_migrations WHERE migration_name='20260718173000_add_outbox_dlq';`);
  assert(count(sql(trackingDatabase, 'public', `SELECT count(*) FROM information_schema.tables WHERE table_schema='vietride_tracking' AND table_name='outbox_dlq';`)) === 0, 'Tracking rollback left outbox_dlq');
  deployTracking();
  assert(count(sql(trackingDatabase, 'public', `SELECT count(*) FROM information_schema.tables WHERE table_schema='vietride_tracking' AND table_name='outbox_dlq';`)) === 1, 'Tracking migration reapply did not restore outbox_dlq');
  assert(count(sql(trackingDatabase, 'public', `SELECT count(*) FROM pg_indexes WHERE schemaname='vietride_tracking' AND tablename='outbox_dlq' AND indexname='idx_outbox_dlq_terminal_event_id' AND indexdef LIKE '%terminal_at%event_id%';`)) === 1, 'Tracking DLQ event cursor index drifted');
}

async function runAcceptance() {
  run('docker', ['version']);
  if (scope === 'all' || scope === '43' || scope === 'day43') {
    run('node', ['scripts/verify-idempotency-inventory.mjs']);
    summary.add('idempotency inventory PASS');
    console.log('idempotency inventory PASS');
  }
  if (!useDev) {
    composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
    composeRun(['--profile', 'infra', 'up', '-d', '--wait', 'postgres', 'redis', 'rabbitmq']);
    composeRun(['--profile', 'app', 'up', '-d', '--build', '--no-deps', ...activeServiceNames]);
    stackStarted = true;
  }
  await Promise.all(activeServiceNames.map((service) => waitFor(`${urls[service]}/${service === 'gateway' ? 'health' : 'ready'}`)));
  seed();
  tokens.systemAdmin = await userJwt(systemAdmin, 'SYSTEM_ADMIN');
  tokens.operatorA = await userJwt(operatorAdminA, 'OPERATOR_ADMIN', operatorA);
  tokens.operatorB = await userJwt(operatorAdminB, 'OPERATOR_ADMIN', operatorB);
  tokens.passenger = await userJwt(passenger, 'PASSENGER');
  summary.add('seed');
  console.log('seed PASS');

  if (!useDev && scope === 'all') {
    composeRun(['--profile', 'app', 'stop', '-t', '30', 'tracking']);
    console.log('tracking paused for Day 41-42 memory isolation');
  }

  if (scope === 'all' || scope === '41' || scope === 'day41') await scenario('gateway REST PASS', runExcelScenario);
  if (scope === 'revenue') await scenario('real-stack revenue API', runRevenueScenario);
  if (!useDev && scope === 'all') {
    await recycleIsolatedApps(serviceNames.filter((service) => service !== 'tracking'), 'Day 42');
  }
  if (scope === 'all' || scope === '42' || scope === 'day42') await scenario('platform aggregate + Redis cache PASS', runPlatformScenario);
  if (!useDev && scope === 'all') {
    await recycleIsolatedApps(serviceNames, 'Day 43');
  }
  if (scope === 'all' || scope === '43' || scope === 'day43') await scenario('DLQ + idempotency + Hangfire job health PASS', runReliabilityScenario);
  if (scope === 'all' || scope === '43' || scope === 'day43') await scenario('migration up/down/reapply PASS', async () => runMigrationGate());

  const required = scope === '41' || scope === 'day41'
    ? ['seed', 'gateway REST PASS', 'six XLSX + 10k + tenant isolation PASS']
    : scope === '42' || scope === 'day42'
      ? ['seed', 'platform aggregate + Redis cache PASS']
      : scope === '43' || scope === 'day43'
        ? ['seed', 'idempotency inventory PASS', 'DLQ + idempotency + Hangfire job health PASS', 'migration up/down/reapply PASS']
        : scope === 'revenue'
          ? ['seed', 'real-stack revenue API', '20 real HTTP revenue checks PASS']
          : ['seed', 'idempotency inventory PASS', 'gateway REST PASS', 'platform aggregate + Redis cache PASS', 'DLQ + idempotency + Hangfire job health PASS', 'migration up/down/reapply PASS'];
  const missing = required.filter((item) => !summary.has(item));
  assert(missing.length === 0, `Summary gates missing: ${missing.join(', ')}`);
  console.log(`${scope} acceptance PASS`);
}

let failure;
try {
  await runAcceptance();
} catch (error) {
  failure = error;
  console.error(error instanceof Error ? error.stack : error);
} finally {
  if (!useDev && stackStarted) {
    if (failure) {
      try {
        const states = run('docker', [
          'inspect',
          '--format',
          '{{.Name}} status={{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} error={{.State.Error}}',
          ...activeServiceNames.map(containerName),
        ]);
        console.error(`[failure diagnostics: container states]\n${states}`);
      } catch (diagnosticError) {
        console.error(`diagnostic capture FAIL (container states): ${diagnosticError instanceof Error ? diagnosticError.message : String(diagnosticError)}`);
      }
      for (const service of ['gateway', 'booking']) {
        try {
          const logs = composeRun(['logs', '--no-color', '--tail', '200', service]);
          console.error(`[failure diagnostics: ${service}]\n${logs}`);
        } catch (diagnosticError) {
          console.error(`diagnostic capture FAIL (${service}): ${diagnosticError instanceof Error ? diagnosticError.message : String(diagnosticError)}`);
        }
      }
    }
    try {
      composeRun(['--profile', 'infra', '--profile', 'app', 'down', '-v', '--remove-orphans']);
      console.log('cleanup PASS');
    } catch (cleanupError) {
      console.error(`cleanup FAIL: ${cleanupError instanceof Error ? cleanupError.message : String(cleanupError)}`);
      failure ??= cleanupError;
    }
  }
}

if (failure) process.exitCode = 1;
