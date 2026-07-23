import { spawnSync } from 'node:child_process';
import { once } from 'node:events';
import http from 'node:http';
import { generateKeyPair, exportSPKI, SignJWT } from 'jose';
import { io } from 'socket.io-client';

const root = process.cwd();
const noBuild = process.argv.includes('--no-build');
const trackingUrl = 'http://127.0.0.1:56121';
const downstreamUrl = 'http://127.0.0.1:56131';
const postgresContainer = 'tracking-idempotency-e2e-postgres';
const redisContainer = 'tracking-idempotency-e2e-redis';
const databaseUrl = 'postgresql://vietride:vietride_dev@127.0.0.1:55461/vietride_tracking';
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.tracking-idempotency-e2e.yml',
  '--profile',
  'infra',
];
const ids = {
  trip: '46100000-0000-4000-8000-000000000001',
  shuttle: '46100000-0000-4000-8000-000000000002',
  stop: '46100000-0000-4000-8000-000000000003',
  driver: '46100000-0000-4000-8000-000000000004',
  observer: '46100000-0000-4000-8000-000000000005',
  operator: '46100000-0000-4000-8000-000000000006',
};
const tripRecordedAt = '2026-07-23T01:00:00.000Z';
const shuttleRecordedAt = '2026-07-23T01:01:00.000Z';
const childLogs = [];
const downstreamCalls = [];
let downstreamServer;
const sockets = new Set();

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
  });
  if (result.status !== 0) {
    throw new Error(
      `${command} ${args.join(' ')} failed (${result.status ?? result.error?.code ?? result.signal}):\n${result.stderr || result.stdout || result.error?.message}`,
    );
  }
  return result.stdout.trim();
}

function composeRun(args, options = {}) {
  return run('docker', [...compose, ...args], {
    ...options,
    env: {
      POSTGRES_PORT: '55461',
      REDIS_PORT: '56391',
      RABBITMQ_PORT: '55691',
      RABBITMQ_MGMT_PORT: '55692',
      ...options.env,
    },
  });
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function sql(statement) {
  return run('docker', [
    'exec',
    postgresContainer,
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

function redis(...args) {
  return run('docker', ['exec', redisContainer, 'redis-cli', '--raw', ...args]);
}

async function poll(check, message, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await check();
    if (value) return value;
    await delay(100);
  }
  throw new Error(typeof message === 'function' ? message() : message);
}

async function waitForHealth() {
  await poll(
    async () => {
      try {
        const response = await fetch(`${trackingUrl}/health`);
        return response.ok;
      } catch {
        return false;
      }
    },
    'Tracking service did not become healthy',
    60_000,
  );
}

async function waitForTrackingDatabase() {
  await poll(
    () => {
      try {
        return (
          run('docker', [
            'exec',
            postgresContainer,
            'psql',
            '-v',
            'ON_ERROR_STOP=1',
            '-U',
            'vietride',
            '-d',
            'vietride_tracking',
            '-qAtc',
            'SELECT 1',
          ]) === '1'
        );
      } catch {
        return false;
      }
    },
    'Tracking database did not become ready',
    30_000,
  );
}

function startDownstreamServer() {
  downstreamServer = http.createServer((request, response) => {
    const url = new URL(request.url ?? '/', downstreamUrl);
    downstreamCalls.push(url.pathname);
    const role = url.searchParams.get('role');
    response.setHeader('Content-Type', 'application/json');

    if (/\/tracking-authorization(?:\/bookings|\/parcels)?$/.test(url.pathname)) {
      const scope =
        role === 'DRIVER'
          ? 'DRIVER'
          : role === 'ASSISTANT'
            ? 'ASSISTANT'
            : role?.startsWith('OPERATOR')
              ? 'OPERATOR'
              : 'BOOKING_OWNER';
      response.end(JSON.stringify({ allowed: true, scope }));
      return;
    }

    if (/\/route-stops$/.test(url.pathname)) {
      response.end(
        JSON.stringify({
          success: true,
          data: {
            data: {
              stops: [
                {
                  stopId: ids.stop,
                  latitude: 10.79,
                  longitude: 106.69,
                  sequence: 1,
                  estimatedArrivalTime: '2026-07-23T01:20:00.000Z',
                },
              ],
            },
          },
        }),
      );
      return;
    }

    if (/\/route-geometry$/.test(url.pathname)) {
      const tripId = url.pathname.split('/')[4] ?? ids.trip;
      response.end(
        JSON.stringify({
          success: true,
          data: {
            data: {
              tripId,
              points: [
                { latitude: 10.75, longitude: 106.65 },
                { latitude: 10.8, longitude: 106.7 },
              ],
            },
          },
        }),
      );
      return;
    }

    if (/\/pickup-bookings$/.test(url.pathname)) {
      response.end(JSON.stringify({ success: true, data: { data: { bookings: [] } } }));
      return;
    }

    if (/\/internal\/v1\/shuttle-trips\/.+\/tracking-context$/.test(url.pathname)) {
      const driver = role === 'DRIVER';
      response.end(
        JSON.stringify({
          shuttleTripId: ids.shuttle,
          mainTripId: ids.trip,
          operatorId: ids.operator,
          driverUserId: ids.driver,
          allowed: true,
          scope: driver ? 'DRIVER' : 'PASSENGER',
          stops: [
            {
              pickupOrder: 1,
              bookingId: null,
              latitude: 10.79,
              longitude: 106.69,
              status: 'PENDING',
              isStation: true,
            },
          ],
        }),
      );
      return;
    }

    response.statusCode = 404;
    response.end(JSON.stringify({ error: 'NOT_FOUND' }));
  });
  downstreamServer.listen(56131, '0.0.0.0');
  return once(downstreamServer, 'listening');
}

async function createToken(privateKey, userId, role) {
  return new SignJWT({
    role,
    email: `${role.toLowerCase()}@tracking-idempotency.test`,
    ...(role === 'PASSENGER' ? {} : { operatorId: ids.operator }),
  })
    .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'tracking-idempotency-e2e' })
    .setSubject(userId)
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(privateKey);
}

async function connect(token) {
  const socket = io(trackingUrl, {
    path: '/tracking/socket.io',
    auth: { token },
    transports: ['websocket'],
    forceNew: true,
    reconnection: false,
    timeout: 5_000,
  });
  sockets.add(socket);
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Socket connect timeout')), 5_000);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve();
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      reject(error);
    });
  });
  return socket;
}

function emitAck(socket, event, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} ack timeout`)), 5_000);
    socket.emit(event, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

async function verifyTripFlow(privateKey) {
  const driver = await connect(await createToken(privateKey, ids.driver, 'DRIVER'));
  const observer = await connect(await createToken(privateKey, ids.observer, 'OPERATOR_ADMIN'));
  assert(
    (await emitAck(driver, 'joinTripTracking', { tripId: ids.trip })).success,
    'Driver could not join main trip room',
  );
  assert(
    (await emitAck(observer, 'joinTripTracking', { tripId: ids.trip })).success,
    'Observer could not join main trip room',
  );

  const gpsEvents = [];
  const etaEvents = [];
  observer.on('gps:update', (event) => gpsEvents.push(event));
  observer.on('eta:update', (event) => etaEvents.push(event));
  const payload = {
    tripId: ids.trip,
    latitude: 10.76,
    longitude: 106.66,
    speedKmh: 30,
    headingDeg: 90,
    recordedAt: tripRecordedAt,
  };

  assert((await emitAck(driver, 'gps:update', payload)).success, 'First main GPS update failed');
  await poll(
    () =>
      gpsEvents.length === 1 &&
      downstreamCalls.filter((path) => path.endsWith('/route-geometry')).length === 1 &&
      downstreamCalls.filter((path) => path.endsWith('/route-stops')).length === 1,
    () =>
      `First main GPS/detection cycle missing (gps=${gpsEvents.length}, eta=${etaEvents.length}, downstream=${JSON.stringify(downstreamCalls)})`,
  );
  const etaEventsAfterFirst = etaEvents.length;
  const detectionCallsAfterFirst = downstreamCalls.filter(
    (path) => path.endsWith('/route-geometry') || path.endsWith('/route-stops'),
  ).length;
  assert(
    (await emitAck(driver, 'gps:update', payload)).success,
    'Same-payload main GPS replay was not accepted',
  );
  await delay(600);
  assert(gpsEvents.length === 1, `Duplicate main GPS broadcasted ${gpsEvents.length} times`);
  assert(
    etaEvents.length === etaEventsAfterFirst,
    `Duplicate main GPS reran ETA (${etaEventsAfterFirst} -> ${etaEvents.length})`,
  );
  assert(
    downstreamCalls.filter(
      (path) => path.endsWith('/route-geometry') || path.endsWith('/route-stops'),
    ).length === detectionCallsAfterFirst,
    'Duplicate main GPS reran the detection provider chain',
  );
  assert(
    Number(redis('LLEN', `tracking:gps_buffer:${ids.trip}`)) === 1,
    'Duplicate main GPS appended to Redis buffer',
  );

  const mismatch = await emitAck(driver, 'gps:update', { ...payload, latitude: 10.761 });
  assert(
    mismatch.success === false && mismatch.error === 'IDEMPOTENCY_KEY_REUSED',
    `Main GPS mismatch was not rejected: ${JSON.stringify(mismatch)}`,
  );
  assert(
    Number(redis('LLEN', `tracking:gps_buffer:${ids.trip}`)) === 1,
    'Main GPS mismatch changed Redis buffer',
  );
  assert(
    downstreamCalls.filter(
      (path) => path.endsWith('/route-geometry') || path.endsWith('/route-stops'),
    ).length === detectionCallsAfterFirst,
    'Main GPS payload mismatch reran the detection provider chain',
  );
  console.log('PASS | trip gps:update replay/mismatch/broadcast/detection Redis gate');
}

async function verifyShuttleFlow(privateKey) {
  const driver = await connect(await createToken(privateKey, ids.driver, 'DRIVER'));
  const observer = await connect(await createToken(privateKey, ids.observer, 'PASSENGER'));
  assert(
    (await emitAck(observer, 'joinShuttleTracking', { shuttleTripId: ids.shuttle })).success,
    'Observer could not join shuttle room',
  );

  const gpsEvents = [];
  const etaEvents = [];
  observer.on('shuttle:gps:update', (event) => gpsEvents.push(event));
  observer.on('shuttle:eta:update', (event) => etaEvents.push(event));
  const payload = {
    shuttleTripId: ids.shuttle,
    latitude: 10.76,
    longitude: 106.66,
    speedKmh: 30,
    heading: 90,
    recordedAt: shuttleRecordedAt,
  };

  assert(
    (await emitAck(driver, 'shuttle:gps:update', payload)).success,
    'First shuttle GPS update failed',
  );
  await poll(
    () => gpsEvents.length === 1 && etaEvents.length === 1,
    () =>
      `First shuttle GPS/ETA broadcast missing (gps=${gpsEvents.length}, eta=${etaEvents.length}, downstream=${JSON.stringify(downstreamCalls)})`,
  );
  assert(
    (await emitAck(driver, 'shuttle:gps:update', payload)).success,
    'Same-payload shuttle GPS replay was not accepted',
  );
  await delay(600);
  assert(gpsEvents.length === 1, `Duplicate shuttle GPS broadcasted ${gpsEvents.length} times`);
  assert(etaEvents.length === 1, `Duplicate shuttle GPS reran ETA ${etaEvents.length} times`);
  assert(
    Number(redis('LLEN', `tracking:shuttle:gps_buffer:${ids.shuttle}`)) === 1,
    'Duplicate shuttle GPS appended to Redis buffer',
  );

  const mismatch = await emitAck(driver, 'shuttle:gps:update', {
    ...payload,
    longitude: 106.661,
  });
  assert(
    mismatch.success === false && mismatch.error === 'IDEMPOTENCY_KEY_REUSED',
    `Shuttle GPS mismatch was not rejected: ${JSON.stringify(mismatch)}`,
  );
  assert(
    redis('SISMEMBER', 'tracking:active_trips', ids.shuttle).trim() === '0',
    'Shuttle leaked into main-trip batch set',
  );
  console.log('PASS | shuttle:gps:update replay/mismatch/broadcast/ETA Redis gate');
}

async function verifyPersistence() {
  await poll(
    () =>
      Number(
        sql(
          `SELECT count(*) FROM gps_trails WHERE trip_id='${ids.trip}' AND recorded_at='${tripRecordedAt}'`,
        ),
      ) === 1,
    'Main GPS batch did not flush to PostgreSQL exactly once',
    30_000,
  );
  await delay(5_500);
  assert(
    Number(
      sql(
        `SELECT count(*) FROM gps_trails WHERE trip_id='${ids.trip}' AND recorded_at='${tripRecordedAt}'`,
      ),
    ) === 1,
    'Main GPS duplicate created a second PostgreSQL row',
  );
  assert(
    Number(sql(`SELECT count(*) FROM gps_trails WHERE trip_id='${ids.shuttle}'`)) === 0,
    'Shuttle GPS leaked into the main-trip gps_trails table',
  );
  assert(
    Number(redis('LLEN', `tracking:shuttle:gps_buffer:${ids.shuttle}`)) === 1,
    'Main-trip batch worker consumed the isolated shuttle buffer',
  );
  console.log('PASS | real batch flush + PostgreSQL natural identity');
  console.log('PASS | shuttle remains Redis-only by Tracking Phase 11 contract');
}

async function cleanup() {
  for (const socket of sockets) socket.close();
  if (downstreamServer) {
    await new Promise((resolve) => downstreamServer.close(resolve));
  }
  try {
    composeRun(['down', '-v', '--remove-orphans']);
    console.log('PASS | isolated Tracking E2E cleanup');
  } catch (error) {
    console.error(`WARN | cleanup failed: ${error.message}`);
  }
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main() {
  try {
    composeRun(['up', '-d', '--wait', 'postgres', 'redis', 'rabbitmq']);
    await waitForTrackingDatabase();
    run(
      process.execPath,
      [
        'node_modules/prisma/build/index.js',
        'migrate',
        'deploy',
        '--schema=apps/tracking/prisma/schema.prisma',
      ],
      {
        env: { TRACKING_DATABASE_URL: databaseUrl, DATABASE_URL: databaseUrl },
      },
    );
    const { privateKey, publicKey } = await generateKeyPair('RS256');
    const publicKeyPem = await exportSPKI(publicKey);
    await startDownstreamServer();
    if (!noBuild) composeRun(['build', 'tracking']);
    composeRun(['up', '-d', '--wait', '--no-build', 'tracking'], {
      env: { TRACKING_E2E_PUBLIC_KEY: publicKeyPem },
    });
    await waitForHealth();

    await verifyTripFlow(privateKey);
    await verifyShuttleFlow(privateKey);
    await verifyPersistence();
    console.log('PASS | Tracking idempotency focused system E2E');
  } catch (error) {
    try {
      childLogs.push(composeRun(['logs', '--no-color', '--tail', '200', 'tracking']));
    } catch {
      // The service may not have been created yet.
    }
    console.error(`FAIL | ${error.stack || error.message}`);
    if (childLogs.length > 0) console.error(`--- tracking logs ---\n${childLogs.join('')}`);
    process.exitCode = 1;
  } finally {
    await cleanup();
  }
}

await main();
