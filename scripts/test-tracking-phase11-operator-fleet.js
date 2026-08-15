const requiredEnvironment = [
  'OPERATOR_TOKEN',
  'OTHER_OPERATOR_TOKEN',
  'PASSENGER_TOKEN',
  'SHUTTLE_TRIP_ID',
];
const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL;

async function main() {
  const missing = requiredEnvironment.filter((name) => !process.env[name]);
  if (!baseUrl) missing.unshift('BASE_URL or TRACKING_URL');
  if (missing.length > 0) {
    throw new Error(`Missing verification environment: ${missing.join(', ')}`);
  }

  const endpoint = trackingUrl('operator/fleet-latest');
  const targetShuttleTripId = process.env.SHUTTLE_TRIP_ID;

  const fleet = await request(
    `${endpoint}?include=shuttle&status=IN_PROGRESS`,
    process.env.OPERATOR_TOKEN,
  );
  assertStatus(fleet, 200, 'operator Shuttle fleet');
  const data = assertSuccessEnvelope(fleet, 'operator Shuttle fleet');
  if (!Array.isArray(data.items)) throw new Error('fleet items must be an array');
  const shuttle = data.items.find((item) =>
    item.kind === 'SHUTTLE' && item.shuttleTripId === targetShuttleTripId);
  if (!shuttle) throw new Error('owned active Shuttle is missing from fleet-latest');
  assertShuttleItem(shuttle);
  data.items.filter((item) => item.kind === 'TRIP').forEach(assertTripItem);

  const legacy = await request(endpoint, process.env.OPERATOR_TOKEN);
  assertStatus(legacy, 200, 'fleet without Shuttle opt-in');
  const legacyData = assertSuccessEnvelope(legacy, 'fleet without Shuttle opt-in');
  if (legacyData.items.some((item) => item.kind === 'SHUTTLE')) {
    throw new Error('fleet returned Shuttle without include=shuttle');
  }

  const invalid = await request(`${endpoint}?include=bus`, process.env.OPERATOR_TOKEN);
  assertStatus(invalid, 400, 'invalid fleet include');
  assertErrorEnvelope(invalid, 'VALIDATION_FAILED');

  const unauthenticated = await request(endpoint);
  assertStatus(unauthenticated, 401, 'fleet missing auth');

  const passenger = await request(`${endpoint}?include=shuttle`, process.env.PASSENGER_TOKEN);
  assertStatus(passenger, 403, 'passenger fleet denial');

  const otherTenant = await request(
    `${endpoint}?include=shuttle&status=IN_PROGRESS`,
    process.env.OTHER_OPERATOR_TOKEN,
  );
  assertStatus(otherTenant, 200, 'other operator fleet');
  const otherData = assertSuccessEnvelope(otherTenant, 'other operator fleet');
  if (otherData.items.some((item) => item.shuttleTripId === targetShuttleTripId)) {
    throw new Error('other operator received the target Shuttle');
  }

  console.log('Tracking operator Shuttle fleet verification passed.');
}

function trackingUrl(path) {
  const normalizedBase = baseUrl.replace(/\/$/, '');
  const prefix = normalizedBase.endsWith('/v1/tracking')
    ? normalizedBase
    : `${normalizedBase}/v1/tracking`;
  return `${prefix}/${path}`;
}

async function request(url, token) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  const response = await fetch(url, {
    headers,
    signal: AbortSignal.timeout(Number(process.env.VERIFY_TIMEOUT_MS || 10_000)),
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : undefined;
  return { response, text, json };
}

function assertStatus(result, expected, label) {
  if (result.response.status !== expected) {
    throw new Error(`${label} expected HTTP ${expected}, got ${result.response.status}: ${result.text}`);
  }
}

function assertSuccessEnvelope(result, label) {
  if (!result.json
    || result.json.success !== true
    || result.json.statusCode !== 200
    || !result.json.data) {
    throw new Error(`${label} returned an invalid ApiResponse envelope`);
  }
  return result.json.data;
}

function assertErrorEnvelope(result, code) {
  if (!result.json || result.json.success !== false || result.json.error?.code !== code) {
    throw new Error(`Expected error envelope ${code}, got: ${result.text}`);
  }
}

function assertTripItem(item) {
  const allowed = [
    'kind', 'tripId', 'latitude', 'longitude', 'speedKmh', 'headingDeg', 'recordedAt', 'status',
  ];
  assertAllowedKeys(item, allowed, 'Trip fleet item');
  if (item.kind !== 'TRIP' || !item.tripId || item.shuttleTripId !== undefined) {
    throw new Error('Invalid Trip fleet discriminator or ID shape');
  }
  assertCoordinate(item, 'Trip fleet item');
}

function assertShuttleItem(item) {
  const allowed = [
    'kind', 'shuttleTripId', 'mainTripId', 'latitude', 'longitude', 'speedKmh', 'headingDeg',
    'recordedAt', 'status',
  ];
  assertAllowedKeys(item, allowed, 'Shuttle fleet item');
  if (item.kind !== 'SHUTTLE'
    || !item.shuttleTripId
    || !item.mainTripId
    || item.tripId !== undefined
    || item.status !== 'IN_PROGRESS') {
    throw new Error('Invalid Shuttle fleet discriminator, ID, or status shape');
  }
  assertCoordinate(item, 'Shuttle fleet item');
}

function assertCoordinate(item, label) {
  if (!Number.isFinite(item.latitude)
    || !Number.isFinite(item.longitude)
    || item.latitude < -90
    || item.latitude > 90
    || item.longitude < -180
    || item.longitude > 180) {
    throw new Error(`${label} has invalid coordinates`);
  }
}

function assertAllowedKeys(value, allowed, label) {
  const unexpected = Object.keys(value).filter((key) => !allowed.includes(key));
  if (unexpected.length > 0) {
    throw new Error(`${label} contains unexpected fields: ${unexpected.join(', ')}`);
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
