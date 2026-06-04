const { io } = require('socket.io-client');

const DEFAULT_TRACKING_URL = 'http://localhost:3001';
const SOCKET_PATH = '/tracking/socket.io';
const CONNECT_TIMEOUT_MS = 5_000;
const ACK_TIMEOUT_MS = 5_000;
const EVENT_TIMEOUT_MS = 5_000;
const DEFAULT_LATITUDE = 10.762622;
const DEFAULT_LONGITUDE = 106.660172;
const DEFAULT_SPEED_KMH = 40;
const DEFAULT_HEADING_DEG = 90;

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || DEFAULT_TRACKING_URL;
const tripId = process.env.TRIP_ID;
const driverToken = process.env.DRIVER_TOKEN || process.env.ACCESS_TOKEN;
const passengerToken = process.env.PASSENGER_TOKEN;
const expectEtaUpdate = process.env.EXPECT_ETA_UPDATE === 'true';

async function main() {
  assertRequiredEnv();

  await runCase('auth fail rejects invalid token', async () => {
    await expectRejectsUnauthorized(connectSocket('invalid-token'));
  });

  await runCase('permission fail denies passenger gps:update', async () => {
    const socket = await connectSocket(passengerToken);
    try {
      const ack = await emitWithAck(socket, 'gps:update', createGpsPayload());
      assert(ack && ack.success === false && ack.error === 'ACCESS_DENIED', 'expected ACCESS_DENIED ack');
    } finally {
      socket.disconnect();
    }
  });

  await runCase('happy path accepts driver gps:update for approaching alert trigger flow', async () => {
    const socket = await connectSocket(driverToken);
    try {
      await emitWithAck(socket, 'joinTripTracking', { tripId });
      const etaPromise = expectEtaUpdate ? waitForEvent(socket, 'eta:update') : Promise.resolve(null);
      const ack = await emitWithAck(socket, 'gps:update', createGpsPayload());
      assert(ack && ack.success === true, 'expected successful gps:update ack');
      const eta = await etaPromise;
      if (expectEtaUpdate) {
        assert(eta && eta.tripId === tripId && eta.stopId && eta.etaMinutes > 0, 'expected eta:update payload');
      }
    } finally {
      socket.disconnect();
    }
  });
}

function assertRequiredEnv() {
  const missing = [];
  if (!tripId) missing.push('TRIP_ID');
  if (!driverToken) missing.push('DRIVER_TOKEN or ACCESS_TOKEN');
  if (!passengerToken) missing.push('PASSENGER_TOKEN');
  if (missing.length > 0) {
    throw new Error(`Missing required env vars: ${missing.join(', ')}`);
  }
}

async function runCase(name, fn) {
  try {
    await fn();
    console.log(`[PASS] ${name}`);
  } catch (error) {
    console.error(`[FAIL] ${name}: ${error.message}`);
    throw error;
  }
}

function connectSocket(token) {
  return new Promise((resolve, reject) => {
    const socket = io(baseUrl, {
      path: SOCKET_PATH,
      auth: { token },
      transports: ['websocket'],
      forceNew: true,
      reconnection: false,
      timeout: CONNECT_TIMEOUT_MS,
    });

    const timeout = setTimeout(() => {
      socket.disconnect();
      reject(new Error('SOCKET_CONNECT_TIMEOUT'));
    }, CONNECT_TIMEOUT_MS);

    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve(socket);
    });

    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      socket.disconnect();
      reject(error);
    });
  });
}

async function expectRejectsUnauthorized(promise) {
  try {
    await promise;
  } catch (error) {
    assert(error.message === 'UNAUTHORIZED', `expected UNAUTHORIZED, got ${error.message}`);
    return;
  }
  throw new Error('expected socket connection to fail');
}

function emitWithAck(socket, eventName, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(`${eventName.toUpperCase()}_ACK_TIMEOUT`));
    }, ACK_TIMEOUT_MS);

    socket.emit(eventName, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

function waitForEvent(socket, eventName) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(`${eventName.toUpperCase()}_EVENT_TIMEOUT`));
    }, EVENT_TIMEOUT_MS);

    socket.once(eventName, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

function createGpsPayload() {
  return {
    tripId,
    latitude: Number(process.env.LATITUDE || DEFAULT_LATITUDE),
    longitude: Number(process.env.LONGITUDE || DEFAULT_LONGITUDE),
    speedKmh: Number(process.env.SPEED_KMH || DEFAULT_SPEED_KMH),
    headingDeg: Number(process.env.HEADING_DEG || DEFAULT_HEADING_DEG),
    recordedAt: new Date().toISOString(),
  };
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

main().catch(() => {
  process.exitCode = 1;
});
