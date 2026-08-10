import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import net from 'node:net';

const root = process.cwd();
const startDate = process.argv.find((value) => value.startsWith('--start-date='))?.split('=')[1];
const password = process.env.DEMO_SEED_ACCOUNT_PASSWORD;
if (!password) throw new Error('DEMO_SEED_ACCOUNT_PASSWORD is required');
if (!startDate) throw new Error('--start-date is required');
if ((process.env.NODE_ENV ?? '').toLowerCase() === 'production')
  throw new Error('Day 44 E2E is forbidden in Production');

const invocationId = `${process.pid}-${crypto.randomUUID().slice(0, 8)}`;
const composeProject = `day44-e2e-${invocationId}`;
const containerPrefix = composeProject;
if (!/^day44-e2e-[0-9]+-[0-9a-f]{8}$/.test(composeProject))
  throw new Error('Refusing an invalid Compose project name');
const compose = [
  'compose',
  '--env-file',
  '.env.example',
  '-p',
  composeProject,
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day44-e2e.yml',
  '--profile',
  'app',
];
let runtime = {};
const postgresContainer = `${containerPrefix}-postgres`;
const gatewayContainer = `${containerPrefix}-gateway`;
const ragContainer = `${containerPrefix}-rag`;

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...runtime, ...options.env },
    stdio: options.inherit ? 'inherit' : 'pipe',
  });
  if (result.error) {
    const code = result.error.code ?? result.error.name ?? 'SPAWN_ERROR';
    throw new Error(`Day 44 child command execution failed (${code})`);
  }
  if (result.status !== 0) {
    const stderr = (result.stderr ?? '').slice(0, 4000);
    throw new Error(
      `Day 44 child command failed (exit ${result.status ?? 'unknown'}): ${stderr || 'stderr unavailable or suppressed'}`,
    );
  }
  return result.stdout?.trim() ?? '';
}

function allocatePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (!address || typeof address === 'string')
        return reject(new Error('Could not allocate port'));
      server.close((error) => (error ? reject(error) : resolve(address.port)));
    });
  });
}

async function configure() {
  const names = [
    'POSTGRES_PORT',
    'PGBOUNCER_PORT',
    'REDIS_PORT',
    'RABBITMQ_PORT',
    'RABBITMQ_MGMT_PORT',
    'IDENTITY_PORT',
    'TRIP_PORT',
    'BOOKING_PORT',
    'PAYMENT_PORT',
    'PARCEL_PORT',
    'TRACKING_PORT',
    'NOTIFICATION_PORT',
    'RAG_PORT',
  ];
  const ports = await Promise.all(names.map(allocatePort));
  runtime = Object.fromEntries(names.map((name, index) => [name, String(ports[index])]));
  Object.assign(runtime, {
    DAY44_CONTAINER_PREFIX: containerPrefix,
    DAY44_POSTGRES_CONTAINER: postgresContainer,
    DEMO_SEED_ACCOUNT_PASSWORD: password,
    OPENROUTER_API_KEY: '',
    INTERNAL_JWT_SECRET: 'day44-e2e-internal-jwt-secret-32-bytes-minimum',
    SYSTEM_ADMIN_BOOTSTRAP_PASSWORD: 'day44-e2e-bootstrap-only-password',
    VNPAY_HASH_SECRET: 'day44-e2e-disabled-vnpay-secret',
  });
}

function sql(statement, database = 'vietride_identity') {
  return run('docker', [
    'exec',
    postgresContainer,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-At',
    '-U',
    process.env.POSTGRES_USER ?? 'vietride',
    '-d',
    database,
    '-c',
    statement,
  ]);
}

function waitForProviderTrap(timeoutMs = 240000) {
  const healthUrl = 'http://127.0.0.1:8080/health';
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const probe = spawnSync(
      'docker',
      [
        'exec',
        `${containerPrefix}-provider-trap`,
        'node',
        '-e',
        `fetch('${healthUrl}').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))`,
      ],
      { cwd: root, stdio: 'ignore' },
    );
    if (probe.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  throw new Error(`Timed out waiting for provider trap at ${healthUrl}`);
}

function emitProviderTrapDiagnostics() {
  const outputLimit = 4000;
  for (const [label, command, args] of [
    ['compose ps', 'docker', [...compose, 'ps', 'provider-trap']],
    [
      'provider-trap logs',
      'docker',
      [...compose, 'logs', '--no-color', '--tail', '100', 'provider-trap'],
    ],
    [
      'provider-trap inspect',
      'docker',
      [
        'inspect',
        '--format',
        'status={{.State.Status}} health={{json .State.Health}} ports={{json .NetworkSettings.Ports}}',
        `${containerPrefix}-provider-trap`,
      ],
    ],
  ]) {
    const result = spawnSync(command, args, {
      cwd: root,
      encoding: 'utf8',
      env: { ...process.env, ...runtime },
    });
    console.error(`DAY44_PROVIDER_TRAP_DIAGNOSTIC=${label}`);
    const output = `${result.stdout ?? ''}${result.stderr ?? ''}`.trim();
    console.error(
      output.slice(0, outputLimit) ||
        `diagnostic command exit=${result.status ?? 'unknown'} with no output`,
    );
  }
}

function startAndVerifyProviderTrap() {
  try {
    run('docker', [...compose, 'up', '-d', '--build']);
    waitForProviderTrap();
  } catch (error) {
    emitProviderTrapDiagnostics();
    throw error;
  }
}

function emitRagDiagnostics() {
  const outputLimit = 4000;
  for (const [label, command, args] of [
    ['compose ps', 'docker', [...compose, 'ps', 'rag']],
    ['rag logs', 'docker', [...compose, 'logs', '--no-color', '--tail', '100', 'rag']],
    [
      'rag inspect',
      'docker',
      [
        'inspect',
        '--format',
        'status={{.State.Status}} health={{json .State.Health}} ports={{json .NetworkSettings.Ports}}',
        ragContainer,
      ],
    ],
  ]) {
    const result = spawnSync(command, args, {
      cwd: root,
      encoding: 'utf8',
      env: { ...process.env, ...runtime },
    });
    console.error(`DAY44_RAG_DIAGNOSTIC=${label}`);
    const output = `${result.stdout ?? ''}${result.stderr ?? ''}`.trim();
    console.error(
      output.slice(0, outputLimit) ||
        `diagnostic command exit=${result.status ?? 'unknown'} with no output`,
    );
  }
}

function waitForRag(timeoutMs = 240000) {
  const deadline = Date.now() + timeoutMs;
  const probeScript =
    "fetch('http://127.0.0.1:3003/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))";
  while (Date.now() < deadline) {
    const probe = spawnSync('docker', ['exec', ragContainer, 'node', '-e', probeScript], {
      cwd: root,
      stdio: 'ignore',
    });
    if (probe.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  throw new Error('Timed out waiting for the container-local RAG health endpoint');
}

function verifyRagReady() {
  try {
    waitForRag();
  } catch (error) {
    emitRagDiagnostics();
    throw error;
  }
}

function emitGatewayDiagnostics() {
  const outputLimit = 4000;
  for (const [label, command, args] of [
    ['compose ps', 'docker', [...compose, 'ps', 'gateway']],
    ['gateway logs', 'docker', [...compose, 'logs', '--no-color', '--tail', '100', 'gateway']],
    [
      'gateway inspect',
      'docker',
      [
        'inspect',
        '--format',
        'status={{.State.Status}} health={{json .State.Health}} ports={{json .NetworkSettings.Ports}}',
        gatewayContainer,
      ],
    ],
  ]) {
    const result = spawnSync(command, args, {
      cwd: root,
      encoding: 'utf8',
      env: { ...process.env, ...runtime },
    });
    console.error(`DAY44_GATEWAY_DIAGNOSTIC=${label}`);
    const output = `${result.stdout ?? ''}${result.stderr ?? ''}`.trim();
    console.error(
      output.slice(0, outputLimit) ||
        `diagnostic command exit=${result.status ?? 'unknown'} with no output`,
    );
  }
}

async function withGatewayDiagnostics(action) {
  try {
    return await action();
  } catch (error) {
    emitGatewayDiagnostics();
    throw error;
  }
}

function waitForGateway(timeoutMs = 240000) {
  const deadline = Date.now() + timeoutMs;
  const probeScript =
    "fetch('http://127.0.0.1:3000/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))";
  while (Date.now() < deadline) {
    const probe = spawnSync('docker', ['exec', gatewayContainer, 'node', '-e', probeScript], {
      cwd: root,
      stdio: 'ignore',
    });
    if (probe.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  throw new Error('Timed out waiting for the container-local Gateway health endpoint');
}

const gatewayRequestScript = `
const chunks=[];
process.stdin.on('data',chunk=>chunks.push(chunk));
process.stdin.on('end',async()=>{
  try {
    const request=JSON.parse(Buffer.concat(chunks).toString('utf8'));
    const response=await fetch('http://127.0.0.1:3000'+request.path,request.options);
    const text=await response.text();
    const body=text?JSON.parse(text):null;
    process.stdout.write(JSON.stringify({ok:response.ok,status:response.status,body}));
  } catch {
    process.stderr.write('Gateway container request failed');
    process.exit(1);
  }
});`;

function requestGateway(path, options) {
  const result = spawnSync(
    'docker',
    ['exec', '-i', gatewayContainer, 'node', '-e', gatewayRequestScript],
    {
      cwd: root,
      encoding: 'utf8',
      input: JSON.stringify({ path, options }),
    },
  );
  if (result.status !== 0) throw new Error('Gateway container request failed');
  try {
    return JSON.parse(result.stdout);
  } catch {
    throw new Error('Gateway container returned an invalid response');
  }
}

function assertCount(database, query, expected, label) {
  const actual = Number(sql(query, database));
  if (actual !== expected) throw new Error(`${label}: expected ${expected}, got ${actual}`);
}

async function api(path, options = {}) {
  const response = requestGateway(path, options);
  if (!response.ok) throw new Error(`${path} returned ${response.status}`);
  return response.body?.data ?? response.body;
}

async function smoke() {
  const login = await api('/v1/auth/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'passenger01@demo.vietride.local', password }),
  });
  const token = login.accessToken;
  if (!token) throw new Error('Passenger login did not return accessToken');
  const auth = { authorization: `Bearer ${token}`, 'content-type': 'application/json' };
  const [tripId, originStationId, destinationStationId, seatNumber] = sql(
    "SELECT t.id||'|'||r.origin_station_id||'|'||r.destination_station_id||'|'||ts.seat_number FROM vietride_trip.trips t JOIN vietride_trip.routes r ON r.id=t.route_id JOIN vietride_trip.trip_seats ts ON ts.trip_id=t.id WHERE t.operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7' AND t.status='SCHEDULED' ORDER BY t.departure_date_time,ts.seat_number LIMIT 1",
    'vietride_trip',
  ).split('|');
  const bookingKey = crypto.randomUUID();
  const bookingBody = JSON.stringify({
    tripId,
    pickup: { stationId: originStationId },
    dropoff: { stationId: destinationStationId },
    seats: [{ seatNumber }],
    paymentMethod: 'WALLET',
  });
  const booking = await api('/v1/bookings', {
    method: 'POST',
    headers: { ...auth, 'idempotency-key': bookingKey },
    body: bookingBody,
  });
  const bookingReplay = await api('/v1/bookings', {
    method: 'POST',
    headers: { ...auth, 'idempotency-key': bookingKey },
    body: bookingBody,
  });
  if (booking.bookingId !== bookingReplay.bookingId)
    throw new Error('Booking idempotency replay changed the resource');
  console.log('BOOKING_READY=PASS');
  const parcelKey = crypto.randomUUID();
  const parcelBody = JSON.stringify({
    tripId,
    itemName: 'Day44 demo parcel',
    description: 'isolated smoke',
    sizeCategory: 'SMALL',
    lengthCm: 20,
    widthCm: 20,
    heightCm: 20,
    estimatedWeightKg: 2,
    recipient: {
      fullName: 'Demo Recipient',
      phoneNumber: '+84901234567',
      email: 'recipient@demo.vietride.local',
    },
    deliveryMethod: 'TERMINAL_PICKUP',
    paymentMethod: 'WALLET',
  });
  const parcel = await api('/v1/parcels', {
    method: 'POST',
    headers: { ...auth, 'idempotency-key': parcelKey },
    body: parcelBody,
  });
  const parcelReplay = await api('/v1/parcels', {
    method: 'POST',
    headers: { ...auth, 'idempotency-key': parcelKey },
    body: parcelBody,
  });
  if (parcel.parcelId !== parcelReplay.parcelId)
    throw new Error('Parcel idempotency replay changed the resource');
  assertCount(
    'vietride_booking',
    `SELECT count(*) FROM vietride_booking.bookings WHERE id='${booking.bookingId}' AND operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7'`,
    1,
    'Booking tenant snapshot',
  );
  assertCount(
    'vietride_parcel',
    `SELECT count(*) FROM vietride_parcel.parcels WHERE id='${parcel.parcelId}' AND operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7'`,
    1,
    'Parcel tenant snapshot',
  );
  assertCount(
    'vietride_payment',
    'SELECT count(*) FROM vietride_payment.wallets WHERE balance<0',
    0,
    'non-negative wallets',
  );
  console.log('PARCEL_READY=PASS');
}

function assertAcceptanceMatrix() {
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.users WHERE role='SYSTEM_ADMIN' AND status='ACTIVE' AND deleted_at IS NULL",
    1,
    'System Admin',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.operators WHERE contact_email LIKE 'operator.%@demo.vietride.local'",
    3,
    'Operators',
  );
  for (const [role, expected] of [
    ['OPERATOR_ADMIN', 3],
    ['OPERATOR_STAFF', 0],
    ['DRIVER', 9],
    ['ASSISTANT', 3],
    ['PASSENGER', 10],
  ])
    assertCount(
      'vietride_identity',
      `SELECT count(*) FROM vietride_identity.users WHERE email LIKE '%@demo.vietride.local' AND role='${role}'`,
      expected,
      role,
    );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.users WHERE email LIKE '%@demo.vietride.local' AND password_hash LIKE '$2%$12$%' AND status='ACTIVE'",
    25,
    'login-ready accounts',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.oauth_identities WHERE user_id IN (SELECT id FROM vietride_identity.users WHERE email LIKE '%@demo.vietride.local')",
    0,
    'demo OAuth identities',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.subscription_plans WHERE id IN ('00000000-0000-0000-0000-000000000001','44000000-0000-4000-8000-000000000001')",
    2,
    'plans',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.operator_subscriptions WHERE operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3') AND status='ACTIVE'",
    3,
    'subscriptions',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.subscription_upgrade_attempts WHERE status='SUCCEEDED' AND operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605')",
    2,
    'upgrade attempts',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.integration_inbox WHERE id IN ('6f1a2f10-d9ca-5d89-8d55-7194dae1364d','ce48381f-919e-5222-a900-b645b00578be')",
    2,
    'identity inbox',
  );

  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.payments WHERE reference_type='SUBSCRIPTION' AND status='SUCCEEDED'",
    2,
    'subscription Payments',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.invoices WHERE status='ISSUED' AND pdf_generation_status='COMPLETED'",
    2,
    'Invoices',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.processed_integration_events WHERE id IN ('496209ea-4358-5d81-a91e-33704ed81c77','6fcceb19-f24c-5e0e-8bc3-59351df2da68')",
    2,
    'processed events',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.outbox_events WHERE id IN ('3ddf16ca-8deb-5719-83b7-b3683392b782','a213a3e7-d834-5897-a404-9b2c883afd00') AND status='PUBLISHED'",
    2,
    'published Outbox events',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT' AND type='CREDIT' AND amount=2000000",
    2,
    'platform credits',
  );
  if (
    sql(
      "SELECT COALESCE(sum(amount),0) FROM vietride_payment.platform_wallet_transactions WHERE reference_type='SUBSCRIPTION_PAYMENT'",
      'vietride_payment',
    ) !== '4000000'
  )
    throw new Error('platform credits total mismatch');

  for (const [query, expected, label] of [
    [
      "SELECT count(*) FROM vietride_trip.stations WHERE slug IN ('day44-ben-xe-mien-tay','day44-ben-xe-mien-dong-moi','day44-ben-xe-trung-tam-can-tho','day44-ben-xe-khach-phuong-long-chau','day44-ben-xe-ben-tre')",
      5,
      'Stations',
    ],
    [
      "SELECT count(*) FROM vietride_trip.operator_stations WHERE operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      15,
      'OperatorStation links',
    ],
    [
      "SELECT count(*) FROM vietride_trip.stops WHERE operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      9,
      'Stops',
    ],
    ["SELECT count(*) FROM vietride_trip.routes WHERE name LIKE 'D44 %'", 9, 'Routes'],
    [
      "SELECT count(*) FROM vietride_trip.routes WHERE name LIKE 'D44 %' AND return_route_id IS NOT NULL AND id::text<return_route_id::text",
      3,
      'return pairs',
    ],
    [
      "SELECT count(*) FROM vietride_trip.alternative_routes ar JOIN vietride_trip.routes r ON r.id=ar.route_id WHERE r.name LIKE 'D44 %'",
      3,
      'AlternativeRoutes',
    ],
    [
      "SELECT count(*) FROM vietride_trip.vehicles WHERE license_plate LIKE '51B-44%'",
      9,
      'Vehicles',
    ],
    [
      "SELECT count(*) FROM vietride_trip.driver_schedules WHERE operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      9,
      'schedules',
    ],
  ])
    assertCount('vietride_trip', query, expected, label);
  assertCount(
    'vietride_trip',
    "SELECT count(*) FROM vietride_trip.route_stops rs JOIN vietride_trip.routes r ON r.id=rs.route_id WHERE r.name LIKE 'D44 %'",
    9,
    'RouteStops',
  );
  assertCount(
    'vietride_trip',
    "SELECT count(*) FROM vietride_trip.alternative_route_stops ars JOIN vietride_trip.alternative_routes ar ON ar.id=ars.alternative_route_id JOIN vietride_trip.routes r ON r.id=ar.route_id WHERE r.name LIKE 'D44 %'",
    9,
    'AlternativeRouteStops',
  );
  assertCount(
    'vietride_trip',
    "SELECT count(*) FROM vietride_trip.trip_stops ts JOIN vietride_trip.trips t ON t.id=ts.trip_id WHERE t.operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
    126,
    'TripStops',
  );
  for (const operatorId of [
    '6276b48c-3984-582b-9c35-0c2fbe20baa7',
    'd63b3c32-8c12-5130-a347-0ef8df286605',
    '8554beea-8b1b-57c5-bb87-8d1f136654a3',
  ]) {
    const expectedCounter = Number(
      sql(
        `SELECT count(*) FROM vietride_trip.trips WHERE operator_id='${operatorId}' AND (departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh')::date BETWEEN DATE '${startDate}' AND DATE '${startDate}' + 13 AND to_char(departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh','YYYY-MM')=substring('${startDate}',1,7)`,
        'vietride_trip',
      ),
    );
    assertCount(
      'vietride_identity',
      `SELECT count(*) FROM vietride_identity.operator_subscriptions WHERE operator_id='${operatorId}' AND current_trips_this_month=${expectedCounter}`,
      1,
      `Asia/Ho_Chi_Minh trip counter ${operatorId}`,
    );
  }

  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.wallets WHERE user_id IN (SELECT user_id FROM vietride_payment.top_up_requests WHERE vnpay_txn_ref LIKE 'D44-%') AND balance=2000000",
    10,
    'wallets',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.top_up_requests WHERE vnpay_txn_ref LIKE 'D44-%' AND status='SUCCEEDED'",
    10,
    'top-ups',
  );
  assertCount(
    'vietride_payment',
    "SELECT count(*) FROM vietride_payment.wallet_transactions WHERE reference_type='TOP_UP' AND type='CREDIT' AND amount=2000000 AND balance_before=0 AND balance_after=2000000",
    10,
    'wallet transactions',
  );
  assertCount(
    'vietride_booking',
    "SELECT count(*) FROM vietride_booking.operator_voucher_consents WHERE status='ACCEPTED'",
    2,
    'voucher consents',
  );
  assertCount(
    'vietride_parcel',
    "SELECT count(*) FROM vietride_parcel.parcel_route_fares WHERE size_category='SMALL' AND price_vnd=50000",
    2,
    'Parcel fares',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.operator_subscriptions s JOIN vietride_identity.subscription_plans p ON p.id=s.active_plan_id WHERE s.operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605') AND p.enable_parcel AND p.enable_rag",
    2,
    'Business entitlements',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM vietride_identity.operator_subscriptions s JOIN vietride_identity.subscription_plans p ON p.id=s.active_plan_id WHERE s.operator_id='8554beea-8b1b-57c5-bb87-8d1f136654a3' AND NOT p.enable_parcel AND p.enable_rag",
    1,
    'Starter entitlement isolation',
  );
  assertCount(
    'vietride_parcel',
    "SELECT count(*) FROM vietride_parcel.parcel_route_fares WHERE operator_id='8554beea-8b1b-57c5-bb87-8d1f136654a3'",
    0,
    'Starter Parcel denial',
  );
  assertCount(
    'vietride_rag',
    "SELECT count(*) FROM vietride_rag.knowledge_documents WHERE storage_path LIKE 'day44-v1/%' AND status='APPROVED' AND ingest_status='COMPLETED' AND embedding_model='nvidia/llama-nemotron-embed-vl-1b-v2:free' AND embedding_dimensions=2048",
    3,
    'RAG exact documents',
  );
  assertCount(
    'vietride_rag',
    "SELECT count(*) FROM vietride_rag.knowledge_documents WHERE storage_path='day44-v1/rag/operator-a-policy.txt' AND access_level='OPERATOR' AND operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7' AND NOT ('PASSENGER'=ANY(audience_roles))",
    1,
    'RAG cross-role denial',
  );
  assertCount(
    'vietride_rag',
    "SELECT count(*) FROM vietride_rag.knowledge_documents WHERE access_level='ADMIN' AND audience_roles=ARRAY['SYSTEM_ADMIN']::text[]",
    1,
    'RAG admin denial matrix',
  );
}

async function main() {
  await configure();
  const projectLabel = `label=com.docker.compose.project=${composeProject}`;
  const existingObjects = [
    run('docker', ['ps', '-aq', '--filter', projectLabel]),
    run('docker', ['volume', 'ls', '-q', '--filter', projectLabel]),
    run('docker', ['network', 'ls', '-q', '--filter', projectLabel]),
  ].filter(Boolean);
  if (existingObjects.length) throw new Error('Refusing to reuse a non-empty Compose project');
  try {
    startAndVerifyProviderTrap();
    verifyRagReady();
    await withGatewayDiagnostics(() => waitForGateway());
    let checksum;
    for (let index = 1; index <= 2; index += 1) {
      const before = Date.now();
      const output = run(
        process.execPath,
        [
          '--require',
          'ts-node/register/transpile-only',
          'scripts/seed-dev-data.ts',
          `--start-date=${startDate}`,
        ],
        {
          env: {
            TS_NODE_COMPILER_OPTIONS: JSON.stringify({
              module: 'commonjs',
              moduleResolution: 'node10',
              target: 'ES2022',
              ignoreDeprecations: '6.0',
            }),
          },
        },
      );
      const elapsed = Date.now() - before;
      console.log(output);
      console.log(`DAY44_SEED_RUN_${index}_MS=${elapsed}`);
      if (elapsed >= 120000) throw new Error(`Seed run ${index} exceeded 120000 ms`);
      const next = output.match(/DAY44_SEED_CHECKSUM=([0-9a-f]{64})/)?.[1];
      if (!next || (checksum && next !== checksum))
        throw new Error('Fixture checksum changed across rerun');
      checksum = next;
    }
    console.log('IDEMPOTENT_RERUN=PASS');
    assertAcceptanceMatrix();
    assertCount(
      'vietride_identity',
      "SELECT count(*) FROM vietride_identity.operators WHERE id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      3,
      'operators',
    );
    assertCount(
      'vietride_trip',
      "SELECT count(*) FROM vietride_trip.trips WHERE operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      126,
      'trips',
    );
    assertCount(
      'vietride_trip',
      "SELECT count(*) FROM vietride_trip.trip_seats ts JOIN vietride_trip.trips t ON t.id=ts.trip_id WHERE t.operator_id IN ('6276b48c-3984-582b-9c35-0c2fbe20baa7','d63b3c32-8c12-5130-a347-0ef8df286605','8554beea-8b1b-57c5-bb87-8d1f136654a3')",
      3948,
      'trip seats',
    );
    assertCount(
      'vietride_booking',
      "SELECT count(*) FROM vietride_booking.vouchers WHERE code LIKE 'D44%'",
      5,
      'vouchers',
    );
    assertCount(
      'vietride_rag',
      "SELECT count(*) FROM vietride_rag.knowledge_documents WHERE storage_path LIKE 'day44-v1/%'",
      3,
      'RAG documents',
    );
    assertCount(
      'vietride_rag',
      "SELECT count(*) FROM vietride_rag.knowledge_chunks c JOIN vietride_rag.knowledge_documents d ON d.id=c.document_id WHERE d.storage_path LIKE 'day44-v1/%' AND vector_dims(c.embedding)=2048 AND c.search_vector IS NOT NULL",
      3,
      'RAG searchable chunks',
    );
    const providerRequests = run('docker', [
      'exec',
      `${containerPrefix}-provider-trap`,
      'cat',
      '/tmp/request-count',
    ]);
    if (providerRequests !== '0')
      throw new Error(`RAG provider trap observed ${providerRequests} requests`);
    console.log('RAG_READY=PASS');
    await withGatewayDiagnostics(() => smoke());
    console.log('DAY44_RUN=PASS');
  } finally {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
  }
}

await main();
