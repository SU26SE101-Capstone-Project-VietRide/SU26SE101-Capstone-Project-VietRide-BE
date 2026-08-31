import amqp from 'amqplib';
import { spawnSync } from 'node:child_process';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { once } from 'node:events';
import http from 'node:http';
import { exportJWK, generateKeyPair, SignJWT } from 'jose';
import Redis from 'ioredis';
import { io } from 'socket.io-client';

const root = process.cwd();
const noBuild = process.argv.includes('--no-build');
const ports = {
  tracking: 56421,
  fake: 56431,
  postgres: 55481,
  redis: 56398,
  rabbitmq: 55698,
  rabbitManagement: 55699,
};
const trackingUrl = `http://127.0.0.1:${ports.tracking}`;
const fakeUrl = `http://127.0.0.1:${ports.fake}`;
const databaseUrl = `postgresql://vietride:vietride_dev@127.0.0.1:${ports.postgres}/vietride_tracking`;
const runUuid = randomUUID();
const runTag = runUuid.replaceAll('-', '').slice(0, 12);
const prefix = `tracking-sharing-e2e-${runTag}`;
const containers = {
  postgres: `${prefix}-postgres`,
  redis: `${prefix}-redis`,
  rabbitmq: `${prefix}-rabbitmq`,
  tracking: `${prefix}-tracking`,
};
const secrets = {
  internal: randomBytes(36).toString('base64url'),
  sharing: randomBytes(48).toString('base64url'),
};
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-p',
  prefix,
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.tracking-sharing-e2e.yml',
  '--profile',
  'infra',
  '--profile',
  'app',
];
const ids = {
  trip: randomUUID(),
  replacementTrip: randomUUID(),
  replacementVehicle: randomUUID(),
  ownerA: randomUUID(),
  ownerB: randomUUID(),
  outsider: randomUUID(),
  driver: randomUUID(),
  operator: randomUUID(),
  originStation: randomUUID(),
  destinationStation: randomUUID(),
  routeStop: randomUUID(),
};
const ownerIds = new Set([ids.ownerA, ids.ownerB]);
const idempotencyKeys = new Set();
const eventIds = new Set();
const rawTokens = new Set();
const tokenHashes = new Set();
const grantIds = new Set();
const sockets = new Set();
const recordedAtValues = [];
let fakeServer;
let jwk;
let signingKey;
let redisClient;
let rabbitConnection;
let rabbitChannel;
let tripStatus = 'IN_PROGRESS';
let replacementTripStatus = 'BOARDING';
let composeAttempted = false;
let infraStarted = false;

function composeEnv(extra = {}) {
  return {
    POSTGRES_PORT: String(ports.postgres),
    REDIS_PORT: String(ports.redis),
    RABBITMQ_PORT: String(ports.rabbitmq),
    RABBITMQ_MGMT_PORT: String(ports.rabbitManagement),
    TRACKING_PORT: String(ports.tracking),
    TRACKING_SHARING_E2E_PREFIX: prefix,
    TRACKING_SHARING_E2E_POSTGRES_PORT: String(ports.postgres),
    TRACKING_SHARING_E2E_REDIS_PORT: String(ports.redis),
    TRACKING_SHARING_E2E_RABBITMQ_PORT: String(ports.rabbitmq),
    TRACKING_SHARING_E2E_RABBITMQ_MGMT_PORT: String(ports.rabbitManagement),
    TRACKING_SHARING_E2E_TRACKING_PORT: String(ports.tracking),
    TRACKING_SHARING_E2E_FAKE_PORT: String(ports.fake),
    TRACKING_SHARING_E2E_INTERNAL_JWT_SECRET: secrets.internal,
    TRACKING_SHARING_E2E_SHARE_SECRET: secrets.sharing,
    TRACKING_SHARING_E2E_SHARE_TTL_SECONDS: '15',
    INTERNAL_JWT_SECRET: secrets.internal,
    TRACKING_SHARE_TOKEN_SECRET: secrets.sharing,
    POSTGRES_USER: 'vietride',
    POSTGRES_PASSWORD: 'vietride_dev',
    RABBITMQ_USER: 'vietride',
    RABBITMQ_PASSWORD: 'vietride_dev',
    ...extra,
  };
}

function redact(value) {
  let output = String(value ?? '');
  for (const token of rawTokens) output = output.replaceAll(token, '[REDACTED_SHARE_TOKEN]');
  output = output.replaceAll(secrets.internal, '[REDACTED_INTERNAL_SECRET]');
  output = output.replaceAll(secrets.sharing, '[REDACTED_SHARE_SECRET]');
  return output;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
    env: { ...process.env, ...options.env },
  });
  if (result.status !== 0) {
    const detail = result.stderr || result.stdout || result.error?.message || 'unknown error';
    throw new Error(
      `${command} failed (${result.status ?? result.error?.code ?? result.signal}): ${detail}`,
    );
  }
  return result.stdout.trim();
}

function composeRun(args, options = {}) {
  return run('docker', [...compose, ...args], {
    ...options,
    env: composeEnv(options.env),
  });
}

function sql(statement) {
  return run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    'vietride',
    '-d',
    'vietride_tracking',
    '-qAtc',
    `SET search_path TO vietride_tracking,public; ${statement}`,
  ]);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function poll(check, message, timeoutMs = 30_000, intervalMs = 150) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await check();
    if (value) return value;
    await delay(intervalMs);
  }
  throw new Error(typeof message === 'function' ? message() : message);
}

function safeUuidV4() {
  const value = randomUUID();
  idempotencyKeys.add(value);
  return value;
}

async function startFakeBoundaries() {
  const keys = await generateKeyPair('RS256');
  signingKey = keys.privateKey;
  jwk = {
    ...(await exportJWK(keys.publicKey)),
    kid: 'tracking-sharing-e2e',
    alg: 'RS256',
    use: 'sig',
  };

  fakeServer = http.createServer((request, response) => {
    const url = new URL(request.url ?? '/', fakeUrl);
    response.setHeader('Content-Type', 'application/json');

    if (url.pathname === '/v1/.well-known/jwks.json') {
      response.end(JSON.stringify({ keys: [jwk] }));
      return;
    }

    const bookingMatch = url.pathname.match(
      /^\/internal\/v1\/trips\/([0-9a-f-]+)\/tracking-authorization\/bookings$/i,
    );
    if (bookingMatch) {
      const userId = url.searchParams.get('userId');
      const allowed =
        bookingMatch[1].toLowerCase() === ids.trip && userId !== null && ownerIds.has(userId);
      response.statusCode = allowed ? 200 : 403;
      response.end(
        JSON.stringify(
          allowed ? { allowed: true, scope: 'BOOKING_OWNER' } : { allowed: false, scope: 'NONE' },
        ),
      );
      return;
    }

    const trackingAuthMatch = url.pathname.match(
      /^\/internal\/v1\/trips\/([0-9a-f-]+)\/tracking-authorization$/i,
    );
    if (trackingAuthMatch) {
      const allowed = [ids.trip, ids.replacementTrip].includes(
        trackingAuthMatch[1].toLowerCase(),
      );
      response.statusCode = allowed ? 200 : 403;
      response.end(JSON.stringify({ allowed, scope: allowed ? 'DRIVER' : 'NONE' }));
      return;
    }

    const tripMatch = url.pathname.match(/^\/internal\/v1\/trips\/([0-9a-f-]+)$/i);
    if (tripMatch) {
      const tripId = tripMatch[1].toLowerCase();
      if (![ids.trip, ids.replacementTrip].includes(tripId)) {
        response.statusCode = 404;
        response.end(JSON.stringify({ errorCode: 'TRIP_NOT_FOUND' }));
        return;
      }
      response.end(
        JSON.stringify({
          tripId,
          status: tripId === ids.trip ? tripStatus : replacementTripStatus,
        }),
      );
      return;
    }

    const geometryMatch = url.pathname.match(
      /^\/internal\/v1\/trips\/([0-9a-f-]+)\/route-geometry$/i,
    );
    if (geometryMatch) {
      response.end(
        JSON.stringify({
          success: true,
          data: {
            tripId: geometryMatch[1].toLowerCase(),
            points: [
              { latitude: 10.7812, longitude: 106.6981 },
              { latitude: 10.8123, longitude: 106.7214 },
            ],
            geometrySource: 'ROUTE_POLYLINE',
            originStation: {
              stationId: ids.originStation,
              name: 'Bến xe Miền Đông',
              latitude: 10.7812,
              longitude: 106.6981,
            },
            intermediateStops: [
              {
                stopId: ids.routeStop,
                name: 'Trạm dừng Bảo Lộc',
                sequence: 1,
                latitude: 11.5475,
                longitude: 107.8078,
              },
            ],
            destinationStation: {
              stationId: ids.destinationStation,
              name: 'Bến xe Đà Lạt',
              latitude: 11.9404,
              longitude: 108.4583,
            },
          },
        }),
      );
      return;
    }

    const routeStopsMatch = url.pathname.match(
      /^\/internal\/v1\/trips\/([0-9a-f-]+)\/route-stops$/i,
    );
    if (routeStopsMatch) {
      response.end(JSON.stringify({ success: true, data: { stops: [] } }));
      return;
    }

    response.statusCode = 404;
    response.end(JSON.stringify({ errorCode: 'NOT_FOUND' }));
  });
  fakeServer.listen(ports.fake, '0.0.0.0');
  await once(fakeServer, 'listening');
}

async function signIdentityToken(userId, role) {
  return new SignJWT({
    role,
    ...(role === 'DRIVER' ? { operatorId: ids.operator } : {}),
  })
    .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: jwk.kid })
    .setSubject(userId)
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setIssuedAt()
    .setExpirationTime('20m')
    .sign(signingKey);
}

async function waitForTracking() {
  await poll(
    async () => {
      try {
        return (await fetch(`${trackingUrl}/ready`)).ok;
      } catch {
        return false;
      }
    },
    'Tracking did not become ready with PostgreSQL, Redis, RabbitMQ and fake HTTP boundaries',
    90_000,
  );
}

async function ownerRequest(method, tripId, identityToken, idempotencyKey) {
  const response = await fetch(`${trackingUrl}/v1/tracking/trips/${tripId}/share-link`, {
    method,
    headers: {
      Authorization: `Bearer ${identityToken}`,
      'Idempotency-Key': idempotencyKey,
    },
  });
  return { response, body: await response.json() };
}

function extractShareToken(body) {
  assert(
    body?.success === true && body?.statusCode === 200,
    'Owner share-link response was not successful',
  );
  assert(typeof body.data?.shareUrl === 'string', 'Owner share-link response omitted shareUrl');
  const url = new URL(body.data.shareUrl);
  const token = new URLSearchParams(url.hash.slice(1)).get('token');
  assert(
    typeof token === 'string' && token.startsWith('v1.'),
    'Owner share-link fragment token was malformed',
  );
  rawTokens.add(token);
  tokenHashes.add(createHash('sha256').update(token, 'utf8').digest('hex'));
  const grantId = token.split('.')[1];
  if (grantId) grantIds.add(grantId);
  return { token, expiresAt: body.data.expiresAt, shareUrl: body.data.shareUrl };
}

async function contextRequest(token) {
  const response = await fetch(`${trackingUrl}/v1/tracking/shared-trip/context`, {
    headers: { 'X-Trip-Share-Token': token },
  });
  return { response, body: await response.json() };
}

function assertExactKeys(value, expected, path) {
  assert(
    value !== null && typeof value === 'object' && !Array.isArray(value),
    `${path} was not an object`,
  );
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  assert(
    actual.length === wanted.length && actual.every((key, index) => key === wanted[index]),
    `${path} did not match the public allow-list`,
  );
}

function assertPublicContext(result) {
  assert(result.response.status === 200, 'Guest context did not return HTTP 200');
  assert(
    result.response.headers.get('cache-control')?.includes('no-store'),
    'Guest context omitted no-store',
  );
  assert(
    result.response.headers.get('pragma')?.toLowerCase() === 'no-cache',
    'Guest context omitted no-cache',
  );
  assert(
    result.response.headers.get('referrer-policy') === 'no-referrer',
    'Guest context omitted no-referrer',
  );

  assertExactKeys(result.body, ['success', 'statusCode', 'data', 'meta'], 'envelope');
  assert(
    result.body.success === true && result.body.statusCode === 200,
    'Guest context envelope was invalid',
  );
  assertExactKeys(result.body.meta, ['traceId', 'timestamp'], 'meta');
  assertExactKeys(
    result.body.data,
    ['status', 'expiresAt', 'lastUpdatedAt', 'vehicle', 'route', 'eta'],
    'data',
  );
  assert(result.body.data.status === 'IN_PROGRESS', 'Guest context exposed an unexpected status');
  assert(
    result.body.data.lastUpdatedAt === null,
    'Missing GPS/ETA did not produce null lastUpdatedAt',
  );
  assert(result.body.data.eta === null, 'Missing ETA did not produce null');
  assertExactKeys(result.body.data.vehicle, ['location'], 'vehicle');
  assert(result.body.data.vehicle.location === null, 'Missing GPS did not produce null');
  assertExactKeys(
    result.body.data.route,
    ['originName', 'destinationName', 'origin', 'destination', 'stops', 'geometry'],
    'route',
  );
  assertExactKeys(result.body.data.route.origin, ['latitude', 'longitude'], 'route.origin');
  assertExactKeys(
    result.body.data.route.destination,
    ['latitude', 'longitude'],
    'route.destination',
  );
  assert(
    result.body.data.route.originName === 'Bến xe Miền Đông',
    'Guest origin name was incorrect',
  );
  assert(
    result.body.data.route.destinationName === 'Bến xe Đà Lạt',
    'Guest destination name was incorrect',
  );
  assert(
    Array.isArray(result.body.data.route.stops) && result.body.data.route.stops.length === 1,
    'Guest route stops were missing',
  );
  assertExactKeys(
    result.body.data.route.stops[0],
    ['name', 'latitude', 'longitude', 'sequence'],
    'route.stops[0]',
  );
  assert(
    result.body.data.route.stops[0].name === 'Trạm dừng Bảo Lộc'
      && result.body.data.route.stops[0].sequence === 1,
    'Guest route stop was incorrect',
  );
  assertExactKeys(result.body.data.route.geometry, ['type', 'coordinates'], 'route.geometry');
  assert(
    result.body.data.route.geometry.type === 'LineString',
    'Guest geometry was not a LineString',
  );
  const coordinates = result.body.data.route.geometry.coordinates;
  assert(
    Array.isArray(coordinates) && coordinates.length === 2,
    'Guest geometry coordinates were invalid',
  );
  assert(
    coordinates.every(
      (pair) => Array.isArray(pair) && pair.length === 2 && pair.every(Number.isFinite),
    ),
    'Guest geometry coordinate pair was invalid',
  );

  const serialized = JSON.stringify(result.body);
  const forbiddenValues = [
    ids.trip,
    ids.replacementTrip,
    ids.replacementVehicle,
    ids.ownerA,
    ids.ownerB,
    ids.outsider,
    ids.driver,
    ids.operator,
    ids.originStation,
    ids.destinationStation,
    ids.routeStop,
    ...rawTokens,
  ];
  for (const forbidden of forbiddenValues) {
    assert(!serialized.includes(forbidden), 'Guest context leaked a private identifier or token');
  }
  const forbiddenNames = [
    'tripId',
    'grantId',
    'tokenHash',
    'shareToken',
    'stationId',
    'stopId',
    'bookingId',
    'ticketId',
    'userId',
    'operatorId',
    'seat',
    'email',
    'phone',
    'driver',
    'assistant',
    'history',
  ];
  for (const name of forbiddenNames) {
    assert(
      !serialized.toLowerCase().includes(name.toLowerCase()),
      'Guest context leaked a forbidden field',
    );
  }
}

async function connectSocket(url, auth) {
  const socket = io(url, {
    path: '/tracking/socket.io',
    auth,
    transports: ['websocket'],
    forceNew: true,
    reconnection: false,
    timeout: 8_000,
  });
  sockets.add(socket);
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      socket.close();
      reject(new Error('Socket connection timed out'));
    }, 8_000);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve();
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      socket.close();
      reject(new Error(`Socket connection failed: ${error.message}`));
    });
  });
  return socket;
}

function connectShared(token) {
  return connectSocket(`${trackingUrl}/shared`, { shareToken: token });
}

function waitForSocketEvent(socket, event, timeoutMs = 8_000) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} timed out`)), timeoutMs);
    socket.once(event, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

function waitForDisconnect(socket, timeoutMs = 8_000) {
  if (!socket.connected) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Socket disconnect timed out')), timeoutMs);
    socket.once('disconnect', () => {
      clearTimeout(timeout);
      resolve();
    });
  });
}

function emitAck(socket, event, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(
      () => reject(new Error(`${event} acknowledgement timed out`)),
      8_000,
    );
    socket.emit(event, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

async function sendDriverGps(driverSocket, sequence, tripId = ids.trip) {
  const recordedAt = new Date(Date.now() + sequence * 10).toISOString();
  recordedAtValues.push(recordedAt);
  const ack = await emitAck(driverSocket, 'gps:update', {
    tripId,
    latitude: 10.7812 + sequence * 0.0001,
    longitude: 106.6981 + sequence * 0.0001,
    headingDeg: 42,
    speedKmh: 38,
    recordedAt,
  });
  assert(ack?.success === true, 'Driver gps:update was not accepted');
}

function assertSharedGps(payload) {
  assertExactKeys(payload, ['location'], 'shared:gps:update');
  assertExactKeys(
    payload.location,
    ['latitude', 'longitude', 'heading', 'speedKph', 'recordedAt'],
    'shared:gps:update.location',
  );
  assert(Number.isFinite(payload.location.latitude), 'Shared GPS latitude was invalid');
  assert(Number.isFinite(payload.location.longitude), 'Shared GPS longitude was invalid');
  assert(
    payload.location.heading === 42 && payload.location.speedKph === 38,
    'Shared GPS optional fields were not sanitized correctly',
  );
  const serialized = JSON.stringify(payload);
  assert(
    !serialized.includes(ids.trip)
      && !serialized.includes(ids.replacementTrip)
      && !/tripId|grantId|userId|driver/i.test(serialized),
    'Shared GPS leaked an internal identifier',
  );
}

async function verifyMigrationAndIndex() {
  const failedMigrations = Number(
    sql(
      'SELECT count(*) FROM public._prisma_migrations WHERE finished_at IS NULL OR rolled_back_at IS NOT NULL',
    ),
  );
  assert(failedMigrations === 0, 'Fresh Prisma migrate deploy left a failed migration');
  assert(
    Number(
      sql(
        "SELECT count(*) FROM public._prisma_migrations WHERE migration_name='20260803000000_add_trip_share_grants' AND finished_at IS NOT NULL",
      ),
    ) === 1,
    'Trip sharing migration was not applied exactly once',
  );
  const predicate = sql(
    "SELECT pg_get_expr(indpred, indrelid) FROM pg_index WHERE indexrelid='vietride_tracking.uq_trip_share_grants_active_owner_trip'::regclass",
  );
  assert(
    /revoked_at IS NULL/i.test(predicate),
    'Active owner/trip partial unique index predicate was not present',
  );
  console.log('PASS | fresh Prisma migration + real partial unique index predicate');
}

async function verifyOwnerConcurrencyAndPrivacy(tokens) {
  const concurrentKeys = [safeUuidV4(), safeUuidV4()];
  const [firstResponse, secondResponse] = await Promise.all(
    concurrentKeys.map((key) => ownerRequest('PUT', ids.trip, tokens.ownerA, key)),
  );
  assert(
    firstResponse.response.status === 200 && secondResponse.response.status === 200,
    `Concurrent owner PUT did not return two successful responses (${[firstResponse, secondResponse]
      .map(
        ({ response, body }) =>
          `${response.status}:${body?.errorCode ?? body?.error?.code ?? 'NO_ERROR_CODE'}`,
      )
      .join(', ')})`,
  );
  const first = extractShareToken(firstResponse.body);
  const second = extractShareToken(secondResponse.body);
  assert(first.shareUrl === second.shareUrl, 'Concurrent owner PUT did not return one stable link');
  assert(
    Number(
      sql(
        `SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND created_by_user_id='${ids.ownerA}' AND revoked_at IS NULL`,
      ),
    ) === 1,
    'Concurrent owner PUT created more than one active grant',
  );

  const stableResponse = await ownerRequest('PUT', ids.trip, tokens.ownerA, safeUuidV4());
  const stable = extractShareToken(stableResponse.body);
  assert(stable.shareUrl === first.shareUrl, 'New-key owner PUT did not reuse the active grant');

  const ownerBResponse = await ownerRequest('PUT', ids.trip, tokens.ownerB, safeUuidV4());
  assert(
    ownerBResponse.response.status === 200,
    'Second owner could not create an independent grant',
  );
  const ownerB = extractShareToken(ownerBResponse.body);
  assert(ownerB.token !== first.token, 'Two owners on one Trip received the same capability token');
  assert(
    Number(
      sql(
        `SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND revoked_at IS NULL`,
      ),
    ) === 2,
    'Two owners on one Trip did not have independent active grants',
  );

  const outsider = await ownerRequest('PUT', ids.trip, tokens.outsider, safeUuidV4());
  assert(
    outsider.response.status === 403 && outsider.body?.error?.code === 'ACCESS_DENIED',
    'Non-owner was not rejected by the real owner endpoint',
  );

  assertPublicContext(await contextRequest(first.token));
  console.log(
    'PASS | real concurrent PUT + stable link + owner isolation + recursive REST privacy',
  );
  return { ownerA: first, ownerB };
}

async function verifyRealtimeAndExpiry(tokens, shares) {
  const guestA = await connectShared(shares.ownerA.token);
  const guestB = await connectShared(shares.ownerB.token);
  const driver = await connectSocket(trackingUrl, { token: tokens.driver });

  const firstA = waitForSocketEvent(guestA, 'shared:gps:update');
  const firstB = waitForSocketEvent(guestB, 'shared:gps:update');
  await sendDriverGps(driver, 1);
  assertSharedGps(await firstA);
  assertSharedGps(await firstB);

  const revokedA = waitForSocketEvent(guestA, 'shared:access:revoked');
  const disconnectedA = waitForDisconnect(guestA);
  const deleteResult = await ownerRequest('DELETE', ids.trip, tokens.ownerA, safeUuidV4());
  assert(
    deleteResult.response.status === 200 && deleteResult.body?.data?.revoked === true,
    'Owner A DELETE did not revoke its active grant',
  );
  assert((await revokedA)?.reason === 'REVOKED', 'Owner A socket did not receive REVOKED');
  await disconnectedA;
  assert(guestB.connected, 'Revoking owner A disconnected owner B');

  const laterB = waitForSocketEvent(guestB, 'shared:gps:update');
  await sendDriverGps(driver, 2);
  assertSharedGps(await laterB);
  assert(guestB.connected, 'Owner B did not remain connected after owner A revoke');

  const replacementResult = await ownerRequest('PUT', ids.trip, tokens.ownerA, safeUuidV4());
  assert(replacementResult.response.status === 200, 'Owner A could not create a replacement grant');
  const replacement = extractShareToken(replacementResult.body);
  assert(replacement.token !== shares.ownerA.token, 'Owner A replacement reused a revoked token');
  const expiring = await connectShared(replacement.token);
  const expiryEvent = waitForSocketEvent(expiring, 'shared:access:revoked', 25_000);
  const expiryDisconnect = waitForDisconnect(expiring, 25_000);
  const expiryWaitMs = Math.max(0, Date.parse(replacement.expiresAt) - Date.now()) + 5_000;
  const expiry = await Promise.race([
    expiryEvent,
    delay(expiryWaitMs).then(() => {
      throw new Error('Active shared socket did not expire at grant TTL');
    }),
  ]);
  assert(expiry?.reason === 'EXPIRED', 'Active shared socket did not receive EXPIRED');
  await expiryDisconnect;
  const expiredRest = await contextRequest(replacement.token);
  assert(
    expiredRest.response.status === 410 &&
      expiredRest.body?.error?.code === 'TRACKING_SHARE_LINK_UNAVAILABLE',
    'Expired capability did not return the public 410 contract',
  );

  guestB.close();
  driver.close();
  console.log('PASS | shared GPS + grant-room revoke isolation + active-socket expiry + REST 410');
}

async function rabbitManagement(path) {
  const authorization = Buffer.from('vietride:vietride_dev').toString('base64');
  const response = await fetch(`http://127.0.0.1:${ports.rabbitManagement}${path}`, {
    headers: { Authorization: `Basic ${authorization}` },
  });
  assert(response.ok, 'RabbitMQ management API was unavailable');
  return response.json();
}

async function verifyRabbitTopology() {
  const bindings = [
    ['tracking-trip-share-completed', 'trip.trip.completed'],
    ['tracking-trip-share-cancelled', 'trip.trip.cancelled'],
    ['tracking-trip-share-disrupted', 'trip.trip.disrupted'],
    ['tracking-trip-share-vehicle-substituted', 'trip.trip.vehicle_substituted'],
  ];
  for (const [queue, routingKey] of bindings) {
    const encodedQueue = encodeURIComponent(queue);
    const queueDetails = await rabbitManagement(`/api/queues/%2F/${encodedQueue}`);
    assert(
      queueDetails.arguments?.['x-dead-letter-exchange'] === 'vietride.events.retry',
      `Queue ${queue} did not use the production retry exchange`,
    );
    assert(
      queueDetails.arguments?.['x-dead-letter-routing-key'] === routingKey,
      `Queue ${queue} did not dead-letter with its exact routing key`,
    );
    const sourceBindings = await rabbitManagement(
      `/api/bindings/%2F/e/${encodeURIComponent('vietride.events')}/q/${encodedQueue}`,
    );
    const routingKeys = sourceBindings.map((binding) => binding.routing_key).sort();
    assert(routingKeys.includes(routingKey), `Queue ${queue} was not bound to ${routingKey}`);
    assert(
      routingKeys.includes(`__retry__.${queue}`),
      `Queue ${queue} omitted its retry-return binding`,
    );
    assert(routingKeys.length === 2, `Queue ${queue} had an unexpected primary-exchange binding`);

    const retry = await rabbitManagement(`/api/queues/%2F/${encodeURIComponent(`${queue}.retry`)}`);
    assert(
      retry.arguments?.['x-message-ttl'] === 10_000,
      `Queue ${queue}.retry did not use 10 second TTL`,
    );
    assert(
      retry.arguments?.['x-dead-letter-exchange'] === 'vietride.events',
      `Queue ${queue}.retry did not return to the primary exchange`,
    );
    await rabbitManagement(`/api/queues/%2F/${encodeURIComponent(`${queue}.dlq`)}`);
  }
  console.log('PASS | four exact share-lifecycle bindings + production retry/DLQ topology');
}

function terminalPayload(eventId, kind = 'completed') {
  const now = new Date().toISOString();
  const base = {
    eventId,
    occurredAt: now,
    tripId: ids.trip,
    operatorId: ids.operator,
  };
  if (kind === 'cancelled')
    return { ...base, cancelledAt: now, cancelReason: 'E2E_LOCK_CONTENTION' };
  return { ...base, terminalAt: now, hasSubstitution: false };
}

async function publishTerminal(routingKey, payload) {
  rabbitChannel.publish('vietride.events', routingKey, Buffer.from(JSON.stringify(payload)), {
    contentType: 'application/json',
    persistent: true,
    messageId: payload.eventId,
    correlationId: payload.eventId,
  });
  await rabbitChannel.waitForConfirms();
}

function vehicleSubstitutedPayload(eventId) {
  const occurredAt = new Date().toISOString();
  return {
    eventId,
    occurredAt,
    substitutionId: eventId,
    disruptedAt: occurredAt,
    operatorId: ids.operator,
    oldTripId: ids.trip,
    oldTripStatus: 'DISRUPTED',
    oldVehicleId: randomUUID(),
    newTripId: ids.replacementTrip,
    newTripStatus: 'BOARDING',
    newVehicleId: ids.replacementVehicle,
    newVehiclePlateNumber: 'E2E-REDACTED',
    newTripDepartureDateTime: new Date(Date.now() + 30 * 60_000).toISOString(),
    actorUserId: ids.driver,
    reason: 'E2E vehicle substitution',
    notifyPassengers: true,
    mappings: [],
  };
}

async function createFreshShares(tokens) {
  const ownerAResult = await ownerRequest('PUT', ids.trip, tokens.ownerA, safeUuidV4());
  const ownerBResult = await ownerRequest('PUT', ids.trip, tokens.ownerB, safeUuidV4());
  assert(
    ownerAResult.response.status === 200 && ownerBResult.response.status === 200,
    'Could not create fresh grants for terminal-event coverage',
  );
  return {
    ownerA: extractShareToken(ownerAResult.body),
    ownerB: extractShareToken(ownerBResult.body),
  };
}

async function verifyTerminalAndDuplicate(tokens) {
  tripStatus = 'IN_PROGRESS';
  const fresh = await createFreshShares(tokens);
  const guestA = await connectShared(fresh.ownerA.token);
  const guestB = await connectShared(fresh.ownerB.token);
  const revokedA = waitForSocketEvent(guestA, 'shared:access:revoked');
  const revokedB = waitForSocketEvent(guestB, 'shared:access:revoked');
  const disconnectedA = waitForDisconnect(guestA);
  const disconnectedB = waitForDisconnect(guestB);
  const eventId = randomUUID();
  eventIds.add(eventId);
  const payload = terminalPayload(eventId);
  await publishTerminal('trip.trip.completed', payload);

  assert((await revokedA)?.reason === 'TRIP_ENDED', 'Terminal event did not revoke owner A socket');
  assert((await revokedB)?.reason === 'TRIP_ENDED', 'Terminal event did not revoke owner B socket');
  await Promise.all([disconnectedA, disconnectedB]);
  await poll(
    () =>
      Number(
        sql(
          `SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND revoked_at IS NULL`,
        ),
      ) === 0,
    'Terminal event did not revoke every active grant',
  );
  assert(
    Number(
      sql(
        `SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.trip}' AND revoke_reason='TRIP_TERMINATED'`,
      ),
    ) >= 2,
    'Terminal event did not persist TRIP_TERMINATED',
  );
  const processedKey = `tracking:trip-share:event:processed:${eventId}`;
  await poll(() => redisClient.get(processedKey), 'Terminal event was not marked processed');
  const beforeDuplicate = sql(
    `SELECT string_agg(id::text || ':' || updated_at::text, ',' ORDER BY id) FROM trip_share_grants WHERE trip_id='${ids.trip}'`,
  );
  await publishTerminal('trip.trip.completed', payload);
  await delay(800);
  const afterDuplicate = sql(
    `SELECT string_agg(id::text || ':' || updated_at::text, ',' ORDER BY id) FROM trip_share_grants WHERE trip_id='${ids.trip}'`,
  );
  assert(
    beforeDuplicate === afterDuplicate,
    'Duplicate terminal delivery mutated grants after processed marker',
  );
  tripStatus = 'COMPLETED';
  console.log('PASS | canonical terminal event revoke/disconnect + duplicate idempotency');
}

async function verifyVehicleSubstitutionKeepsToken(tokens) {
  tripStatus = 'IN_PROGRESS';
  replacementTripStatus = 'BOARDING';
  const fresh = await createFreshShares(tokens);
  const guestA = await connectShared(fresh.ownerA.token);
  const guestB = await connectShared(fresh.ownerB.token);
  const substitutedA = waitForSocketEvent(guestA, 'shared:trip:vehicleSubstituted');
  const substitutedB = waitForSocketEvent(guestB, 'shared:trip:vehicleSubstituted');

  const disruptedEventId = randomUUID();
  eventIds.add(disruptedEventId);
  tripStatus = 'DISRUPTED';
  await publishTerminal('trip.trip.disrupted', {
    ...terminalPayload(disruptedEventId),
    hasSubstitution: true,
  });
  const substitutionEventId = randomUUID();
  eventIds.add(substitutionEventId);
  await publishTerminal(
    'trip.trip.vehicle_substituted',
    vehicleSubstitutedPayload(substitutionEventId),
  );

  for (const payload of [await substitutedA, await substitutedB]) {
    assertExactKeys(payload, ['status', 'occurredAt'], 'shared:trip:vehicleSubstituted');
    assert(
      payload.status === 'VEHICLE_REPLACEMENT_PENDING',
      'Vehicle-substituted event exposed an unexpected status',
    );
    const serialized = JSON.stringify(payload);
    assert(
      !serialized.includes(ids.trip)
        && !serialized.includes(ids.replacementTrip)
        && !serialized.includes(ids.replacementVehicle)
        && !/tripId|vehicleId|plate|userId|operatorId/i.test(serialized),
      'Vehicle-substituted event leaked an internal identifier or vehicle data',
    );
  }
  assert(guestA.connected && guestB.connected, 'Vehicle substitution disconnected a viewer');
  await poll(
    () =>
      Number(
        sql(
          `SELECT count(*) FROM trip_share_grants WHERE trip_id='${ids.replacementTrip}' AND revoked_at IS NULL`,
        ),
      ) === 2,
    'Vehicle substitution did not transfer both active grants',
  );

  const pending = await contextRequest(fresh.ownerA.token);
  assert(pending.response.status === 200, 'Original token failed during vehicle replacement');
  assert(
    pending.body?.data?.status === 'VEHICLE_REPLACEMENT_PENDING',
    'Context did not expose replacement-pending status',
  );
  assert(pending.body?.data?.vehicle?.location, 'Pending context omitted the previous GPS marker');
  assert(pending.body?.data?.eta === null, 'Pending context did not force ETA to null');

  replacementTripStatus = 'IN_PROGRESS';
  const driver = await connectSocket(trackingUrl, { token: tokens.driver });
  const replacementGpsA = waitForSocketEvent(guestA, 'shared:gps:update');
  const replacementGpsB = waitForSocketEvent(guestB, 'shared:gps:update');
  await sendDriverGps(driver, 20, ids.replacementTrip);
  assertSharedGps(await replacementGpsA);
  assertSharedGps(await replacementGpsB);
  const active = await contextRequest(fresh.ownerA.token);
  assert(active.body?.data?.status === 'IN_PROGRESS', 'Context did not return to IN_PROGRESS');

  const revokedA = waitForSocketEvent(guestA, 'shared:access:revoked');
  const disconnectedA = waitForDisconnect(guestA);
  const revokeViaOldTrip = await ownerRequest(
    'DELETE',
    ids.trip,
    tokens.ownerA,
    safeUuidV4(),
  );
  assert(
    revokeViaOldTrip.response.status === 200 && revokeViaOldTrip.body?.data?.revoked === true,
    'DELETE through the old Trip alias did not revoke the transferred grant',
  );
  assert((await revokedA)?.reason === 'REVOKED', 'Alias DELETE emitted the wrong revoke reason');
  await disconnectedA;
  assert(guestB.connected, 'Alias DELETE disconnected the independent owner viewer');

  guestB.close();
  driver.close();
  console.log('PASS | same share tokens + pending marker + room transfer + replacement GPS');
}

async function verifyRetryAndDlq() {
  const eventId = randomUUID();
  eventIds.add(eventId);
  const processingKey = `tracking:trip-share:event:processing:${eventId}`;
  await redisClient.set(processingKey, `e2e-lock-${runUuid}`, 'EX', 120, 'NX');
  await publishTerminal('trip.trip.cancelled', terminalPayload(eventId, 'cancelled'));

  const dlqName = 'tracking-trip-share-cancelled.dlq';
  await poll(
    async () => {
      const queue = await rabbitManagement(`/api/queues/%2F/${encodeURIComponent(dlqName)}`);
      return queue.messages_ready === 1;
    },
    'Lock-contention terminal event did not reach DLQ after five real retries',
    80_000,
    500,
  );

  const dlqMessage = await rabbitChannel.get(dlqName, { noAck: false });
  assert(dlqMessage, 'RabbitMQ reported a DLQ message but basic.get returned none');
  assert(
    dlqMessage.properties.headers?.['x-vietride-dlq-reason'] === 'max-retries-exceeded',
    'DLQ message omitted max-retries reason',
  );
  assert(
    Number(dlqMessage.properties.headers?.['x-vietride-retry-count']) === 5,
    'DLQ message did not record exactly five rejected retries',
  );
  const parkedPayload = JSON.parse(dlqMessage.content.toString('utf8'));
  assert(parkedPayload.eventId === eventId, 'DLQ changed the canonical event identity');
  rabbitChannel.ack(dlqMessage);
  assert(
    (await redisClient.get(`tracking:trip-share:event:processed:${eventId}`)) === null,
    'Lock-contention event was falsely marked processed',
  );
  assert(
    (await redisClient.get(processingKey)) !== null,
    'Deterministic processing lock expired before retry/DLQ proof completed',
  );
  console.log('PASS | real Redis lock contention + 5 delayed Rabbit retries + DLQ park');
}

async function scanRedisKeys() {
  const keys = [];
  let cursor = '0';
  do {
    const [next, page] = await redisClient.scan(cursor, 'COUNT', 200);
    cursor = next;
    keys.push(...page);
  } while (cursor !== '0');
  return keys;
}

async function redisDumpText(key) {
  const dumped = await redisClient.dump(key);
  return dumped ? dumped.toString('utf8') : '';
}

function ownedMarkers() {
  const operationHashes = [...idempotencyKeys].map((key) =>
    createHash('sha256').update(key.toLowerCase(), 'utf8').digest('hex'),
  );
  return [
    runUuid,
    ...Object.values(ids),
    ...eventIds,
    ...tokenHashes,
    ...grantIds,
    ...operationHashes,
  ];
}

async function verifyNoTokenLeakage() {
  const databaseDump = run('docker', [
    'exec',
    containers.postgres,
    'pg_dump',
    '-U',
    'vietride',
    '-d',
    'vietride_tracking',
    '--data-only',
  ]);
  const keys = await scanRedisKeys();
  const redisDumps = await Promise.all(keys.map((key) => redisDumpText(key)));
  const trackingLogs = run('docker', ['logs', '--timestamps', containers.tracking]);
  for (const token of rawTokens) {
    assert(!databaseDump.includes(token), 'Raw capability token persisted in PostgreSQL');
    assert(
      !keys.some((key) => key.includes(token)),
      'Raw capability token persisted in a Redis key',
    );
    assert(
      !redisDumps.some((value) => value.includes(token)),
      'Raw capability token persisted in Redis',
    );
    assert(!trackingLogs.includes(token), 'Raw capability token appeared in Tracking logs');
  }
  console.log('PASS | raw capability tokens absent from PostgreSQL, Redis and Tracking logs');
}

async function cleanupFixtures() {
  if (redisClient?.status === 'ready') {
    await redisClient.srem('tracking:active_trips', ids.trip, ids.replacementTrip);
    const markers = ownedMarkers();
    const keys = await scanRedisKeys();
    const ownedKeys = [];
    for (const key of keys) {
      const dump = await redisDumpText(key);
      if (markers.some((marker) => key.includes(marker) || dump.includes(marker)))
        ownedKeys.push(key);
    }
    if (ownedKeys.length > 0) await redisClient.del(...ownedKeys);
    const remainingKeys = await scanRedisKeys();
    for (const key of remainingKeys) {
      const dump = await redisDumpText(key);
      assert(
        !markers.some((marker) => key.includes(marker) || dump.includes(marker)),
        'Run-owned Redis fixture remained after exact-key cleanup',
      );
    }
  }

  if (infraStarted) {
    sql(`DELETE FROM gps_trails WHERE trip_id IN ('${ids.trip}','${ids.replacementTrip}')`);
    sql(`DELETE FROM trip_share_grants WHERE trip_id IN ('${ids.trip}','${ids.replacementTrip}')`);
    assert(
      Number(
        sql(
          `SELECT count(*) FROM trip_share_grants WHERE trip_id IN ('${ids.trip}','${ids.replacementTrip}')`,
        ),
      ) === 0,
      'Run-owned trip_share_grants remained after exact-row cleanup',
    );
    assert(
      Number(
        sql(
          `SELECT count(*) FROM gps_trails WHERE trip_id IN ('${ids.trip}','${ids.replacementTrip}')`,
        ),
      ) === 0,
      'Run-owned gps_trails remained after exact-row cleanup',
    );
  }
}

async function closeResources() {
  for (const socket of sockets) socket.close();
  if (rabbitChannel) await rabbitChannel.close().catch(() => undefined);
  if (rabbitConnection) await rabbitConnection.close().catch(() => undefined);
  if (redisClient) await redisClient.quit().catch(() => redisClient.disconnect());
  if (fakeServer) {
    await new Promise((resolve) => fakeServer.close(resolve));
  }
}

async function cleanup() {
  try {
    await cleanupFixtures();
    if (infraStarted) console.log('PASS | exact run-owned row/key cleanup assertions');
  } catch (error) {
    console.error(`WARN | fixture cleanup failed: ${redact(error.stack || error.message)}`);
    process.exitCode = 1;
  }
  await closeResources();
  if (composeAttempted) {
    try {
      composeRun(['down', '-v', '--remove-orphans']);
      console.log('PASS | isolated compose teardown');
    } catch (error) {
      console.error(`WARN | compose teardown failed: ${redact(error.message)}`);
      process.exitCode = 1;
    }
  }
}

async function main() {
  try {
    try {
      run('docker', ['version', '--format', '{{.Server.Version}}']);
    } catch (error) {
      throw new Error(`INFRASTRUCTURE_BLOCKED: Docker daemon is unavailable: ${error.message}`);
    }

    await startFakeBoundaries();
    composeAttempted = true;
    composeRun(['up', '-d', '--wait', 'postgres', 'redis', 'rabbitmq']);
    infraStarted = true;
    run(
      process.execPath,
      [
        'node_modules/prisma/build/index.js',
        'migrate',
        'deploy',
        '--schema=apps/tracking/prisma/schema.prisma',
      ],
      { env: { TRACKING_DATABASE_URL: databaseUrl, DATABASE_URL: databaseUrl } },
    );
    await verifyMigrationAndIndex();

    if (!noBuild) composeRun(['build', 'tracking']);
    composeRun(['up', '-d', '--wait', '--no-build', 'tracking']);
    await waitForTracking();

    redisClient = new Redis({
      host: '127.0.0.1',
      port: ports.redis,
      lazyConnect: true,
      maxRetriesPerRequest: 1,
    });
    await redisClient.connect();
    rabbitConnection = await amqp.connect({
      hostname: '127.0.0.1',
      port: ports.rabbitmq,
      username: 'vietride',
      password: 'vietride_dev',
    });
    rabbitChannel = await rabbitConnection.createConfirmChannel();
    await rabbitChannel.assertExchange('vietride.events', 'topic', { durable: true });
    await verifyRabbitTopology();

    const tokens = {
      ownerA: await signIdentityToken(ids.ownerA, 'PASSENGER'),
      ownerB: await signIdentityToken(ids.ownerB, 'PASSENGER'),
      outsider: await signIdentityToken(ids.outsider, 'PASSENGER'),
      driver: await signIdentityToken(ids.driver, 'DRIVER'),
    };
    const shares = await verifyOwnerConcurrencyAndPrivacy(tokens);
    await verifyRealtimeAndExpiry(tokens, shares);
    await verifyTerminalAndDuplicate(tokens);
    await verifyVehicleSubstitutionKeepsToken(tokens);
    await verifyRetryAndDlq();
    await verifyNoTokenLeakage();
    console.log('PASS | Tracking Phase 13 sharing isolated real-infrastructure E2E');
  } catch (error) {
    console.error(`FAIL | ${redact(error.stack || error.message)}`);
    if (infraStarted) {
      try {
        const logs = composeRun(['logs', '--no-color', '--tail', '250', 'tracking']);
        console.error(`--- redacted tracking logs ---\n${redact(logs)}`);
      } catch {
        // Tracking may not have been created yet.
      }
    }
    process.exitCode = 1;
  } finally {
    await cleanup();
  }
}

await main();
