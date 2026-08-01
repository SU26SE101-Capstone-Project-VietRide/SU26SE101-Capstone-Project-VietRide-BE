const { io } = require('socket.io-client');
const { spawnSync } = require('node:child_process');

const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL || 'http://localhost:3001';
const driverToken = process.env.DRIVER_TOKEN || '';
const passengerToken = process.env.PASSENGER_TOKEN || process.env.ACCESS_TOKEN || '';
const shuttleTripId = process.env.SHUTTLE_TRIP_ID || '';
const liveValues = [driverToken, passengerToken, shuttleTripId].filter(Boolean).length;
const failures = [];

(async () => {
  if (liveValues === 0) {
    process.stdout.write('No live Shuttle credentials supplied; running fake-Google Shuttle E2E.\n');
    const result = spawnSync(
      'npx',
      [
        'jest',
        '--config',
        'apps/tracking/jest.e2e.config.cts',
        '--runInBand',
        'apps/tracking/src/shuttle/shuttle-google-routes.e2e-spec.ts',
      ],
      {
        cwd: process.cwd(),
        stdio: 'inherit',
        shell: process.platform === 'win32',
      },
    );
    process.exitCode = result.status ?? 1;
    return;
  }

  if (liveValues !== 3) {
    throw new Error('Live smoke requires DRIVER_TOKEN, PASSENGER_TOKEN and SHUTTLE_TRIP_ID together');
  }

  await runCase('auth failure returns an ADR 0004 envelope', async () => {
    const response = await fetch(
      new URL(`/v1/tracking/shuttle-trips/${shuttleTripId}/latest`, baseUrl),
    );
    const body = await response.json();
    assert(response.status === 401, `expected 401, received ${response.status}`);
    assert(body.success === false && body.error, 'missing error envelope');
  });

  await runCase('validation failure returns an ADR 0004 envelope', async () => {
    const response = await request('/v1/tracking/shuttle-trips/not-a-uuid/latest');
    const body = await response.json();
    assert([400, 422].includes(response.status), `unexpected status ${response.status}`);
    assert(body.success === false && body.error, 'missing validation envelope');
  });

  await runCase('Socket GPS ack is followed by Shuttle GPS and ETA events', async () => {
    const passenger = await connect(passengerToken);
    const driver = await connect(driverToken);
    try {
      const joinAck = await emitAck(passenger, 'joinShuttleTracking', { shuttleTripId });
      assert(joinAck?.success === true, `passenger join failed: ${JSON.stringify(joinAck)}`);

      const gpsEvent = onceEvent(passenger, 'shuttle:gps:update', 5_000);
      const etaEvent = onceEvent(passenger, 'shuttle:eta:update', 10_000);
      const gpsAck = await emitAck(driver, 'shuttle:gps:update', {
        shuttleTripId,
        latitude: Number(process.env.SHUTTLE_LATITUDE || 10.762622),
        longitude: Number(process.env.SHUTTLE_LONGITUDE || 106.660172),
        speedKmh: 30,
        recordedAt: new Date().toISOString(),
      });
      assert(gpsAck?.success === true, `GPS ack failed: ${JSON.stringify(gpsAck)}`);

      const [gps, eta] = await Promise.all([gpsEvent, etaEvent]);
      assert(gps.shuttleTripId === shuttleTripId, 'unexpected Shuttle GPS event');
      assert(eta.shuttleTripId === shuttleTripId, 'unexpected Shuttle ETA event');
      assert(Number.isFinite(eta.etaMinutes) && eta.etaMinutes > 0, 'invalid ETA minutes');
      assert(Number.isFinite(eta.distanceMeters) && eta.distanceMeters >= 0, 'invalid distance');
    } finally {
      passenger.close();
      driver.close();
    }
  });

  await runCase('REST latest and ETA preserve success envelopes', async () => {
    for (const suffix of ['latest', 'eta']) {
      const response = await request(`/v1/tracking/shuttle-trips/${shuttleTripId}/${suffix}`);
      const body = await response.json();
      assert(response.status === 200, `${suffix} returned ${response.status}`);
      assert(body.success === true && Object.prototype.hasOwnProperty.call(body, 'data'), `${suffix} missing success envelope`);
    }
  });

  if (failures.length > 0) {
    process.stderr.write(`${failures.length} Shuttle Google smoke checks failed.\n`);
    process.exitCode = 1;
  } else {
    process.stdout.write('Tracking Phase 11 Shuttle Google smoke verification passed.\n');
  }
})();

async function runCase(name, run) {
  try {
    await run();
    process.stdout.write(`PASS ${name}\n`);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    failures.push(`${name}: ${message}`);
    process.stderr.write(`FAIL ${name}: ${message}\n`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function request(path) {
  return fetch(new URL(path, baseUrl), {
    headers: { Authorization: `Bearer ${passengerToken}` },
  });
}

function connect(token) {
  return new Promise((resolve, reject) => {
    const socket = io(baseUrl, {
      path: '/tracking/socket.io',
      auth: { token },
      transports: ['websocket'],
      timeout: 5_000,
      reconnection: false,
    });
    socket.once('connect', () => resolve(socket));
    socket.once('connect_error', reject);
  });
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

function onceEvent(socket, event, timeoutMs) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} event timeout`)), timeoutMs);
    socket.once(event, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}
