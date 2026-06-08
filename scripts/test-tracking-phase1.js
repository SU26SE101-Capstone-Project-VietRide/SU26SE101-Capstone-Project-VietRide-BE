const { io } = require('socket.io-client');

const DEFAULT_TRACKING_URL = 'http://localhost:3001';
const SOCKET_PATH = '/tracking/socket.io';
const DEFAULT_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const CONNECT_TIMEOUT_MS = 5000;
const ACK_TIMEOUT_MS = 5000;

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || DEFAULT_TRACKING_URL;
const driverToken = process.env.DRIVER_TOKEN || process.env.ACCESS_TOKEN;
const passengerToken = process.env.PASSENGER_TOKEN;
const tripId = process.env.TRIP_ID || DEFAULT_TRIP_ID;

const results = [];

function gpsPayload() {
  return {
    tripId,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 35,
    headingDeg: 90,
    recordedAt: new Date().toISOString(),
  };
}

function pass(name) {
  results.push({ name, ok: true });
  console.log(`[PASS] ${name}`);
}

function fail(name, error) {
  results.push({ name, ok: false });
  console.error(`[FAIL] ${name}: ${error instanceof Error ? error.message : error}`);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function connectSocket(token) {
  return new Promise((resolve, reject) => {
    const socket = io(baseUrl, {
      path: SOCKET_PATH,
      auth: token ? { token } : {},
      transports: ['websocket'],
      timeout: CONNECT_TIMEOUT_MS,
      forceNew: true,
      reconnection: false,
    });

    const timer = setTimeout(() => {
      socket.close();
      reject(new Error('Socket connect timeout'));
    }, CONNECT_TIMEOUT_MS);

    socket.once('connect', () => {
      clearTimeout(timer);
      resolve(socket);
    });

    socket.once('connect_error', (error) => {
      clearTimeout(timer);
      socket.close();
      reject(error);
    });
  });
}

function emitWithAck(socket, event, payload) {
  return new Promise((resolve, reject) => {
    socket.timeout(ACK_TIMEOUT_MS).emit(event, payload, (error, response) => {
      if (error) {
        reject(error);
        return;
      }
      resolve(response);
    });
  });
}

async function expectConnectError() {
  try {
    await connectSocket('invalid-token');
    throw new Error('Expected invalid token to be rejected');
  } catch (error) {
    assert(error.message === 'UNAUTHORIZED', `Expected UNAUTHORIZED, received ${error.message}`);
  }
}

async function runCase(name, fn) {
  try {
    await fn();
    pass(name);
  } catch (error) {
    fail(name, error);
  }
}

async function main() {
  console.log(`Tracking Phase 1 production integration verify: ${baseUrl}`);

  if (!driverToken) {
    throw new Error('Missing DRIVER_TOKEN or ACCESS_TOKEN env var');
  }

  if (!passengerToken) {
    throw new Error('Missing PASSENGER_TOKEN env var');
  }

  await runCase('Auth fail rejects invalid Identity token', expectConnectError);

  await runCase('Validation fail returns VALIDATION_ERROR ack', async () => {
    const socket = await connectSocket(driverToken);
    try {
      const ack = await emitWithAck(socket, 'joinTripTracking', { tripId: 'not-a-uuid' });
      assert(ack && ack.success === false, 'Expected failed ack');
      assert(ack.error === 'VALIDATION_ERROR', `Expected VALIDATION_ERROR, received ${ack.error}`);
    } finally {
      socket.close();
    }
  });

  await runCase('Permission fail blocks passenger gps:update', async () => {
    const socket = await connectSocket(passengerToken);
    try {
      const ack = await emitWithAck(socket, 'gps:update', gpsPayload());
      assert(ack && ack.success === false, 'Expected failed ack');
      assert(ack.error === 'ACCESS_DENIED', `Expected ACCESS_DENIED, received ${ack.error}`);
    } finally {
      socket.close();
    }
  });

  await runCase('Happy path joins trip and accepts driver gps:update', async () => {
    const socket = await connectSocket(driverToken);
    try {
      const joinAck = await emitWithAck(socket, 'joinTripTracking', { tripId });
      assert(joinAck && joinAck.success === true, 'Expected join success ack');
      assert(joinAck.tripId === tripId, `Expected tripId ${tripId}, received ${joinAck.tripId}`);
      assert(typeof joinAck.room === 'string' && joinAck.room.length > 0, 'Expected room');

      const gpsAck = await emitWithAck(socket, 'gps:update', gpsPayload());
      assert(gpsAck && gpsAck.success === true, 'Expected GPS success ack');
    } finally {
      socket.close();
    }
  });

  const failed = results.filter((result) => !result.ok);
  console.log(`Result: ${results.length - failed.length}/${results.length} passed`);

  if (failed.length > 0) {
    process.exitCode = 1;
  }
}

main().catch((error) => {
  console.error(`[FATAL] ${error instanceof Error ? error.message : error}`);
  process.exitCode = 1;
});
