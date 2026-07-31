const { io } = require('socket.io-client');
const { spawnSync } = require('node:child_process');

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || 'http://localhost:3001';
const token = process.env.DRIVER_TOKEN || process.env.ACCESS_TOKEN || '';
const tripId = process.env.TRIP_ID || '00000000-0000-4000-8000-000000000000';
const stopId = process.env.STOP_ID || '00000000-0000-4000-8000-000000000001';
const failures = [];

(async () => {
if (!token) {
  process.stdout.write('No live token supplied; running the isolated Socket/REST E2E smoke matrix.\n');
  const result = spawnSync('npx', ['nx', 'run', 'tracking:test:e2e', '--runInBand'], {
    cwd: process.cwd(),
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
  process.exitCode = result.status ?? 1;
  return;
}

async function runCase(name, fn) {
  try {
    await fn();
    process.stdout.write(`PASS ${name}\n`);
  } catch (error) {
    failures.push(`${name}: ${error instanceof Error ? error.message : String(error)}`);
    process.stderr.write(`FAIL ${name}: ${failures.at(-1)}\n`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function request(path, options = {}) {
  return fetch(new URL(path, baseUrl), {
    ...options,
    headers: { ...(options.headers || {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  });
}

await runCase('auth failure returns ApiResponse error envelope', async () => {
  const response = await fetch(new URL(`/v1/tracking/trips/${tripId}/latest`, baseUrl));
  const body = await response.json();
  assert(response.status === 401, `expected 401, received ${response.status}`);
  assert(body.success === false && body.error && typeof body.error.code === 'string', 'missing ADR 0004 error envelope');
});

await runCase('validation failure returns ApiResponse error envelope', async () => {
  const response = await request('/v1/tracking/trips/not-a-uuid/latest');
  const body = await response.json();
  assert([400, 422].includes(response.status), `expected validation status, received ${response.status}`);
  assert(body.success === false && body.error, 'missing validation envelope');
});

await runCase('REST happy path preserves ApiResponse envelope', async () => {
  assert(token, 'ACCESS_TOKEN or DRIVER_TOKEN is required for the happy path');
  const response = await request(`/v1/tracking/trips/${tripId}/eta?stopId=${stopId}`);
  const body = await response.json();
  assert(response.status < 500, `unexpected server failure ${response.status}`);
  assert(body.success === true && body.data && Object.prototype.hasOwnProperty.call(body.data, 'eta'), 'missing ETA success envelope');
});

await runCase('Socket happy path returns ack and gps event', async () => {
  assert(token, 'ACCESS_TOKEN or DRIVER_TOKEN is required for the socket path');
  const socket = io(baseUrl, { path: '/tracking/socket.io', auth: { token }, transports: ['websocket'], timeout: 3000, reconnection: false });
  await new Promise((resolve, reject) => {
    socket.once('connect', resolve);
    socket.once('connect_error', reject);
  });
  const joinAck = await new Promise((resolve) => socket.emit('joinTripTracking', { tripId }, resolve));
  assert(joinAck && (joinAck.success === true || joinAck.error === 'ACCESS_DENIED'), 'unexpected join ack');
  const gpsEvent = new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('gps:update event timeout')), 3000);
    socket.once('gps:update', (payload) => { clearTimeout(timeout); resolve(payload); });
  });
  const gpsAck = await new Promise((resolve) => socket.emit('gps:update', {
    tripId,
    latitude: 10.762622,
    longitude: 106.660172,
    recordedAt: new Date().toISOString(),
  }, resolve));
  assert(gpsAck && gpsAck.success === true, `GPS ack failed: ${JSON.stringify(gpsAck)}`);
  const published = await gpsEvent;
  assert(published.tripId === tripId && typeof published.latitude === 'number', 'unexpected published GPS event');
  socket.close();
});

if (failures.length) {
  process.stderr.write(`${failures.length} Tracking Phase 10 checks failed.\n`);
  process.exitCode = 1;
} else {
  process.stdout.write('Tracking Phase 10 smoke verification passed.\n');
}
})();
