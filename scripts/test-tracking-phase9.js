const { io } = require('socket.io-client');

const DEFAULT_TRACKING_URL = 'http://localhost:3001';
const TRACKING_SOCKET_PATH = '/tracking/socket.io';
const CONNECT_TIMEOUT_MS = 5_000;
const ACK_TIMEOUT_MS = 5_000;

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || DEFAULT_TRACKING_URL;
const driverToken = process.env.DRIVER_TOKEN;
const tripId = process.env.TRIP_ID;

async function main() {
  if (!driverToken || !tripId) {
    fail('Missing required env: DRIVER_TOKEN and TRIP_ID');
  }

  await assertAuthFail();
  const socket = await connectSocket(driverToken);
  try {
    await assertValidationFail(socket);
    await assertGpsUpdateHappyPath(socket);
  } finally {
    socket.disconnect();
  }

  pass('Tracking Phase 9 smoke passed');
}

async function assertAuthFail() {
  try {
    await connectSocket(undefined);
    fail('Auth fail case unexpectedly connected');
  } catch (error) {
    assert(String(error.message).includes('UNAUTHORIZED'), 'Auth fail case must return UNAUTHORIZED');
    pass('Auth fail case passed');
  }
}

async function assertValidationFail(socket) {
  const ack = await emitWithAck(socket, 'joinTripTracking', { tripId: 'bad-trip-id' });
  assert(ack && ack.success === false, 'Validation fail ack must be unsuccessful');
  assert(ack.error === 'VALIDATION_ERROR', 'Validation fail ack must use VALIDATION_ERROR');
  pass('Validation fail case passed');
}

async function assertGpsUpdateHappyPath(socket) {
  const ack = await emitWithAck(socket, 'gps:update', {
    tripId,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 36,
    headingDeg: 90,
    recordedAt: new Date().toISOString(),
  });

  assert(ack && ack.success === true, `gps:update happy path failed: ${JSON.stringify(ack)}`);
  pass('gps:update happy path passed');
}

function connectSocket(token) {
  return new Promise((resolve, reject) => {
    const socket = io(baseUrl, {
      path: TRACKING_SOCKET_PATH,
      auth: token ? { token } : {},
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

function assert(condition, message) {
  if (!condition) fail(message);
}

function pass(message) {
  console.log(`[PASS] ${message}`);
}

function fail(message) {
  console.error(`[FAIL] ${message}`);
  process.exit(1);
}

main().catch((error) => fail(error.stack || error.message));
