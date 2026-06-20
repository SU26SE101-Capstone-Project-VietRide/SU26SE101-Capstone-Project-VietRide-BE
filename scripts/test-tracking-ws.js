const { io } = require('socket.io-client');

const DEFAULT_TRACKING_URL = 'http://localhost:3001';
const SOCKET_PATH = '/tracking/socket.io';
const CONNECT_TIMEOUT_MS = 5_000;
const ACK_TIMEOUT_MS = 5_000;

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || DEFAULT_TRACKING_URL;
const tripId = process.env.TRIP_ID;
const driverToken = process.env.DRIVER_TOKEN || process.env.ACCESS_TOKEN;
const passengerToken = process.env.PASSENGER_TOKEN;

async function main() {
  assertRequiredEnv();

  await runCase('auth fail — invalid token rejects connection', async () => {
    await expectRejectsUnauthorized(connectSocket('invalid-token'));
  });

  await runCase('validation — joinTripTracking with bad tripId', async () => {
    const socket = await connectSocket(driverToken);
    try {
      const ack = await emitWithAck(socket, 'joinTripTracking', { tripId: 'not-a-uuid' });
      assert(ack && ack.success === false && ack.error === 'VALIDATION_ERROR', 'expected VALIDATION_ERROR for bad tripId');
    } finally {
      socket.disconnect();
    }
  });

  await runCase('validation — gps:update missing fields', async () => {
    const socket = await connectSocket(driverToken);
    try {
      const ack = await emitWithAck(socket, 'gps:update', { tripId });
      assert(ack && ack.success === false && ack.error === 'VALIDATION_ERROR', 'expected VALIDATION_ERROR for missing fields');
    } finally {
      socket.disconnect();
    }
  });

  await runCase('permission — passenger gps:update denied', async () => {
    const socket = await connectSocket(passengerToken);
    try {
      const ack = await emitWithAck(socket, 'gps:update', createGpsPayload());
      assert(ack && ack.success === false && ack.error === 'ACCESS_DENIED', 'expected ACCESS_DENIED for passenger');
    } finally {
      socket.disconnect();
    }
  });

  await runCase('happy — driver joinTripTracking + gps:update', async () => {
    const socket = await connectSocket(driverToken);
    try {
      const joinAck = await emitWithAck(socket, 'joinTripTracking', { tripId });
      if (joinAck.success === true) {
        const gpsAck = await emitWithAck(socket, 'gps:update', createGpsPayload());
        assert(gpsAck.success === true, 'expected gps:update ack success');
      } else {
        assert(joinAck.error === 'TRACKING_AUTH_UNAVAILABLE', 'authz downstream not available — expected TRACKING_AUTH_UNAVAILABLE');
        console.log('[INFO] Authz downstream not available; joinTripTracking returned TRACKING_AUTH_UNAVAILABLE');
      }
    } finally {
      socket.disconnect();
    }
  });

  console.log('[PASS] All WebSocket cases completed');
}

function assertRequiredEnv() {
  const missing = [];
  if (!tripId) missing.push('TRIP_ID');
  if (!driverToken) missing.push('DRIVER_TOKEN or ACCESS_TOKEN');
  if (!passengerToken) missing.push('PASSENGER_TOKEN');
  if (missing.length > 0) {
    throw new Error('Missing required env vars: ' + missing.join(', '));
  }
}

async function runCase(name, fn) {
  try {
    await fn();
    console.log('[PASS] ' + name);
  } catch (error) {
    console.error('[FAIL] ' + name + ': ' + error.message);
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
    assert(error.message === 'UNAUTHORIZED', 'expected UNAUTHORIZED, got ' + error.message);
    return;
  }
  throw new Error('expected socket connection to fail');
}

function emitWithAck(socket, eventName, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(eventName.toUpperCase() + '_ACK_TIMEOUT'));
    }, ACK_TIMEOUT_MS);

    socket.emit(eventName, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

function createGpsPayload() {
  return {
    tripId,
    latitude: Number(process.env.LATITUDE || 10.762622),
    longitude: Number(process.env.LONGITUDE || 106.660172),
    speedKmh: Number(process.env.SPEED_KMH || 40),
    headingDeg: Number(process.env.HEADING_DEG || 90),
    recordedAt: new Date().toISOString(),
  };
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

main().catch((error) => {
  console.error('[FAIL] ' + error.message);
  process.exitCode = 1;
});
