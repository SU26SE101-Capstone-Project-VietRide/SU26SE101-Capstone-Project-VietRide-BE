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
  if (!response.ok) {
    const errorCode = response.body?.error?.code ?? 'UNKNOWN_ERROR';
    const errorMessage = response.body?.error?.message ?? 'No error message returned';
    throw new Error(`${path} returned ${response.status} ${errorCode}: ${errorMessage}`);
  }
  return response.body?.data ?? response.body;
}

function seedCurrentFeatureSmokeData() {
  sql(
    `INSERT INTO vietride_trip.incidents
      (id,trip_id,reported_by_user_id,category,description,photo_urls,latitude,longitude,reported_at,created_at,updated_at)
    SELECT
      'f44e0000-0000-4000-8000-000000000101',t.id,t.driver_user_id,'TRAFFIC_JAM',
      'Day 44 current-feature incident smoke','["https://example.com/day44-incident.jpg"]'::jsonb,
      10.7410370,106.6189800,now(),now(),now()
    FROM vietride_trip.trips t
    JOIN vietride_trip.routes r ON r.id=t.route_id
    WHERE r.operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7'
      AND r.name LIKE 'D44 A R3 %'
      AND t.status='SCHEDULED'
    ORDER BY t.departure_date_time,t.id
    LIMIT 1;`,
    'vietride_trip',
  );

  const [tripId, pickupStopId, dropoffStopId] = sql(
    `SELECT t.id||'|'||pickup.stop_id||'|'||dropoff.stop_id
     FROM vietride_trip.trips t
     JOIN vietride_trip.routes r ON r.id=t.route_id
     JOIN vietride_trip.trip_stops pickup ON pickup.trip_id=t.id AND pickup.order_index=2
     JOIN vietride_trip.trip_stops dropoff ON dropoff.trip_id=t.id AND dropoff.order_index=3
     WHERE r.operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7'
       AND r.name LIKE 'D44 A R3 %'
       AND t.status='SCHEDULED'
     ORDER BY t.departure_date_time,t.id
     LIMIT 1`,
    'vietride_trip',
  ).split('|');
  if (!tripId || !pickupStopId || !dropoffStopId)
    throw new Error('Current-feature smoke fixture did not resolve its Trip/Stops');
  return { tripId, pickupStopId, dropoffStopId };
}

function assertApiFailure(response, expectedStatus, expectedCode, label) {
  if (response.status !== expectedStatus || response.body?.error?.code !== expectedCode)
    throw new Error(
      `${label}: expected ${expectedStatus} ${expectedCode}, got ${response.status} ${response.body?.error?.code ?? 'NO_CODE'}`,
    );
}

function addDateDays(value, offsetDays) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}

async function currentFeatureSmoke() {
  const registrationEmail = `operator.feature.${invocationId}@demo.vietride.local`;
  const registration = await api('/v1/operators/register', {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'idempotency-key': crypto.randomUUID() },
    body: JSON.stringify({
      name: 'Day 44 Districtless Operator',
      contactEmail: registrationEmail,
      contactPhone: '+84901112223',
      businessRegistrationNumber: `D44-E2E-${invocationId}`,
      taxCode: `D44TAX-${invocationId}`,
      addressStreet: '1 Nguyễn Huệ',
      addressWard: 'Phường Vũng Tàu',
      addressProvince: 'Thành phố Hồ Chí Minh',
      representativeName: 'Day 44 Representative',
      representativePhone: '+84901112224',
      password,
    }),
  });
  if (!registration.operatorId) throw new Error('Districtless registration returned no operatorId');
  assertCount(
    'vietride_identity',
    `SELECT count(*) FROM vietride_identity.operators WHERE id='${registration.operatorId}' AND contact_email='${registrationEmail}' AND address_street='1 Nguyễn Huệ' AND address_ward='Phường Vũng Tàu' AND address_province='Thành phố Hồ Chí Minh'`,
    1,
    'districtless Operator persistence',
  );
  assertCount(
    'vietride_identity',
    "SELECT count(*) FROM information_schema.columns WHERE table_schema='vietride_identity' AND table_name='operators' AND column_name='address_district'",
    0,
    'removed Operator district column',
  );
  console.log('OPERATOR_DISTRICT_REMOVAL_E2E=PASS');

  const roots = await api('/v1/locations');
  const hcm = roots.find((location) => location.code === '79');
  if (
    roots.length !== 34 ||
    !hcm ||
    hcm.type !== 'MUNICIPALITY' ||
    hcm.parentId !== null ||
    hcm.parentCode !== null
  )
    throw new Error('Location root catalog did not expose the 34 official top-level units');
  const hcmSearch = await api(`/v1/locations?${new URLSearchParams({ search: 'Ho Chi Minh' })}`);
  if (!hcmSearch.some((location) => location.code === '79'))
    throw new Error('Accent-insensitive root Location search did not find Hồ Chí Minh');
  const hcmChildrenQuery = new URLSearchParams({ parentCode: '79', search: 'Vung Tau' });
  const hcmChildren = await api(`/v1/locations?${hcmChildrenQuery}`);
  const vungTau = hcmChildren.find((location) => location.code === '26506');
  if (
    !vungTau ||
    vungTau.name !== 'Phường Vũng Tàu' ||
    vungTau.type !== 'WARD' ||
    vungTau.parentCode !== '79' ||
    vungTau.parentName !== 'Thành phố Hồ Chí Minh'
  )
    throw new Error('Location cascade did not expose Phường Vũng Tàu under Hồ Chí Minh');
  console.log('LOCATION_HIERARCHY_CATALOG_E2E=PASS');

  const fixture = seedCurrentFeatureSmokeData();
  const search = async (
    originProvince,
    originWard,
    destinationProvince,
    destinationWard,
    allowAlongRoutePickup,
  ) => {
    const query = new URLSearchParams({
      originProvinceCode: originProvince,
      destinationProvinceCode: destinationProvince,
      departureDate: startDate,
      passengerCount: '1',
      allowAlongRoutePickup: String(allowAlongRoutePickup),
    });
    if (originWard) query.set('originWardCode', originWard);
    if (destinationWard) query.set('destinationWardCode', destinationWard);
    return api(`/v1/trips/search?${query}`);
  };
  const terminalResult = await search('79', '27460', '86', '28789', false);
  const terminalTrip = terminalResult.items?.find((item) => item.tripId === fixture.tripId);
  if (
    !terminalTrip ||
    !terminalTrip.pickupPoints?.some((point) => point.type === 'STATION') ||
    !terminalTrip.dropoffPoints?.some((point) => point.type === 'STATION')
  )
    throw new Error('Terminal-to-terminal location search did not expose Station points');

  const stopResult = await search('92', '31186', '86', '29551', false);
  const stopTrip = stopResult.items?.find((item) => item.tripId === fixture.tripId);
  if (
    !stopTrip ||
    !stopTrip.pickupPoints?.some(
      (point) =>
        point.type === 'STOP' && point.stopId === fixture.pickupStopId && point.allowPickup,
    ) ||
    !stopTrip.dropoffPoints?.some(
      (point) =>
        point.type === 'STOP' && point.stopId === fixture.dropoffStopId && point.allowDropoff,
    )
  )
    throw new Error('Stop-to-stop location search did not expose eligible Stop points');
  const reverseResult = await search('86', '29551', '92', '31186', true);
  if (reverseResult.items?.some((item) => item.tripId === fixture.tripId))
    throw new Error('Location search accepted a dropoff-before-pickup journey');
  console.log('TRIP_LOCATION_STOP_SEARCH_E2E=PASS');

  const login = async (email) =>
    api('/v1/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
  const operatorA = await login('operator.a@demo.vietride.local');
  const operatorB = await login('operator.b@demo.vietride.local');
  const passenger = await login('passenger01@demo.vietride.local');
  const operatorAuth = {
    authorization: `Bearer ${operatorA.accessToken}`,
    'content-type': 'application/json',
  };
  const createdStation = await api('/v1/operator/stations', {
    method: 'POST',
    headers: {
      ...operatorAuth,
      'idempotency-key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      name: 'Bến xe Vũng Tàu E2E',
      latitude: 10.401234,
      longitude: 107.121234,
      addressStreet: '1 Đường Thùy Vân',
      supportsShuttle: false,
      locationCode: '26506',
    }),
  });
  if (!createdStation.stationId)
    throw new Error('Station creation with an official ward code returned no Station ID');
  const stationDetail = await api(`/v1/stations/${createdStation.stationId}`);
  if (
    stationDetail.locationId !== vungTau.id ||
    stationDetail.city !== 'Thành phố Hồ Chí Minh' ||
    stationDetail.ward !== 'Phường Vũng Tàu'
  )
    throw new Error('Station did not derive City/Ward snapshots from the official leaf Location');
  const createdStop = await api('/v1/operator/stops', {
    method: 'POST',
    headers: {
      ...operatorAuth,
      'idempotency-key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      name: 'Bến xe Vũng Tàu dọc tuyến E2E',
      latitude: 10.411234,
      longitude: 107.131234,
      address: 'Phường Vũng Tàu, Thành phố Hồ Chí Minh',
      locationCode: '26506',
    }),
  });
  if (
    !createdStop.id ||
    createdStop.locationId !== vungTau.id ||
    createdStop.city !== 'Thành phố Hồ Chí Minh' ||
    createdStop.ward !== 'Phường Vũng Tàu'
  )
    throw new Error('Stop creation did not return its complete administrative hierarchy');
  const invalidTopLevelStation = requestGateway('/v1/operator/stations', {
    method: 'POST',
    headers: {
      ...operatorAuth,
      'idempotency-key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      name: 'Invalid top-level Station',
      latitude: 10.421234,
      longitude: 107.141234,
      supportsShuttle: false,
      locationCode: '79',
    }),
  });
  assertApiFailure(
    invalidTopLevelStation,
    422,
    'VALIDATION_ERROR',
    'top-level Station Location rejection',
  );

  const publicStations = await api(
    `/v1/stations/search?${new URLSearchParams({ q: 'ben xe vung tau', locationId: vungTau.id })}`,
  );
  if (!publicStations.some((station) => station.id === createdStation.stationId))
    throw new Error('Public Station search did not match an unaccented query');
  const operatorStations = await api('/v1/operator/stations?search=ben%20xe%20vung%20tau', {
    headers: operatorAuth,
  });
  if (!operatorStations.items?.some((mapping) => mapping.station?.id === createdStation.stationId))
    throw new Error('Operator Station search did not match an unaccented query');
  const operatorStops = await api('/v1/operator/stops?search=ben%20xe%20vung%20tau', {
    headers: operatorAuth,
  });
  if (
    !operatorStops.items?.some(
      (stop) =>
        stop.id === createdStop.id &&
        stop.city === 'Thành phố Hồ Chí Minh' &&
        stop.ward === 'Phường Vũng Tàu',
    )
  )
    throw new Error('Operator Stop search did not match an unaccented query');

  const systemAdmin = await api('/v1/auth/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      email: 'admin@vietride.app',
      password: runtime.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD,
    }),
  });
  const adminAuth = { authorization: `Bearer ${systemAdmin.accessToken}` };
  const adminStations = await api('/v1/admin/stations?search=vung%20tau', {
    headers: adminAuth,
  });
  if (!adminStations.items?.some((station) => station.id === createdStation.stationId))
    throw new Error('Admin Station search did not match an unaccented location snapshot');
  const adminStops = await api('/v1/admin/stops?search=ben%20xe%20vung%20tau', {
    headers: adminAuth,
  });
  if (
    !adminStops.items?.some(
      (stop) =>
        stop.id === createdStop.id &&
        stop.city === 'Thành phố Hồ Chí Minh' &&
        stop.ward === 'Phường Vũng Tàu',
    )
  )
    throw new Error('Admin Stop search did not match an unaccented query');
  console.log('LEAF_LOCATION_RESOURCE_CREATE_E2E=PASS');
  console.log('ACCENT_INSENSITIVE_RESOURCE_SEARCH_E2E=PASS');

  const [routeId, vehicleId, driverUserId] = sql(
    `SELECT ds.route_id||'|'||ds.vehicle_id||'|'||ds.driver_user_id
     FROM vietride_trip.driver_schedules ds
     JOIN vietride_trip.routes r ON r.id=ds.route_id
     WHERE ds.operator_id='6276b48c-3984-582b-9c35-0c2fbe20baa7'
       AND r.name LIKE 'D44 A R3 %'
     LIMIT 1`,
    'vietride_trip',
  ).split('|');
  const localToday = sql("SELECT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date", 'vietride_trip');
  const generatedSchedule = await api('/v1/operator/driver-schedules', {
    method: 'POST',
    headers: {
      ...operatorAuth,
      'idempotency-key': crypto.randomUUID(),
    },
    body: JSON.stringify({
      routeId,
      vehicleId,
      driverUserId,
      assistantUserId: null,
      dayOfWeek: [1, 2, 3, 4, 5, 6, 7],
      departureTime: '23:59:00',
      validFrom: localToday,
      validUntil: addDateDays(localToday, 31),
      baseFare: 120000,
      isActive: true,
    }),
  });
  if (!generatedSchedule.id) throw new Error('DriverSchedule creation returned no id');
  let generatedCount = 0;
  for (let attempt = 0; attempt < 60; attempt += 1) {
    generatedCount = Number(
      sql(
        `SELECT count(*) FROM vietride_trip.trips WHERE driver_schedule_id='${generatedSchedule.id}'`,
        'vietride_trip',
      ),
    );
    if (generatedCount >= 31) break;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  const [count, minimumDate, maximumDate, plus30Count, plus31Count] = sql(
    `SELECT count(*)||'|'||coalesce(min((departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh')::date)::text,'')||'|'||coalesce(max((departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh')::date)::text,'')||'|'||count(*) FILTER (WHERE (departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh')::date=DATE '${localToday}'+30)||'|'||count(*) FILTER (WHERE (departure_date_time AT TIME ZONE 'Asia/Ho_Chi_Minh')::date=DATE '${localToday}'+31)
     FROM vietride_trip.trips
     WHERE driver_schedule_id='${generatedSchedule.id}'`,
    'vietride_trip',
  ).split('|');
  if (
    Number(count) !== 31 ||
    generatedCount !== 31 ||
    minimumDate !== localToday ||
    maximumDate !== addDateDays(localToday, 30) ||
    Number(plus30Count) !== 1 ||
    Number(plus31Count) !== 0
  ) {
    const jobState = sql(
      `SELECT coalesce(string_agg(j.id||':'||j.statename||':'||coalesce(s.reason,'')||':'||left(coalesce(s.data,''),1000), E'\\n'),'NO_JOB')
       FROM hangfire.job j
       LEFT JOIN LATERAL (
         SELECT reason,data FROM hangfire.state WHERE jobid=j.id ORDER BY id DESC LIMIT 1
       ) s ON true
       WHERE j.arguments ILIKE '%${generatedSchedule.id}%'`,
      'vietride_trip',
    );
    const skipState = sql(
      `SELECT coalesce(string_agg(skipped_date||':'||reason||':'||message, E'\\n'),'NO_SKIP_LOG')
       FROM vietride_trip.trip_generation_skip_logs
       WHERE driver_schedule_id='${generatedSchedule.id}'`,
      'vietride_trip',
    );
    throw new Error(
      `30-day Trip generation mismatch: ${count}|${minimumDate}|${maximumDate}|${plus30Count}|${plus31Count}; Hangfire=${jobState}; Skips=${skipState}`,
    );
  }
  console.log('TRIP_30_DAY_GENERATION_E2E=PASS');

  const incidentList = await api(
    '/v1/operator/incidents?category=TRAFFIC_JAM&status=OPEN&page=1&pageSize=20',
    { headers: operatorAuth },
  );
  const incident = incidentList.items?.find(
    (item) => item.incidentId === 'f44e0000-0000-4000-8000-000000000101',
  );
  if (
    !incident ||
    incident.status !== 'OPEN' ||
    incident.trip?.tripId !== fixture.tripId ||
    !incident.reporter?.userId ||
    !incident.reporter?.displayName ||
    incident.reporter?.role !== 'DRIVER'
  )
    throw new Error('Operator Incident list did not return enriched same-tenant data');
  const incidentDetail = await api('/v1/operator/incidents/f44e0000-0000-4000-8000-000000000101', {
    headers: operatorAuth,
  });
  if (incidentDetail.incidentId !== incident.incidentId || !incidentDetail.trip?.route?.routeId)
    throw new Error('Operator Incident detail did not return full Trip/Route context');
  const foreignDetail = requestGateway(
    '/v1/operator/incidents/f44e0000-0000-4000-8000-000000000101',
    { headers: { authorization: `Bearer ${operatorB.accessToken}` } },
  );
  assertApiFailure(foreignDetail, 404, 'INCIDENT_NOT_FOUND', 'cross-tenant Incident detail');
  const passengerList = requestGateway('/v1/operator/incidents', {
    headers: { authorization: `Bearer ${passenger.accessToken}` },
  });
  assertApiFailure(passengerList, 403, 'FORBIDDEN', 'Passenger Incident role gate');
  const invalidFilter = requestGateway('/v1/operator/incidents?category=0', {
    headers: operatorAuth,
  });
  assertApiFailure(invalidFilter, 422, 'VALIDATION_ERROR', 'numeric Incident category');
  console.log('OPERATOR_INCIDENT_READ_E2E=PASS');
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
  const tripSearchQuery = new URLSearchParams({
    originProvinceCode: '79',
    originWardCode: '27460',
    destinationProvinceCode: '92',
    destinationWardCode: '31186',
    departureDate: startDate,
    passengerCount: '1',
  });
  const searchedTrips = await api(`/v1/trips/search?${tripSearchQuery}`);
  const searchedTrip = searchedTrips.items?.find(
    (trip) => trip.operatorId === '6276b48c-3984-582b-9c35-0c2fbe20baa7',
  );
  if (!searchedTrip) throw new Error('Hierarchy Trip search returned no bookable Operator A Trip');
  const tripId = searchedTrip.tripId;
  const originStationId = searchedTrip.originStation.id;
  const destinationStationId = searchedTrip.destinationStation.id;
  const seatNumber = sql(
    `SELECT seat_number FROM vietride_trip.trip_seats WHERE trip_id='${tripId}' AND status='AVAILABLE' ORDER BY seat_number LIMIT 1`,
    'vietride_trip',
  );
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
  assertCount(
    'vietride_booking',
    `SELECT count(*) FROM vietride_booking.bookings WHERE id='${booking.bookingId}' AND trip_id='${tripId}'`,
    1,
    'hierarchy-searched Trip booking',
  );
  console.log('TRIP_SEARCH_TO_BOOKING_E2E=PASS');
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
    await withGatewayDiagnostics(() => currentFeatureSmoke());
    console.log('DAY44_RUN=PASS');
  } finally {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
  }
}

await main();
