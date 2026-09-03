// Focused live HTTP/broker E2E. No image builds, compose projects, or full-day runners.
// Prerequisite: existing vietride_postgres/redis/rabbitmq and current Release app builds.
// All simulated fixtures live in new databases and an isolated RabbitMQ vhost; finally cleans them.
import { spawn, spawnSync } from 'node:child_process';
import { createHmac, generateKeyPairSync, randomUUID, sign } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import assert from 'node:assert/strict';

const root = process.cwd();
const tag = randomUUID().replaceAll('-', '').slice(0, 10);
const prefix = `pcl_e2e_${tag}`;
const services = ['identity', 'trip', 'payment', 'parcel'];
const ports = { gateway: 18100, identity: 18101, trip: 18102, payment: 18104, parcel: 18105 };
const urls = Object.fromEntries(
  Object.entries(ports).map(([key, port]) => [key, `http://127.0.0.1:${port}`]),
);
const databases = Object.fromEntries(services.map((service) => [service, `${prefix}_${service}`]));
const reportDirectory = path.resolve(root, 'artifacts/parcel-compensation-focused-e2e', tag);
fs.mkdirSync(reportDirectory, { recursive: true });
const secret = randomUUID() + randomUUID();
const password = randomUUID().replaceAll('-', '');
const { privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
const pem = privateKey.export({ type: 'pkcs8', format: 'pem' });
const children = [];
const createdDatabases = [];
let roleCreated = false;
let rabbitUserCreated = false;
let vhostCreated = false;
const report = {
  tag,
  startedAt: new Date().toISOString(),
  result: 'RUNNING',
  scope:
    'Simulated users/one trip/accepted lost parcels; real Gateway, JWT, HTTP, PostgreSQL, Redis, RabbitMQ and Payment payout.',
  excluded:
    'Registration/email, booking, carriage/check-in/search SLA, VNPay, Notification, full-day suites and Docker builds.',
  resources: {},
  checks: [],
  http: [],
  cleanup: [],
};
const ids = Object.fromEntries(
  [
    'operator',
    'foreignOperator',
    'admin',
    'foreignAdmin',
    'staff',
    'driver',
    'assistant',
    'passenger',
    'origin',
    'destination',
    'vehicleType',
    'vehicle',
    'route',
    'trip',
  ].map((key) => [key, randomUUID()]),
);
report.resources = { ...ids, databases };

function run(file, args, input) {
  const result = spawnSync(file, args, {
    encoding: 'utf8',
    windowsHide: true,
    input,
    maxBuffer: 8 * 1024 * 1024,
  });
  if (result.status !== 0)
    throw new Error(
      `${file} failed: ${(result.stderr || result.stdout || '').replaceAll(password, '[redacted]')}`,
    );
  return result.stdout.trim();
}
function sql(service, statement) {
  const database = service === 'postgres' ? 'postgres' : databases[service];
  assert(
    database === 'postgres' || createdDatabases.includes(database),
    'Only this run may be addressed',
  );
  return run(
    'docker',
    [
      'exec',
      '-i',
      'vietride_postgres',
      'psql',
      '-U',
      'vietride',
      '-d',
      database,
      '-v',
      'ON_ERROR_STOP=1',
      '-qAt',
    ],
    service === 'postgres'
      ? statement
      : `SET search_path TO vietride_${service},public;\n${statement}`,
  );
}
function rows(service, statement) {
  return JSON.parse(sql(service, `SELECT COALESCE(json_agg(t), '[]') FROM (${statement}) t;`));
}
function pass(label, details) {
  report.checks.push({ label, details });
  console.log(`PASS | ${label}`);
}
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function poll(fn, label, timeout = 90000) {
  const end = Date.now() + timeout;
  let last;
  while (Date.now() < end) {
    try {
      const result = await fn();
      if (result) return result;
    } catch (error) {
      last = error.message;
    }
    const exited = children.find((child) => child.process.exitCode !== null);
    if (exited)
      throw new Error(`${exited.name} exited (${exited.process.exitCode}); see ${exited.log}`);
    await sleep(750);
  }
  throw new Error(`${label} timed out: ${last ?? 'condition not met'}`);
}
function token(subject, role, operatorId) {
  const header = Buffer.from(JSON.stringify({ alg: 'RS256', kid: prefix })).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const payload = Buffer.from(
    JSON.stringify({
      iss: 'vietride-identity',
      aud: 'vietride-api',
      sub: subject,
      role,
      hasPhone: 'true',
      iat: now,
      exp: now + 3600,
      ...(operatorId
        ? {
            operatorId,
            operator_id: operatorId,
            operatorStatus: 'APPROVED',
            operator_status: 'APPROVED',
          }
        : {}),
    }),
  ).toString('base64url');
  return `${header}.${payload}.${sign('RSA-SHA256', Buffer.from(`${header}.${payload}`), privateKey).toString('base64url')}`;
}
const tokens = {
  admin: token(ids.admin, 'OPERATOR_ADMIN', ids.operator),
  foreignAdmin: token(ids.foreignAdmin, 'OPERATOR_ADMIN', ids.foreignOperator),
  staff: token(ids.staff, 'OPERATOR_STAFF', ids.operator),
  driver: token(ids.driver, 'DRIVER', ids.operator),
  assistant: token(ids.assistant, 'ASSISTANT', ids.operator),
  passenger: token(ids.passenger, 'PASSENGER'),
};
async function request(method, route, { actor = 'admin', body, key, expected = 200, error } = {}) {
  const headers = { authorization: `Bearer ${tokens[actor]}`, 'x-request-id': randomUUID() };
  if (body !== undefined) headers['content-type'] = 'application/json';
  if (key) headers['idempotency-key'] = key;
  const response = await fetch(urls.gateway + route, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(20000),
  });
  const json = await response.json();
  report.http.push({
    method,
    route,
    actor,
    status: response.status,
    expected,
    errorCode: json.error?.code,
    traceId: json.meta?.traceId,
  });
  assert.equal(response.status, expected, `${method} ${route}: ${JSON.stringify(json)}`);
  assert.equal(json.statusCode, expected, 'ADR 0004 statusCode');
  assert.equal(json.success, expected < 400, 'ADR 0004 success');
  assert.ok(json.meta?.traceId, 'ADR 0004 traceId');
  if (error) assert.equal(json.error?.code, error);
  return json.data;
}
function environment(service) {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: urls[service],
    INTERNAL_JWT_SECRET: secret,
    InternalJwt__Secret: secret,
    ConnectionStrings__Default: `Host=127.0.0.1;Port=5432;Database=${databases[service]};Username=${prefix};Password=${password};Maximum Pool Size=8`,
    DOTNET_gcServer: '0',
    DOTNET_GCHeapHardLimit: '10000000',
    Hangfire__WorkerCount: '1',
    RabbitMq__HostName: '127.0.0.1',
    RabbitMq__Port: '5672',
    RabbitMq__VirtualHost: prefix,
    RabbitMq__UserName: prefix,
    RabbitMq__Password: password,
    RabbitMq__Outbox__PollInterval: '00:00:01',
    REDIS_URL: '127.0.0.1:6379,abortConnect=false,defaultDatabase=14',
    REDIS_HOST: '127.0.0.1',
    REDIS_PORT: '6379',
    Serilog__MinimumLevel__Default: 'Warning',
    Serilog__MinimumLevel__Override__Microsoft: 'Warning',
    USER_JWT_PRIVATE_KEY: pem,
    USER_JWT_KID: prefix,
    SYSTEM_ADMIN_BOOTSTRAP_EMAIL: `${prefix}@example.invalid`,
    SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: `Test!${password}A1`,
    FIREBASE_PROJECT_ID: '',
    FIREBASE_CLIENT_EMAIL: '',
    FIREBASE_PRIVATE_KEY: '',
    EMAIL_PROVIDER: 'LOG',
    EMAIL_SERVICE_BASE_URL: 'http://127.0.0.1:9',
    Identity__BaseUrl: urls.identity,
    IDENTITY_SERVICE_BASE_URL: urls.identity,
    Trip__BaseUrl: urls.trip,
    TRIP_SERVICE_BASE_URL: urls.trip,
    Payment__BaseUrl: urls.payment,
    PAYMENT_SERVICE_BASE_URL: urls.payment,
    Parcel__BaseUrl: urls.parcel,
    PARCEL_SERVICE_BASE_URL: urls.parcel,
    Booking__BaseUrl: 'http://127.0.0.1:9',
    BOOKING_BASE_URL: 'http://127.0.0.1:9',
    BOOKING_SERVICE_BASE_URL: 'http://127.0.0.1:9',
    Notification__BaseUrl: 'http://127.0.0.1:9',
    NOTIFICATION_SERVICE_BASE_URL: 'http://127.0.0.1:9',
    ROUTING_PROVIDER: 'LOCAL',
    PARCEL_QUOTE_TOKEN_SECRET: secret,
    NODE_ENV: 'development',
    GATEWAY_PORT: String(ports.gateway),
    JWT_PUBLIC_KEY_URL: urls.identity + '/v1/.well-known/jwks.json',
    NX_FILE_TO_RUN: path.join(root, 'dist/apps/gateway/main.js'),
    NX_MAPPINGS: JSON.stringify(
      Object.fromEntries(
        [
          'contracts',
          'nest-common',
          'nest-config',
          'nest-persistence',
          'nest-rabbitmq',
          'nest-redis',
        ].map((name) => [`@vietride/${name}`, path.join(root, `dist/libs/shared/${name}`)]),
      ),
    ),
    IDENTITY_BASE_URL: urls.identity,
    TRIP_BASE_URL: urls.trip,
    PAYMENT_BASE_URL: urls.payment,
    PARCEL_BASE_URL: urls.parcel,
    TRACKING_BASE_URL: 'http://127.0.0.1:9',
    NOTIFICATION_BASE_URL: 'http://127.0.0.1:9',
    RAG_BASE_URL: 'http://127.0.0.1:9',
  };
}
async function setup() {
  for (const port of Object.values(ports))
    await new Promise((resolve, reject) => {
      const server = net.createServer();
      server.once('error', reject);
      server.listen(port, '127.0.0.1', () => server.close(resolve));
    });
  sql('postgres', `CREATE ROLE ${prefix} LOGIN PASSWORD '${password}' CREATEDB;`);
  roleCreated = true;
  for (const database of Object.values(databases)) {
    assert.match(database, /^pcl_e2e_[a-f0-9]{10}_(identity|trip|payment|parcel)$/);
    sql('postgres', `CREATE DATABASE ${database} OWNER ${prefix};`);
    createdDatabases.push(database);
  }
  run('docker', ['exec', 'vietride_rabbitmq', 'rabbitmqctl', 'add_vhost', prefix]);
  vhostCreated = true;
  run('docker', ['exec', 'vietride_rabbitmq', 'rabbitmqctl', 'add_user', prefix, password]);
  rabbitUserCreated = true;
  run('docker', [
    'exec',
    'vietride_rabbitmq',
    'rabbitmqctl',
    'set_permissions',
    '-p',
    prefix,
    prefix,
    '.*',
    '.*',
    '.*',
  ]);
  for (const service of ['gateway', ...services]) {
    const title = service[0].toUpperCase() + service.slice(1);
    const cwd =
      service === 'gateway' ? root : path.join(root, `apps/${service}/src/VietRide.${title}.Api`);
    const executable = service === 'gateway' ? process.execPath : 'dotnet';
    // Use Nx's normal workspace-package resolver, without its watch/build process tree.
    const args =
      service === 'gateway'
        ? [
            '--max-old-space-size=256',
            'node_modules/@nx/js/src/executors/node/node-with-require-overrides.js',
          ]
        : [path.join(cwd, `bin/Release/net8.0/VietRide.${title}.Api.dll`)];
    const log = path.join(reportDirectory, `${service}.log`);
    const fd = fs.openSync(log, 'w');
    const child = spawn(executable, args, {
      cwd,
      env: environment(service),
      windowsHide: true,
      stdio: ['ignore', fd, fd],
    });
    fs.closeSync(fd);
    children.push({ name: service, process: child, log });
    await poll(
      async () =>
        (await fetch(urls[service] + '/health', { signal: AbortSignal.timeout(2000) })).ok,
      `${service} startup`,
      120000,
    );
    pass(`${service} live health`);
  }
  for (const service of services) {
    const response = await fetch(`${urls.gateway}/v1/${service}/health`);
    assert.equal(response.status, 200);
    pass(`Gateway -> ${service} health`);
  }
  const wrongHeader = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT' })).toString(
    'base64url',
  );
  const wrongPayload = Buffer.from(
    JSON.stringify({
      iss: 'vietride-gateway',
      aud: 'vietride-internal',
      sub: ids.admin,
      role: 'OPERATOR_ADMIN',
      operatorId: ids.operator,
      exp: Math.floor(Date.now() / 1000) + 120,
    }),
  ).toString('base64url');
  const wrongSignature = createHmac('sha256', secret + '-wrong')
    .update(`${wrongHeader}.${wrongPayload}`)
    .digest('base64url');
  const denied = await fetch(urls.parcel + '/v1/operator/claims', {
    headers: { 'X-Internal-Auth': `Bearer ${wrongHeader}.${wrongPayload}.${wrongSignature}` },
    signal: AbortSignal.timeout(5000),
  });
  assert.equal(denied.status, 401);
  pass('Parcel rejects a correctly shaped Internal JWT with a forged signature');
  assert.ok((await fetch('http://127.0.0.1:15672', { signal: AbortSignal.timeout(5000) })).ok);
  const exchanges = run('docker', [
    'exec',
    'vietride_rabbitmq',
    'rabbitmqctl',
    'list_exchanges',
    '-p',
    prefix,
    'name',
    'type',
    '-q',
  ]);
  assert.match(exchanges, /vietride\.events\s+topic/);
  pass('RabbitMQ management reachable; isolated vietride.events topic exchange exists');
}
function seed() {
  sql(
    'identity',
    `INSERT INTO operators(id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
    VALUES ('${ids.operator}','Focused ${tag}','${tag}-a','${tag}-a','op@example.invalid','+84910000001','APPROVED',now(),true),
    ('${ids.foreignOperator}','Foreign ${tag}','${tag}-b','${tag}-b','other@example.invalid','+84910000002','APPROVED',now(),true);
    INSERT INTO users(id,email,phone,display_name,role,status,operator_id) VALUES
    ${[
      ['admin', 'OPERATOR_ADMIN'],
      ['staff', 'OPERATOR_STAFF'],
      ['driver', 'DRIVER'],
      ['assistant', 'ASSISTANT'],
      ['passenger', 'PASSENGER'],
      ['foreignAdmin', 'OPERATOR_ADMIN'],
    ]
      .map(
        ([name, role], i) =>
          `('${ids[name]}','${name}@example.invalid','+8492000000${i}','E2E ${name}','${role}','ACTIVE',${name === 'passenger' ? 'NULL' : `'${name === 'foreignAdmin' ? ids.foreignOperator : ids.operator}'`})`,
      )
      .join(',')};`,
  );
  ids.systemAdmin = rows('identity', "SELECT id FROM users WHERE role='SYSTEM_ADMIN'")[0].id;
  tokens.systemAdmin = token(ids.systemAdmin, 'SYSTEM_ADMIN');
  const layout = '{"version":1,"totalSeats":20,"rows":5,"cols":4,"decks":1,"aisles":[],"seats":[]}';
  sql(
    'trip',
    `INSERT INTO stations(id,name,slug,city,is_active) VALUES
    ('${ids.origin}','E2E Origin','origin-${tag}','Hồ Chí Minh',true),('${ids.destination}','E2E Destination','dest-${tag}','Đà Nẵng',true);
    INSERT INTO vehicle_types(id,code,display_name,default_seat_count,is_system_defined,is_active)
    VALUES ('${ids.vehicleType}','E2E_${tag}','E2E vehicle',20,false,true);
    INSERT INTO vehicles(id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
    VALUES ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','E2E${tag}','${layout}',20,100,10,'ACTIVE',true);
    INSERT INTO routes(id,operator_id,name,origin_station_id,destination_station_id,base_fare,estimated_duration_minutes,is_active)
    VALUES ('${ids.route}','${ids.operator}','E2E Route','${ids.origin}','${ids.destination}',150000,60,true);
    INSERT INTO trips(id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,departure_date_time,estimated_arrival_time,status,source,
      base_fare,max_cargo_weight_kg,max_cargo_volume_m3,estimated_passenger_luggage_kg,reserved_parcel_weight_kg,reserved_parcel_volume_m3,
      total_loaded_weight_kg,total_loaded_volume_m3,seat_layout_snapshot_json)
    VALUES ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}','${ids.assistant}',now()-interval '2 hour',
      now()-interval '1 hour','COMPLETED','MANUAL',150000,100,10,0,0,0,0,0,'${layout}');`,
  );
  sql(
    'payment',
    `UPDATE platform_wallets SET balance=5000000;
    INSERT INTO operator_ledger_entries(operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note)
    VALUES ('${ids.operator}','${ids.trip}','PARCEL_REVENUE',5000000,'PARCEL','${randomUUID()}','${randomUUID()}',
      'Isolated E2E simulated collected revenue backing operator holding; not a real payment');`,
  );
  pass('Minimal users, assigned driver/assistant and ONE simulated completed trip seeded');
}
async function fixture(label, declaration, proofStatus, loss = null, refunded = 0) {
  const parcelId = randomUUID();
  const incidentId = randomUUID();
  sql(
    'parcel',
    `INSERT INTO parcels(id,parcel_code,sender_user_id,recipient_name,recipient_phone,operator_id,trip_id,description,
      size_category,estimated_size_category,estimated_weight_kg,deposit_amount,status,declared_value_vnd,declaration_accepted_at,
      final_total_price_vnd,deposit_paid_vnd,refunded_amount_vnd,no_proof_fallback_multiplier_snapshot)
    VALUES ('${parcelId}','PCL-${tag}-${label}','${ids.passenger}','E2E recipient','+84910000009','${ids.operator}','${ids.trip}',
      'Simulated accepted lost parcel ${label}','SMALL','SMALL',1,150000,'PENDING_OPERATOR_ACTION',${declaration ?? 'NULL'},now(),150000,150000,${refunded},4);
    INSERT INTO parcel_incidents(id,parcel_id,operator_id,trip_id,type,status,reporter_id,reporter_source,description,resolved_at,operator_process_breach)
    VALUES ('${incidentId}','${parcelId}','${ids.operator}','${ids.trip}','MISSING','LOST_CONFIRMED','${ids.assistant}','ASSISTANT',
      'Simulated completed investigation to isolate compensation policy',now(),true);`,
  );
  const claim = await request('POST', `/v1/parcels/${parcelId}/claims`, {
    actor: 'passenger',
    key: randomUUID(),
    expected: 201,
  });
  // Upload is deliberately independent of acceptance: even NO_PROOF has an unaccepted document.
  const uploaded = await request(
    'POST',
    `/v1/parcels/${parcelId}/claims/${claim.claimId}/evidence`,
    {
      actor: 'passenger',
      key: randomUUID(),
      expected: 201,
      body: {
        evidenceType: 'INVOICE',
        reference: `https://example.invalid/${tag}/${label}.pdf`,
        note: 'Synthetic evidence; no external file fetched.',
      },
    },
  );
  const evidenceId = uploaded.evidence[0].evidenceId;
  const body = {
    proofStatus,
    provenDirectLossVnd: loss,
    acceptedEvidenceIds: proofStatus === 'VERIFIED' ? [evidenceId] : [],
  };
  const result = {
    label,
    parcelId,
    incidentId,
    claimId: claim.claimId,
    evidenceId,
    declaration,
    refunded,
    body,
  };
  report.resources[label] = { ...result, body: undefined };
  return result;
}
const claimPath = (c) => `/v1/operator/claims/${c.claimId}`;
const count = (service, query) => Number(sql(service, query));
function financialSnapshot(referenceId) {
  return {
    payout: rows(
      'payment',
      `SELECT status,amount_vnd,funding_source,paid_event_id FROM parcel_compensation_payouts WHERE claim_id='${referenceId}'`,
    ),
    passenger: rows(
      'payment',
      `SELECT type,amount FROM wallet_transactions WHERE reference_type='PARCEL_COMPENSATION' AND reference_id='${referenceId}'`,
    ),
    platform: rows(
      'payment',
      `SELECT type,amount FROM platform_wallet_transactions WHERE reference_type='PARCEL_COMPENSATION' AND reference_id='${referenceId}'`,
    ),
    operator: rows(
      'payment',
      `SELECT type,amount FROM operator_wallet_transactions WHERE reference_type='PARCEL_COMPENSATION' AND reference_id='${referenceId}'`,
    ),
  };
}
async function approve(c, expectedCargo, expectedFreight) {
  const before = count('parcel', 'SELECT count(*) FROM outbox_events;');
  const preview = await request('POST', claimPath(c) + '/award-preview', { body: c.body });
  assert.equal(
    count('parcel', 'SELECT count(*) FROM outbox_events;'),
    before,
    'preview has no Outbox write',
  );
  assert.equal(preview.cargoAwardVnd, expectedCargo);
  assert.equal(preview.freightRefundVnd, expectedFreight);
  assert.equal(preview.totalAwardVnd, expectedCargo + expectedFreight);
  assert.equal(preview.fallbackAmountVnd, null);
  assert.equal(
    preview.calculationBasis,
    c.body.proofStatus === 'VERIFIED' ? 'VERIFIED_LOSS' : 'NO_VERIFIED_PROOF_FREIGHT_ONLY',
  );
  const key = randomUUID();
  const body = {
    ...c.body,
    decision: 'APPROVE',
    reason: 'Focused E2E assessed proof, not self-declaration.',
  };
  const decision = await request('POST', claimPath(c) + '/decision', { body, key });
  assert.equal(decision.claim.totalAwardVnd, preview.totalAwardVnd);
  assert.equal(decision.claim.cargoAwardVnd, expectedCargo);
  assert.equal(decision.claim.proofStatus, c.body.proofStatus);
  assert.deepEqual(decision.claim.acceptedEvidenceIds, c.body.acceptedEvidenceIds);
  await poll(async () => {
    const list = await request('GET', `/v1/parcels/${c.parcelId}/claims`, { actor: 'passenger' });
    return list.find((item) => item.claimId === c.claimId)?.status === 'PAID';
  }, `${c.label} paid roundtrip`);
  const snapshot = financialSnapshot(c.claimId);
  assert.equal(snapshot.payout.length, 1);
  assert.equal(snapshot.payout[0].status, 'PAID');
  assert.ok(snapshot.payout[0].paid_event_id);
  assert.equal(snapshot.payout[0].amount_vnd, preview.totalAwardVnd);
  assert.deepEqual(snapshot.passenger, [{ type: 'CREDIT', amount: preview.totalAwardVnd }]);
  assert.equal(snapshot.platform.length + snapshot.operator.length, 1);
  const ledger = rows(
    'payment',
    `SELECT amount FROM operator_ledger_entries WHERE entry_type='PARCEL_COMPENSATION' AND reference_id='${c.parcelId}'`,
  );
  assert.deepEqual(ledger, [{ amount: -preview.totalAwardVnd }]);
  await request('POST', claimPath(c) + '/decision', { body, key });
  await request('POST', claimPath(c) + '/decision', {
    body,
    key: randomUUID(),
    expected: 409,
    error: 'PARCEL_CLAIM_ALREADY_DECIDED',
  });
  await sleep(1500);
  assert.deepEqual(
    financialSnapshot(c.claimId),
    snapshot,
    'replay cannot add a credit/debit/payout',
  );
  const audit = rows(
    'parcel',
    `SELECT evidence_id,accepted_by_user_id FROM parcel_claim_decision_evidence WHERE claim_id='${c.claimId}'`,
  );
  assert.deepEqual(
    audit,
    c.body.proofStatus === 'VERIFIED'
      ? [{ evidence_id: c.evidenceId, accepted_by_user_id: ids.admin }]
      : [],
  );
  pass(`${c.label}: preview = mutation = PAID = wallet/ledger; replay safe`, { preview, snapshot });
}
async function cases() {
  const a = await fixture('undeclared', null, 'NO_PROOF');
  const b = await fixture('declared', 200000, 'NO_PROOF');
  const c = await fixture('inflated', 10000000, 'UNVERIFIED');
  c.body.cargoAwardVnd = 99999999; // An untrusted client-supplied award must never influence either endpoint.
  const d = await fixture('verified', 10000000, 'VERIFIED', 200000);
  const e = await fixture('verified-null', null, 'VERIFIED', 200000);
  const f = await fixture('fully-refunded', 10000000, 'NO_PROOF', null, 150000);
  const g = await fixture('part-refunded', 10000000, 'UNVERIFIED', null, 50000);
  for (const actor of ['passenger', 'driver', 'assistant', 'staff']) {
    await request('POST', claimPath(a) + '/award-preview', { actor, body: a.body, expected: 403 });
  }
  await request('POST', claimPath(a) + '/award-preview', {
    actor: 'foreignAdmin',
    body: a.body,
    expected: 404,
    error: 'PARCEL_CLAIM_NOT_FOUND',
  });
  for (const body of [
    { proofStatus: 'VERIFIED', provenDirectLossVnd: null, acceptedEvidenceIds: [a.evidenceId] },
    { proofStatus: 'VERIFIED', provenDirectLossVnd: 200000, acceptedEvidenceIds: [] },
    {
      proofStatus: 'VERIFIED',
      provenDirectLossVnd: 200000,
      acceptedEvidenceIds: [a.evidenceId, a.evidenceId],
    },
    { proofStatus: 'UNVERIFIED', provenDirectLossVnd: 200000, acceptedEvidenceIds: [] },
    { proofStatus: 'NO_PROOF', provenDirectLossVnd: null, acceptedEvidenceIds: [a.evidenceId] },
  ])
    for (const endpoint of ['award-preview', 'decision']) {
      await request('POST', `${claimPath(a)}/${endpoint}`, {
        body:
          endpoint === 'decision'
            ? { ...body, decision: 'APPROVE', reason: 'Invalid proof test' }
            : body,
        key: endpoint === 'decision' ? randomUUID() : undefined,
        expected: 422,
        error: 'PARCEL_CLAIM_EVIDENCE_REQUIRED',
      });
    }
  for (const evidenceId of [b.evidenceId, randomUUID()]) {
    await request('POST', claimPath(a) + '/award-preview', {
      body: {
        proofStatus: 'VERIFIED',
        provenDirectLossVnd: 200000,
        acceptedEvidenceIds: [evidenceId],
      },
      expected: 404,
      error: 'PARCEL_CLAIM_EVIDENCE_NOT_FOUND',
    });
  }
  assert.equal(count('parcel', "SELECT count(*) FROM parcel_claims WHERE status<>'SUBMITTED';"), 0);
  pass(
    'Gateway role/tenant fences, proof matrix, wrong/duplicate evidence; failed decisions remain SUBMITTED',
  );
  await approve(a, 0, 150000);
  await approve(b, 0, 150000);
  await approve(c, 0, 150000);
  await approve(d, 100000, 150000);
  await approve(e, 100000, 150000);
  await approve(g, 0, 100000);
  const zero = await request('POST', claimPath(f) + '/award-preview', { body: f.body });
  assert.equal(zero.totalAwardVnd, 0);
  await request('POST', claimPath(f) + '/decision', {
    body: { ...f.body, decision: 'APPROVE', reason: 'Cannot pay zero' },
    key: randomUUID(),
    expected: 422,
    error: 'VALIDATION_ERROR',
  });
  assert.equal(
    rows('parcel', `SELECT status FROM parcel_claims WHERE id='${f.claimId}'`)[0].status,
    'SUBMITTED',
  );
  assert.equal(financialSnapshot(f.claimId).payout.length, 0);
  pass(
    'Fully refunded + no verified proof: preview 200/zero; approval 422, no payout, transaction rolled back',
  );
  const appealed = await request('POST', `/v1/parcels/${b.parcelId}/claims/${b.claimId}/appeal`, {
    actor: 'passenger',
    key: randomUUID(),
    body: { reason: 'Ask reviewer to verify the previously uploaded value evidence.' },
  });
  const appealId = appealed.appeal.appealId;
  report.resources.appealId = appealId;
  const appealPath = `/v1/operator/claim-appeals/${appealId}`;
  for (const endpoint of ['adjustment-preview', 'decision']) {
    await request('POST', `${appealPath}/${endpoint}`, {
      body: {
        proofStatus: 'NO_PROOF',
        revisedProvenDirectLossVnd: null,
        acceptedEvidenceIds: [],
        ...(endpoint === 'decision'
          ? { decision: 'APPROVE_ADJUSTMENT', reason: 'Cannot refund freight twice' }
          : {}),
      },
      key: endpoint === 'decision' ? randomUUID() : undefined,
      expected: 422,
      error: 'PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED',
    });
  }
  assert.equal(
    rows('parcel', `SELECT status FROM parcel_claim_appeals WHERE id='${appealId}'`)[0].status,
    'SUBMITTED',
  );
  // Simulate the same trip having settled. The appeal must now debit only OperatorWallet.
  sql(
    'payment',
    `INSERT INTO operator_wallets(operator_id,balance,currency,row_version)
    VALUES ('${ids.operator}',2000000,'VND',0) ON CONFLICT (operator_id) DO UPDATE SET balance=2000000;
    INSERT INTO operator_trip_settlements(operator_id,trip_id,trip_terminal_at,eligible_at,status,settlement_method,settled_at)
    VALUES ('${ids.operator}','${ids.trip}',now()-interval '8 days',now()-interval '1 day','SETTLED','ADMIN_MANUAL',now());`,
  );
  const proof = {
    proofStatus: 'VERIFIED',
    revisedProvenDirectLossVnd: 200000,
    acceptedEvidenceIds: [b.evidenceId],
  };
  const preview = await request('POST', appealPath + '/adjustment-preview', { body: proof });
  assert.equal(preview.originalTotalAwardVnd, 150000);
  assert.equal(preview.totalAwardVnd, 250000);
  assert.equal(preview.supplementaryAwardVnd, 100000);
  const key = randomUUID();
  const body = {
    ...proof,
    decision: 'APPROVE_ADJUSTMENT',
    reason: 'Verified direct loss; only the positive difference is payable.',
  };
  const decision = await request('POST', appealPath + '/decision', { key, body });
  assert.equal(decision.supplementaryAwardVnd, 100000);
  await poll(async () => (await request('GET', appealPath)).status === 'PAID', 'appeal paid');
  const snapshot = financialSnapshot(appealId);
  assert.deepEqual(snapshot.passenger, [{ type: 'CREDIT', amount: 100000 }]);
  assert.deepEqual(snapshot.operator, [{ type: 'DEBIT', amount: 100000 }]);
  assert.deepEqual(snapshot.platform, []);
  await request('POST', appealPath + '/decision', { key, body });
  await sleep(1500);
  assert.deepEqual(financialSnapshot(appealId), snapshot);
  assert.equal(
    rows('parcel', `SELECT total_award_vnd FROM parcel_claims WHERE id='${b.claimId}'`)[0]
      .total_award_vnd,
    150000,
  );
  const mobile = await request('GET', `/v1/parcels/${b.parcelId}/claims`, { actor: 'passenger' });
  assert.equal(mobile[0].status, 'PAID');
  assert.equal(mobile[0].appeal.status, 'PAID');
  pass(
    'Appeal: no-proof cannot refund twice; VERIFIED delta 100000 paid exactly once; original award unchanged',
    { preview, snapshot },
  );
  const wallet = await request('GET', '/v1/wallet', { actor: 'passenger' });
  const transactions = await request('GET', '/v1/wallet/transactions?page=1&pageSize=50', {
    actor: 'passenger',
  });
  assert.ok(JSON.stringify(transactions).includes('PARCEL_COMPENSATION'));
  const total = count('payment', `SELECT balance FROM wallets WHERE user_id='${ids.passenger}';`);
  assert.equal(total, 1150000);
  pass(
    'Passenger wallet API exposes compensation transactions; balance equals all awards + appeal delta',
    { wallet, total },
  );
  const platformRows = await request(
    'GET',
    '/v1/admin/platform-wallet/transactions?referenceType=PARCEL_COMPENSATION&page=1&pageSize=50',
    { actor: 'systemAdmin' },
  );
  const operatorRows = await request(
    'GET',
    '/v1/operator/wallet/transactions?referenceType=PARCEL_COMPENSATION&page=1&pageSize=50',
  );
  const ledgerRows = await request(
    'GET',
    '/v1/operator/ledger?entryType=PARCEL_COMPENSATION&page=1&pageSize=50',
  );
  assert.equal(platformRows.items.length, 6);
  assert.equal(operatorRows.items.length, 1);
  assert.equal(ledgerRows.items.length, 7);
  pass(
    'FE read APIs: Admin sees six holding debits, Operator sees one post-settlement debit and all seven ledger entries',
  );
}
async function cleanup() {
  for (const child of children.reverse()) {
    if (child.process.exitCode === null) {
      const exited = new Promise((resolve) => child.process.once('exit', resolve));
      child.process.kill();
      await Promise.race([exited, sleep(5000)]);
    }
    report.cleanup.push(`Stopped ${child.name} PID ${child.process.pid}`);
  }
  for (const database of createdDatabases) {
    assert.match(database, /^pcl_e2e_[a-f0-9]{10}_(identity|trip|payment|parcel)$/);
    sql('postgres', `DROP DATABASE ${database} WITH (FORCE);`);
    report.cleanup.push(`Dropped isolated ${database}`);
  }
  if (roleCreated) {
    sql('postgres', `DROP ROLE ${prefix};`);
    report.cleanup.push('Dropped test DB role');
  }
  if (vhostCreated) {
    run('docker', ['exec', 'vietride_rabbitmq', 'rabbitmqctl', 'delete_vhost', prefix]);
    report.cleanup.push('Deleted isolated test vhost/queues');
  }
  if (rabbitUserCreated) {
    run('docker', ['exec', 'vietride_rabbitmq', 'rabbitmqctl', 'delete_user', prefix]);
    report.cleanup.push('Deleted test RabbitMQ user');
  }
}
try {
  await setup();
  seed();
  await cases();
  report.result = 'PASS';
} catch (error) {
  report.result = 'FAIL';
  report.error = error.message.replaceAll(password, '[redacted]');
  console.error(report.error);
  process.exitCode = 1;
} finally {
  try {
    await cleanup();
  } catch (error) {
    report.cleanupError = error.message;
    report.result = 'FAIL';
    process.exitCode = 1;
  }
  report.finishedAt = new Date().toISOString();
  fs.writeFileSync(
    path.join(reportDirectory, 'report.json'),
    JSON.stringify(report, null, 2) + '\n',
  );
  fs.writeFileSync(
    path.join(reportDirectory, 'report.md'),
    `# Focused Parcel Compensation E2E\n\nResult: ${report.result}\n\n` +
      `Scope: ${report.scope}\n\nExcluded: ${report.excluded}\n\n` +
      report.checks.map((check) => `- PASS: ${check.label}`).join('\n') +
      '\n\n' +
      (report.error ? `Failure: ${report.error}\n\n` : '') +
      report.cleanup.map((item) => `- ${item}`).join('\n') +
      '\n',
  );
  console.log(`REPORT | ${path.join(reportDirectory, 'report.md')}`);
}
