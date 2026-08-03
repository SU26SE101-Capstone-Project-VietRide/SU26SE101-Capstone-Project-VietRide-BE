const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const { createHash, randomUUID } = require('node:crypto');
const amqp = require('amqplib');
const { Queue } = require('bullmq');
const Redis = require('ioredis');
const { io } = require('socket.io-client');

const gatewayUrl = (process.env.GATEWAY_BASE_URL || 'http://127.0.0.1:3000').replace(/\/$/, '');
const trackingUrl = (process.env.TRACKING_BASE_URL || 'http://127.0.0.1:3001').replace(/\/$/, '');
const notificationUrl = (process.env.NOTIFICATION_BASE_URL || 'http://127.0.0.1:3002').replace(
  /\/$/,
  '',
);
const redisUrl = process.env.REDIS_URL || 'redis://127.0.0.1:6379';
const rabbitUrl = process.env.RABBITMQ_URL || 'amqp://vietride:vietride_dev@127.0.0.1:5672';
const containers = {
  postgres: process.env.POSTGRES_CONTAINER || 'vietride_postgres',
  gateway: process.env.GATEWAY_CONTAINER || 'vietride_gateway',
  tracking: process.env.TRACKING_CONTAINER || 'vietride_tracking',
};
const runId = randomUUID();
const tag = runId.replaceAll('-', '').slice(0, 12);
const phoneTail = BigInt(`0x${tag}`).toString().padStart(7, '0').slice(-7);
const password = `Vr!Phase13-${tag}-A9`;
const ids = Object.fromEntries(
  [
    'ownerA',
    'ownerB',
    'outsider',
    'driver',
    'operator',
    'origin',
    'destination',
    'route',
    'vehicleType',
    'vehicle',
    'trip',
    'bookingA',
    'bookingB',
    'passengerA',
    'passengerB',
    'ticketA',
    'ticketB',
  ].map((name) => [name, randomUUID()]),
);
const emails = {
  ownerA: `phase13-a-${tag}@vietride.local`,
  ownerB: `phase13-b-${tag}@vietride.local`,
  outsider: `phase13-c-${tag}@vietride.local`,
  driver: `phase13-driver-${tag}@vietride.local`,
};
const rawShareTokens = new Set();
const grantIds = new Set();
const idempotencyKeys = new Set();
const eventIds = new Set();
const outboxIds = new Set();
const identityEventIds = new Set();
const identityOutboxIds = new Set();
const identityOtpEventIds = new Set();
const identityUserCreatedEventIds = new Set();
const emailDeliveryIds = new Set();
const sockets = new Set();
let redis;
let rabbitConnection;
let rabbitChannel;
let databasesReady = false;
let identitySeeded = false;
let tripSeeded = false;
let bookingSeeded = false;

function redact(value) {
  let output = String(value ?? '');
  for (const token of rawShareTokens) output = output.replaceAll(token, '[REDACTED_SHARE_TOKEN]');
  return output.replace(/Bearer\s+[A-Za-z0-9._~-]+/giu, 'Bearer [REDACTED]');
}

function blocked(message) {
  const error = new Error(`INTEGRATION_BLOCKED: ${message}`);
  error.integrationBlocked = true;
  throw error;
}

function run(command, args) {
  try {
    return execFileSync(command, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 }).trim();
  } catch (error) {
    throw new Error(`${command} failed: ${redact(error.stderr || error.stdout || error.message)}`);
  }
}

function sql(database, statement) {
  return run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    process.env.POSTGRES_USER || 'vietride',
    '-d',
    database,
    '-qAtc',
    statement,
  ]);
}

function literal(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

function key() {
  const value = randomUUID();
  idempotencyKeys.add(value);
  return value;
}

async function request(base, method, path, options = {}) {
  const headers = { Accept: 'application/json', ...options.headers };
  if (options.jwt) headers.Authorization = `Bearer ${options.jwt}`;
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}${path}`, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: AbortSignal.timeout(10_000),
  });
  const text = await response.text();
  let body = null;
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      throw new Error(`${method} ${path} returned non-JSON`);
    }
  }
  return { response, body };
}

async function poll(check, description, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await check()) return;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`Timed out waiting for ${description}`);
}

async function preflight() {
  if (process.env.NODE_ENV === 'production') blocked('production execution is forbidden');
  for (const value of [gatewayUrl, trackingUrl, notificationUrl]) {
    const url = new URL(value);
    if (!['localhost', '127.0.0.1'].includes(url.hostname))
      blocked(`non-local target ${url.hostname}`);
  }
  for (const [name, value] of [
    ['Redis', redisUrl],
    ['RabbitMQ', rabbitUrl],
  ]) {
    const url = new URL(value);
    if (!['localhost', '127.0.0.1'].includes(url.hostname)) blocked(`${name} target is not local`);
  }
  try {
    run('docker', ['version', '--format', '{{.Server.Version}}']);
  } catch {
    blocked('Docker daemon is unavailable');
  }
  for (const [name, url] of [
    ['Gateway', `${gatewayUrl}/health`],
    ['Tracking', `${trackingUrl}/health`],
    ['Identity', `${gatewayUrl}/v1/identity/health`],
    ['Trip', `${gatewayUrl}/v1/trip/health`],
    ['Booking', `${gatewayUrl}/v1/booking/health`],
    ['Payment', `${gatewayUrl}/v1/payment/health`],
    ['Parcel', `${gatewayUrl}/v1/parcel/health`],
    ['Notification', `${notificationUrl}/health`],
  ]) {
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(3_000) });
      if (!response.ok) blocked(`${name} health returned ${response.status}`);
    } catch (error) {
      blocked(`${name} is unavailable (${error.message})`);
    }
  }
  for (const database of [
    'vietride_identity',
    'vietride_trip',
    'vietride_booking',
    'vietride_parcel',
    'vietride_payment',
    'vietride_notification',
    'vietride_tracking',
  ]) {
    try {
      if (sql(database, 'SELECT 1') !== '1') blocked(`${database} is unavailable`);
    } catch (error) {
      blocked(`${database} is unavailable (${error.message})`);
    }
  }
  databasesReady = true;
  const redisProbe = new Redis(redisUrl, { lazyConnect: true, maxRetriesPerRequest: 1 });
  try {
    await redisProbe.connect();
    if ((await redisProbe.ping()) !== 'PONG') blocked('Redis PING failed');
  } catch (error) {
    blocked(`Redis is unavailable (${error.message})`);
  } finally {
    await redisProbe.quit().catch(() => redisProbe.disconnect());
  }
  try {
    const connection = await amqp.connect(rabbitUrl);
    await connection.close();
  } catch (error) {
    blocked(`RabbitMQ is unavailable (${error.message})`);
  }
  let tripEnvironment;
  try {
    tripEnvironment = run('docker', [
      'inspect',
      '-f',
      '{{range .Config.Env}}{{println .}}{{end}}',
      'vietride_trip',
    ]);
  } catch (error) {
    blocked(`Trip container is unavailable (${error.message})`);
  }
  if (/Trip__BackgroundWorkers__Enabled=false/iu.test(tripEnvironment))
    blocked('Trip background workers are disabled');
}

async function register(email, displayName, phone) {
  const result = await request(gatewayUrl, 'POST', '/v1/auth/register', {
    body: { email, password, displayName, phone },
    idempotencyKey: key(),
  });
  assert(
    [200, 201].includes(result.response.status),
    `Identity register failed (${result.response.status})`,
  );
  const userId = sql(
    'vietride_identity',
    `SET search_path TO vietride_identity,public; SELECT id FROM users WHERE lower(email)=lower(${literal(email)}) LIMIT 1;`,
  );
  assert.match(userId, /^[0-9a-f-]{36}$/iu);
  return userId;
}

async function login(email, role) {
  const result = await request(gatewayUrl, 'POST', '/v1/auth/login', { body: { email, password } });
  assert.equal(result.response.status, 200, `Identity login failed for ${role}`);
  assert.equal(result.body?.data?.user?.role, role);
  assert(result.body?.data?.accessToken, `Identity returned no ${role} access token`);
  return result.body.data.accessToken;
}

async function seedIdentity() {
  ids.ownerA = await register(emails.ownerA, `Phase13 Owner A ${tag}`, `+8491${phoneTail}`);
  ids.ownerB = await register(emails.ownerB, `Phase13 Owner B ${tag}`, `+8492${phoneTail}`);
  ids.outsider = await register(emails.outsider, `Phase13 Outsider ${tag}`, `+8493${phoneTail}`);
  ids.driver = await register(emails.driver, `Phase13 Driver ${tag}`, `+8494${phoneTail}`);
  sql(
    'vietride_identity',
    `SET search_path TO vietride_identity,public; BEGIN;
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
    VALUES ('${ids.operator}','Phase13 Operator ${tag}','P13-${tag}-BRN','P13-${tag}-TAX',${literal(emails.driver)},'+84990001300','APPROVED',now(),true);
    UPDATE users SET role='PASSENGER',status='ACTIVE',operator_id=NULL,deleted_at=NULL,failed_login_attempts=0 WHERE id IN ('${ids.ownerA}','${ids.ownerB}','${ids.outsider}');
    UPDATE users SET role='DRIVER',status='ACTIVE',operator_id='${ids.operator}',deleted_at=NULL,failed_login_attempts=0 WHERE id='${ids.driver}'; COMMIT;`,
  );
  identitySeeded = true;
  return {
    ownerA: await login(emails.ownerA, 'PASSENGER'),
    ownerB: await login(emails.ownerB, 'PASSENGER'),
    outsider: await login(emails.outsider, 'PASSENGER'),
    driver: await login(emails.driver, 'DRIVER'),
  };
}

function seedDomain() {
  sql(
    'vietride_trip',
    `SET search_path TO vietride_trip,public; BEGIN;
    INSERT INTO stations (id,name,slug,address_street,city,province,latitude,longitude,supports_shuttle,is_active) VALUES
    ('${ids.origin}','Phase13 Origin','phase13-origin-${tag}','1 Origin','Hồ Chí Minh','Hồ Chí Minh',10.7812,106.6981,false,true),
    ('${ids.destination}','Phase13 Destination','phase13-destination-${tag}','2 Destination','Đà Lạt','Lâm Đồng',11.9404,108.4583,false,true);
    INSERT INTO routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,path_polyline,is_active)
    VALUES ('${ids.route}','${ids.operator}','Phase13 Route ${tag}','${ids.origin}','${ids.destination}',100000,'_p~iF~ps|U_ulLnnqC_mqNvxq\`@',true);
    INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_system_defined,is_active)
    VALUES ('${ids.vehicleType}','P13${tag.slice(0, 5).toUpperCase()}','Phase13 Coach',40,false,true);
    INSERT INTO vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active)
    VALUES ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','P13-${tag.slice(0, 6)}','{}'::jsonb,40,'ACTIVE',true);
    INSERT INTO trips (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare)
    VALUES ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}',now()-interval '1 hour',now()+interval '3 hours','IN_PROGRESS','MANUAL',100000); COMMIT;`,
  );
  tripSeeded = true;
  sql(
    'vietride_booking',
    `SET search_path TO vietride_booking,public; BEGIN;
    INSERT INTO bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,base_fare,discount_amount,total_amount,status,confirmed_at,created_at,updated_at) VALUES
    ('${ids.bookingA}','P13A-${tag}','${ids.ownerA}','${ids.trip}','${ids.operator}','${ids.origin}','${ids.destination}',100000,0,100000,'CONFIRMED',now(),now(),now()),
    ('${ids.bookingB}','P13B-${tag}','${ids.ownerB}','${ids.trip}','${ids.operator}','${ids.origin}','${ids.destination}',100000,0,100000,'CONFIRMED',now(),now(),now());
    INSERT INTO passengers (id,booking_id,seat_number,boarding_status,boarded_at) VALUES
    ('${ids.passengerA}','${ids.bookingA}','A01','BOARDED',now()),('${ids.passengerB}','${ids.bookingB}','A02','BOARDED',now());
    INSERT INTO tickets (id,booking_id,passenger_id,ticket_code,seat_number,status,fare_amount,discount_amount,paid_amount,issued_at,used_at) VALUES
    ('${ids.ticketA}','${ids.bookingA}','${ids.passengerA}','P13TA-${tag}','A01','USED',100000,0,100000,now(),now()),
    ('${ids.ticketB}','${ids.bookingB}','${ids.passengerB}','P13TB-${tag}','A02','USED',100000,0,100000,now(),now()); COMMIT;`,
  );
  bookingSeeded = true;
}

function shareToken(envelope) {
  assert(envelope?.success && envelope.data?.shareUrl, 'Share response envelope was invalid');
  const token = new URLSearchParams(new URL(envelope.data.shareUrl).hash.slice(1)).get('token');
  assert(token?.startsWith('v1.'), 'Share fragment was malformed');
  rawShareTokens.add(token);
  grantIds.add(token.split('.')[1]);
  return token;
}

async function owner(method, jwt, requestKey = key()) {
  return request(gatewayUrl, method, `/v1/tracking/trips/${ids.trip}/share-link`, {
    jwt,
    idempotencyKey: requestKey,
  });
}

function connect(url, auth) {
  const socket = io(url, {
    path: '/tracking/socket.io',
    auth,
    transports: ['websocket'],
    forceNew: true,
    reconnection: false,
    timeout: 8_000,
  });
  sockets.add(socket);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Socket connect timeout')), 8_000);
    socket.once('connect', () => {
      clearTimeout(timer);
      resolve(socket);
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timer);
      reject(new Error(`Socket rejected: ${error.message}`));
    });
  });
}

function event(socket, name, timeout = 10_000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`${name} timeout`)), timeout);
    socket.once(name, (value) => {
      clearTimeout(timer);
      resolve(value);
    });
  });
}

function disconnected(socket, timeout = 10_000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Socket disconnect timeout')), timeout);
    socket.once('disconnect', (reason) => {
      clearTimeout(timer);
      resolve(reason);
    });
  });
}

function emitAck(socket, name, payload) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`${name} ack timeout`)), 8_000);
    socket.emit(name, payload, (ack) => {
      clearTimeout(timer);
      resolve(ack);
    });
  });
}

function exactKeys(value, expected, path) {
  assert(value && typeof value === 'object' && !Array.isArray(value), `${path} was not an object`);
  assert.deepEqual(Object.keys(value).sort(), [...expected].sort(), `${path} allow-list drifted`);
}

function assertPublicContext(data) {
  exactKeys(data, ['status', 'expiresAt', 'lastUpdatedAt', 'vehicle', 'route', 'eta'], 'context');
  exactKeys(data.vehicle, ['location'], 'context.vehicle');
  exactKeys(data.route, ['originName', 'destinationName', 'geometry'], 'context.route');
  if (data.vehicle.location)
    exactKeys(
      data.vehicle.location,
      ['latitude', 'longitude', 'heading', 'speedKph', 'recordedAt'],
      'context.location',
    );
  if (data.route.geometry)
    exactKeys(data.route.geometry, ['type', 'coordinates'], 'context.geometry');
  if (data.eta)
    exactKeys(
      data.eta,
      ['estimatedArrivalAt', 'remainingSeconds', 'delayMinutes', 'updatedAt'],
      'context.eta',
    );
}

function assertSharedGps(payload) {
  exactKeys(payload, ['location'], 'shared:gps:update');
  exactKeys(
    payload.location,
    ['latitude', 'longitude', 'heading', 'speedKph', 'recordedAt'],
    'shared:gps:update.location',
  );
}

async function journey(tokens) {
  const denied = await owner('PUT', tokens.outsider);
  assert.equal(denied.response.status, 403, 'Outsider was not denied');
  const replayKey = key();
  const first = await owner('PUT', tokens.ownerA, replayKey);
  const replay = await owner('PUT', tokens.ownerA, replayKey);
  const tokenA = shareToken(first.body);
  shareToken(replay.body);
  assert.deepEqual(replay.body?.data, first.body?.data, 'Same-key replay changed link');
  assert.equal(
    shareToken((await owner('PUT', tokens.ownerA)).body),
    tokenA,
    'New key changed active link',
  );
  const concurrent = await Promise.all(
    Array.from({ length: 8 }, () => owner('PUT', tokens.ownerA)),
  );
  assert(concurrent.every((x) => x.response.status === 200 && shareToken(x.body) === tokenA));
  assert.equal(
    Number(
      sql(
        'vietride_tracking',
        `SET search_path TO vietride_tracking,public; SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND created_by_user_id='${ids.ownerA}' AND revoked_at IS NULL;`,
      ),
    ),
    1,
  );
  const tokenB = shareToken((await owner('PUT', tokens.ownerB)).body);
  assert.notEqual(tokenA, tokenB);
  const context = await request(gatewayUrl, 'GET', '/v1/tracking/shared-trip/context', {
    headers: { 'X-Trip-Share-Token': tokenA },
  });
  assert.equal(context.response.status, 200);
  assert.equal(context.response.headers.get('cache-control'), 'no-store');
  assert.equal(context.response.headers.get('pragma'), 'no-cache');
  assert.equal(context.response.headers.get('referrer-policy'), 'no-referrer');
  assertPublicContext(context.body?.data);

  const guestA = await connect(`${trackingUrl}/shared`, { shareToken: tokenA });
  const guestB = await connect(`${trackingUrl}/shared`, { shareToken: tokenB });
  const driver = await connect(trackingUrl, { token: tokens.driver });
  const gpsA = event(guestA, 'shared:gps:update');
  const gpsB = event(guestB, 'shared:gps:update');
  const recordedAt = new Date().toISOString();
  assert(
    (
      await emitAck(driver, 'gps:update', {
        tripId: ids.trip,
        latitude: 10.7812,
        longitude: 106.6981,
        speedKmh: 35,
        headingDeg: 45,
        recordedAt,
      })
    ).success,
  );
  assertSharedGps(await gpsA);
  assertSharedGps(await gpsB);
  const revokedA = event(guestA, 'shared:access:revoked');
  const disconnectedA = disconnected(guestA);
  assert.equal((await owner('DELETE', tokens.ownerA)).response.status, 200);
  assert.equal((await revokedA).reason, 'REVOKED');
  await disconnectedA;
  const gpsBAfter = event(guestB, 'shared:gps:update');
  assert(
    (
      await emitAck(driver, 'gps:update', {
        tripId: ids.trip,
        latitude: 10.782,
        longitude: 106.699,
        speedKmh: 34,
        headingDeg: 46,
        recordedAt: new Date(Date.now() + 1000).toISOString(),
      })
    ).success,
  );
  await gpsBAfter;
  const replacementA = shareToken((await owner('PUT', tokens.ownerA)).body);
  const replacementSocket = await connect(`${trackingUrl}/shared`, { shareToken: replacementA });
  const terminalA = event(replacementSocket, 'shared:access:revoked', 60_000);
  const terminalB = event(guestB, 'shared:access:revoked', 60_000);
  const terminalDisconnectA = disconnected(replacementSocket, 60_000);
  const terminalDisconnectB = disconnected(guestB, 60_000);
  const completed = await request(gatewayUrl, 'POST', `/v1/driver/trips/${ids.trip}/complete`, {
    jwt: tokens.driver,
    idempotencyKey: key(),
  });
  assert.equal(completed.response.status, 200, 'Real Trip completion failed');
  const outbox = await pollOutbox();
  const [terminalEventA, terminalEventB] = await Promise.all([terminalA, terminalB]);
  assert.equal(terminalEventA.reason, 'TRIP_ENDED');
  assert.equal(terminalEventB.reason, 'TRIP_ENDED');
  await Promise.all([terminalDisconnectA, terminalDisconnectB]);
  await poll(
    () =>
      Number(
        sql(
          'vietride_tracking',
          `SET search_path TO vietride_tracking,public; SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND revoked_at IS NULL;`,
        ),
      ) === 0,
    'Tracking terminal revocation',
  );
  await publishDuplicate(outbox);
  await verifyNoLeak();
}

async function pollOutbox() {
  let row;
  await poll(
    () => {
      const value = sql(
        'vietride_trip',
        `SET search_path TO vietride_trip,public; SELECT id || '|' || payload FROM outbox_events WHERE event_type='trip.trip.completed' AND payload::jsonb->>'tripId'='${ids.trip}' AND status='PUBLISHED' ORDER BY created_at DESC LIMIT 1;`,
      );
      if (!value) return false;
      const separator = value.indexOf('|');
      row = { id: value.slice(0, separator), payload: JSON.parse(value.slice(separator + 1)) };
      assert.match(row.payload.eventId, /^[0-9a-f-]{36}$/iu, 'Trip event omitted eventId');
      row.eventId = row.payload.eventId;
      outboxIds.add(row.id);
      eventIds.add(row.eventId);
      return true;
    },
    'Trip outbox PUBLISHED',
    60_000,
  );
  return row;
}

async function publishDuplicate(outbox) {
  const processedKey = `tracking:trip-share:event:processed:${outbox.eventId}`;
  const markerBefore = await redis.get(processedKey);
  const ttlBefore = await redis.ttl(processedKey);
  const snapshotSql = `SET search_path TO vietride_tracking,public; SELECT string_agg(id::text || ':' || updated_at::text,',' ORDER BY id) FROM trip_share_grants WHERE trip_id='${ids.trip}';`;
  const grantsBefore = sql('vietride_tracking', snapshotSql);
  assert(markerBefore && ttlBefore > 0, 'Original terminal event lacks a processed marker');
  rabbitConnection = await amqp.connect(rabbitUrl);
  rabbitChannel = await rabbitConnection.createConfirmChannel();
  rabbitChannel.publish(
    'vietride.events',
    'trip.trip.completed',
    Buffer.from(JSON.stringify(outbox.payload)),
    {
      contentType: 'application/json',
      persistent: true,
      messageId: outbox.eventId,
      correlationId: outbox.eventId,
    },
  );
  await rabbitChannel.waitForConfirms();
  await new Promise((resolve) => setTimeout(resolve, 1000));
  assert.equal(await redis.get(processedKey), markerBefore, 'Duplicate replaced processed marker');
  assert((await redis.ttl(processedKey)) <= ttlBefore, 'Duplicate refreshed processed marker TTL');
  assert.equal(sql('vietride_tracking', snapshotSql), grantsBefore, 'Duplicate mutated grants');
}

async function redisKeys() {
  let cursor = '0';
  const keys = [];
  do {
    const [next, page] = await redis.scan(cursor, 'COUNT', 200);
    cursor = next;
    keys.push(...page);
  } while (cursor !== '0');
  return keys;
}

async function verifyNoLeak() {
  const keys = await redisKeys();
  const values = await Promise.all(
    keys.map(async (redisKey) => (await redis.dump(redisKey))?.toString('utf8') || ''),
  );
  const dbDump = run('docker', [
    'exec',
    containers.postgres,
    'pg_dump',
    '-U',
    process.env.POSTGRES_USER || 'vietride',
    '-d',
    'vietride_tracking',
    '--data-only',
  ]);
  const logs = `${run('docker', ['logs', containers.tracking])}\n${run('docker', ['logs', containers.gateway])}`;
  for (const token of rawShareTokens) {
    assert(!dbDump.includes(token) && !logs.includes(token));
    assert(!keys.some((x) => x.includes(token)) && !values.some((x) => x.includes(token)));
  }
}

function discoverIdentityEvents() {
  const rows = sql(
    'vietride_identity',
    `SET search_path TO vietride_identity,public; SELECT id || '|' || event_type FROM outbox_events WHERE payload::jsonb->>'userId' IN ('${ids.ownerA}','${ids.ownerB}','${ids.outsider}','${ids.driver}');`,
  );
  for (const row of rows.split(/\r?\n/u).filter(Boolean)) {
    const [eventId, eventType] = row.split('|');
    identityOutboxIds.add(eventId);
    identityEventIds.add(eventId);
    if (eventType === 'identity.otp.requested') identityOtpEventIds.add(eventId);
    if (eventType === 'identity.user.created') identityUserCreatedEventIds.add(eventId);
  }
}

function discoverTripEvents() {
  if (!tripSeeded) return;
  const discovered = sql(
    'vietride_trip',
    `SET search_path TO vietride_trip,public; SELECT id || '|' || COALESCE(payload::jsonb->>'eventId','') FROM outbox_events WHERE event_type='trip.trip.completed' AND payload::jsonb->>'tripId'='${ids.trip}';`,
  );
  for (const value of discovered.split(/\r?\n/u).filter(Boolean)) {
    const [outboxId, eventId] = value.split('|');
    outboxIds.add(outboxId);
    if (eventId) eventIds.add(eventId);
  }
}

function discoverEmailDeliveries() {
  if (!identityOtpEventIds.size) return;
  const dedupeKeys = [...identityOtpEventIds]
    .map((eventId) => literal(`identity.otp.requested:${eventId}:email`))
    .join(',');
  const rows = sql(
    'vietride_notification',
    `SET search_path TO vietride_notification,public; SELECT id FROM email_deliveries WHERE dedupe_key IN (${dedupeKeys});`,
  );
  for (const id of rows.split(/\r?\n/u).filter(Boolean)) emailDeliveryIds.add(id);
  assert.equal(
    emailDeliveryIds.size,
    identityOtpEventIds.size,
    'Notification did not create exactly one email delivery per OTP event',
  );
}

async function quiesceIdentitySideEffects() {
  const outboxList =
    [...identityOutboxIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
  await poll(
    () =>
      Number(
        sql(
          'vietride_identity',
          `SET search_path TO vietride_identity,public; SELECT count(*) FROM outbox_events WHERE id IN (${outboxList}) AND status <> 'PUBLISHED';`,
        ),
      ) === 0,
    'run-owned Identity Outbox publication',
    30_000,
  );
  const userCreatedList =
    [...identityUserCreatedEventIds].map(literal).join(',') ||
    "'00000000-0000-0000-0000-000000000000'";
  if (identityUserCreatedEventIds.size)
    await poll(
      () =>
        Number(
          sql(
            'vietride_payment',
            `SET search_path TO vietride_payment,public; SELECT count(*) FROM processed_integration_events WHERE event_id IN (${userCreatedList});`,
          ),
        ) === identityUserCreatedEventIds.size,
      'Payment identity.user.created consumers',
      30_000,
    );
  const otpList =
    [...identityOtpEventIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
  if (identityOtpEventIds.size)
    await poll(
      () =>
        Number(
          sql(
            'vietride_notification',
            `SET search_path TO vietride_notification,public; SELECT count(*) FROM processed_messages WHERE message_id IN (${otpList});`,
          ),
        ) === identityOtpEventIds.size,
      'Notification identity.otp.requested consumers',
      30_000,
    );
  discoverEmailDeliveries();
  if (emailDeliveryIds.size)
    await poll(
      () =>
        Number(
          sql(
            'vietride_notification',
            `SET search_path TO vietride_notification,public; SELECT count(*) FROM email_deliveries WHERE id IN (${[
              ...emailDeliveryIds,
            ]
              .map(literal)
              .join(',')}) AND status NOT IN ('SENT','FAILED');`,
          ),
        ) === 0,
      'Notification email deliveries to become terminal',
      60_000,
    );
}

async function quiesceTripTerminalSideEffects() {
  if (!eventIds.size) return;
  const eventList = [...eventIds].map(literal).join(',');
  await poll(
    () =>
      Number(
        sql(
          'vietride_booking',
          `SET search_path TO vietride_booking,public; SELECT count(DISTINCT message_id) FROM integration_inbox WHERE message_id IN (${eventList});`,
        ),
      ) === eventIds.size,
    'Booking trip.trip.completed consumers',
    60_000,
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_parcel',
          `SET search_path TO vietride_parcel,public; SELECT count(DISTINCT message_id) FROM integration_inbox WHERE message_id IN (${eventList});`,
        ),
      ) === eventIds.size,
    'Parcel trip.trip.completed consumers',
    60_000,
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_payment',
          `SET search_path TO vietride_payment,public; SELECT count(*) FROM processed_integration_events WHERE event_id IN (${eventList}) AND consumer IN ('payment.trip-completed-settlement','payment.trip-terminal-settlement');`,
        ),
      ) ===
      eventIds.size * 2,
    'Payment trip.trip.completed consumers',
    60_000,
  );
}

async function removeEmailJobs() {
  if (!emailDeliveryIds.size) return;
  const connection = new Redis(redisUrl, { maxRetriesPerRequest: null });
  const queue = new Queue('email-send', { connection, prefix: 'notification' });
  try {
    for (const deliveryId of emailDeliveryIds) {
      await poll(
        async () => {
          const current = await queue.getJob(deliveryId);
          if (!current) return true;
          return ['completed', 'failed'].includes(await current.getState());
        },
        `Notification BullMQ job ${deliveryId} to become terminal`,
        60_000,
      );
      const job = await queue.getJob(deliveryId);
      if (job) await job.remove();
      assert.equal(await queue.getJob(deliveryId), undefined, `Email job ${deliveryId} remained`);
    }
  } finally {
    await queue.close().catch(() => undefined);
    if (connection.status !== 'end') await connection.quit().catch(() => connection.disconnect());
  }
}

async function cleanup() {
  for (const socket of sockets) socket.close();
  if (rabbitChannel) await rabbitChannel.close().catch(() => undefined);
  if (rabbitConnection) await rabbitConnection.close().catch(() => undefined);
  if (databasesReady) {
    discoverIdentityEvents();
    discoverTripEvents();
    await quiesceIdentitySideEffects();
    await quiesceTripTerminalSideEffects();
  }
  if (redis?.status === 'ready') {
    await removeEmailJobs();
    await redis.srem('tracking:active_trips', ids.trip);
    const tokenHashes = [...rawShareTokens].map((token) =>
      createHash('sha256').update(token, 'utf8').digest('hex'),
    );
    const operationHashes = [...idempotencyKeys].map((value) =>
      createHash('sha256').update(value.toLowerCase(), 'utf8').digest('hex'),
    );
    const markers = [
      runId,
      ...Object.values(ids),
      ...grantIds,
      ...eventIds,
      ...identityEventIds,
      ...emailDeliveryIds,
      ...Object.values(emails),
      ...tokenHashes,
      ...operationHashes,
      ...operationHashes.map((value) => value.toUpperCase()),
    ];
    for (const email of Object.values(emails))
      await redis.del(`identity:otp_rate:${email.toLowerCase()}`);
    const keys = await redisKeys();
    const owned = [];
    for (const redisKey of keys) {
      if (redisKey === 'tracking:active_trips') continue;
      const dump = (await redis.dump(redisKey))?.toString('utf8') || '';
      const dedicated = [
        'tracking:idem:trip-share:',
        'tracking:trip-share:event:',
        'tracking:trip-share:rate:',
        'tracking:latest:',
        'tracking:gps_buffer:',
        'tracking:gps:idempotency:',
        'identity:idem:v2:',
        'notification:idem:',
        'trip:idem:v2:',
      ].some((prefix) => redisKey.startsWith(prefix));
      if (dedicated && markers.some((marker) => redisKey.includes(marker) || dump.includes(marker)))
        owned.push(redisKey);
    }
    if (owned.length) await redis.del(...owned);
    for (const redisKey of await redisKeys()) {
      if (redisKey === 'tracking:active_trips') continue;
      const dump = (await redis.dump(redisKey))?.toString('utf8') || '';
      assert(
        !markers.some((marker) => redisKey.includes(marker) || dump.includes(marker)),
        `Run-owned Redis residue remained in ${redisKey}`,
      );
    }
    await redis.quit().catch(() => redis.disconnect());
  }
  if (!databasesReady) return;
  const identityEventList =
    [...identityEventIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
  const identityOutboxList =
    [...identityOutboxIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
  const tripEventList =
    [...eventIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
  const userList = `('${ids.ownerA}','${ids.ownerB}','${ids.outsider}','${ids.driver}')`;
  sql(
    'vietride_notification',
    `SET search_path TO vietride_notification,public; DELETE FROM email_deliveries WHERE to_email IN (${Object.values(emails).map(literal).join(',')}); DELETE FROM processed_messages WHERE message_id IN (${identityEventList});`,
  );
  sql(
    'vietride_payment',
    `SET search_path TO vietride_payment,public; DELETE FROM operator_trip_settlements WHERE trip_id='${ids.trip}'; DELETE FROM wallet_transactions WHERE user_id IN ${userList}; DELETE FROM wallets WHERE user_id IN ${userList}; DELETE FROM processed_integration_events WHERE event_id IN (${identityEventList},${tripEventList});`,
  );
  sql(
    'vietride_parcel',
    `SET search_path TO vietride_parcel,public; DELETE FROM integration_inbox WHERE message_id IN (${tripEventList});`,
  );
  if (bookingSeeded) {
    sql(
      'vietride_booking',
      `SET search_path TO vietride_booking,public; DELETE FROM integration_inbox WHERE message_id IN (${tripEventList}); DELETE FROM booking_status_history WHERE booking_id IN ('${ids.bookingA}','${ids.bookingB}'); DELETE FROM tickets WHERE id IN ('${ids.ticketA}','${ids.ticketB}'); DELETE FROM passengers WHERE id IN ('${ids.passengerA}','${ids.passengerB}'); DELETE FROM bookings WHERE id IN ('${ids.bookingA}','${ids.bookingB}');`,
    );
  }
  if (tripSeeded) {
    const outboxList =
      [...outboxIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'";
    sql(
      'vietride_tracking',
      `SET search_path TO vietride_tracking,public; DELETE FROM gps_trails WHERE trip_id='${ids.trip}'; DELETE FROM trip_share_grants WHERE trip_id='${ids.trip}';`,
    );
    sql(
      'vietride_trip',
      `SET search_path TO vietride_trip,public; DELETE FROM trip_audit_logs WHERE trip_id='${ids.trip}'; DELETE FROM outbox_events WHERE id IN (${outboxList}); DELETE FROM trips WHERE id='${ids.trip}'; DELETE FROM vehicles WHERE id='${ids.vehicle}'; DELETE FROM vehicle_types WHERE id='${ids.vehicleType}'; DELETE FROM routes WHERE id='${ids.route}'; DELETE FROM stations WHERE id IN ('${ids.origin}','${ids.destination}');`,
    );
  }
  if (identitySeeded || Object.values(emails).length > 0) {
    sql(
      'vietride_identity',
      `SET search_path TO vietride_identity,public; DELETE FROM activity_logs WHERE user_id IN ${userList}; DELETE FROM user_devices WHERE user_id IN ${userList}; DELETE FROM oauth_identities WHERE user_id IN ${userList}; DELETE FROM refresh_tokens WHERE user_id IN ${userList}; DELETE FROM email_verification_tokens WHERE user_id IN ${userList}; DELETE FROM outbox_events WHERE id IN (${identityOutboxList}); DELETE FROM users WHERE id IN ${userList} OR email IN (${Object.values(emails).map(literal).join(',')}); DELETE FROM operators WHERE id='${ids.operator}';`,
    );
  }
  for (const [database, query] of [
    [
      'vietride_tracking',
      `SET search_path TO vietride_tracking,public; SELECT (SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}') + (SELECT count(*) FROM gps_trails WHERE trip_id='${ids.trip}');`,
    ],
    [
      'vietride_booking',
      `SET search_path TO vietride_booking,public; SELECT (SELECT count(*) FROM bookings WHERE id IN ('${ids.bookingA}','${ids.bookingB}')) + (SELECT count(*) FROM passengers WHERE id IN ('${ids.passengerA}','${ids.passengerB}')) + (SELECT count(*) FROM tickets WHERE id IN ('${ids.ticketA}','${ids.ticketB}')) + (SELECT count(*) FROM booking_status_history WHERE booking_id IN ('${ids.bookingA}','${ids.bookingB}')) + (SELECT count(*) FROM integration_inbox WHERE message_id IN (${tripEventList}));`,
    ],
    [
      'vietride_parcel',
      `SET search_path TO vietride_parcel,public; SELECT count(*) FROM integration_inbox WHERE message_id IN (${tripEventList});`,
    ],
    [
      'vietride_trip',
      `SET search_path TO vietride_trip,public; SELECT (SELECT count(*) FROM trips WHERE id='${ids.trip}') + (SELECT count(*) FROM trip_audit_logs WHERE trip_id='${ids.trip}') + (SELECT count(*) FROM outbox_events WHERE id IN (${[...outboxIds].map(literal).join(',') || "'00000000-0000-0000-0000-000000000000'"})) + (SELECT count(*) FROM vehicles WHERE id='${ids.vehicle}') + (SELECT count(*) FROM vehicle_types WHERE id='${ids.vehicleType}') + (SELECT count(*) FROM routes WHERE id='${ids.route}') + (SELECT count(*) FROM stations WHERE id IN ('${ids.origin}','${ids.destination}'));`,
    ],
    [
      'vietride_identity',
      `SET search_path TO vietride_identity,public; SELECT (SELECT count(*) FROM users WHERE id IN ${userList} OR email IN (${Object.values(emails).map(literal).join(',')})) + (SELECT count(*) FROM operators WHERE id='${ids.operator}') + (SELECT count(*) FROM activity_logs WHERE user_id IN ${userList}) + (SELECT count(*) FROM user_devices WHERE user_id IN ${userList}) + (SELECT count(*) FROM oauth_identities WHERE user_id IN ${userList}) + (SELECT count(*) FROM refresh_tokens WHERE user_id IN ${userList}) + (SELECT count(*) FROM email_verification_tokens WHERE user_id IN ${userList}) + (SELECT count(*) FROM outbox_events WHERE id IN (${identityOutboxList}));`,
    ],
    [
      'vietride_payment',
      `SET search_path TO vietride_payment,public; SELECT (SELECT count(*) FROM operator_trip_settlements WHERE trip_id='${ids.trip}') + (SELECT count(*) FROM wallets WHERE user_id IN ${userList}) + (SELECT count(*) FROM wallet_transactions WHERE user_id IN ${userList}) + (SELECT count(*) FROM processed_integration_events WHERE event_id IN (${identityEventList},${tripEventList}));`,
    ],
    [
      'vietride_notification',
      `SET search_path TO vietride_notification,public; SELECT (SELECT count(*) FROM email_deliveries WHERE to_email IN (${Object.values(emails).map(literal).join(',')})) + (SELECT count(*) FROM processed_messages WHERE message_id IN (${identityEventList}));`,
    ],
  ])
    assert.equal(Number(sql(database, query)), 0, `${database} retained a Phase13 fixture`);
}

async function main() {
  try {
    await preflight();
    redis = new Redis(redisUrl, { lazyConnect: true, maxRetriesPerRequest: 1 });
    await redis.connect();
    const tokens = await seedIdentity();
    seedDomain();
    await journey(tokens);
    console.log('PASS | Phase 13 full local-stack real API journey');
  } catch (error) {
    const prefix = error.integrationBlocked ? 'INTEGRATION_BLOCKED' : 'FAIL';
    console.error(`${prefix} | ${redact(error.stack || error.message)}`);
    process.exitCode = 1;
  } finally {
    try {
      await cleanup();
    } catch (error) {
      console.error(`FAIL | cleanup: ${redact(error.stack || error.message)}`);
      process.exitCode = 1;
    }
  }
}

void main();
