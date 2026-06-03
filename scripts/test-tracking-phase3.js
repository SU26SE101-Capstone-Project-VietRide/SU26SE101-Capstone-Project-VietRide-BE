const DEFAULT_TRACKING_URL = 'http://localhost:3001';

const baseUrl = process.env.BASE_URL ?? process.env.TRACKING_URL ?? DEFAULT_TRACKING_URL;
const accessToken = process.env.ACCESS_TOKEN;
const tripId = process.env.TRIP_ID;
const stopId = process.env.STOP_ID;

async function main() {
  const results = [];

  results.push(await runCase('auth fail returns 401 envelope', async () => {
    const response = await request(`/api/v1/tracking/trips/${readTripIdForNegativeCase()}/latest`);
    assert(response.status === 401, `expected 401, got ${response.status}`);
    assertEnvelope(response.body, false);
    assert(response.body.error?.code === 'UNAUTHORIZED', 'expected UNAUTHORIZED');
  }));

  results.push(await runCase('validation fail returns 400 envelope', async () => {
    const response = await request('/api/v1/tracking/trips/not-a-uuid/latest', accessToken);
    assert(response.status === 400, `expected 400, got ${response.status}`);
    assertEnvelope(response.body, false);
  }));

  results.push(await runCase('happy path returns tracking fallback envelopes', async () => {
    requireEnv('ACCESS_TOKEN', accessToken);
    requireEnv('TRIP_ID', tripId);
    requireEnv('STOP_ID', stopId);

    const latest = await request(`/api/v1/tracking/trips/${tripId}/latest`, accessToken);
    assert(latest.status === 200, `latest expected 200, got ${latest.status}`);
    assertEnvelope(latest.body, true);
    assert('latest' in latest.body.data, 'latest response missing data.latest');

    const trail = await request(`/api/v1/tracking/trips/${tripId}/trail`, accessToken);
    assert(trail.status === 200, `trail expected 200, got ${trail.status}`);
    assertEnvelope(trail.body, true);
    assert(Array.isArray(trail.body.data.items), 'trail response missing data.items array');

    const eta = await request(`/api/v1/tracking/trips/${tripId}/eta?stopId=${stopId}`, accessToken);
    assert(eta.status === 200, `eta expected 200, got ${eta.status}`);
    assertEnvelope(eta.body, true);
    assert('eta' in eta.body.data, 'eta response missing data.eta');
  }));

  const failed = results.filter((result) => !result.ok);
  if (failed.length > 0) {
    process.exitCode = 1;
    return;
  }

  console.log('PASS tracking phase 3 smoke');
}

async function runCase(name, fn) {
  try {
    await fn();
    console.log(`PASS ${name}`);
    return { name, ok: true };
  } catch (error) {
    console.error(`FAIL ${name}: ${error instanceof Error ? error.message : String(error)}`);
    return { name, ok: false };
  }
}

async function request(path, token) {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  return {
    status: response.status,
    body: await response.json(),
  };
}

function assertEnvelope(body, expectedSuccess) {
  assert(body && typeof body === 'object', 'body must be object');
  assert(body.success === expectedSuccess, `expected success ${expectedSuccess}`);
  assert(typeof body.statusCode === 'number', 'statusCode must be number');
  assert(body.meta && typeof body.meta.timestamp === 'string', 'meta.timestamp missing');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function requireEnv(name, value) {
  if (!value) {
    throw new Error(`${name} is required for happy path verification`);
  }
}

function readTripIdForNegativeCase() {
  return tripId ?? '11111111-1111-4111-8111-111111111111';
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
