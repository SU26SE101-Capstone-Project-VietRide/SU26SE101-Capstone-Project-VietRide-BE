const { randomUUID } = require('node:crypto');

const requiredEnvironment = ['PASSENGER_TOKEN', 'OPERATOR_TOKEN', 'TRIP_ID', 'SHUTTLE_TRIP_ID'];
const baseUrl = process.env.BASE_URL || process.env.TRACKING_URL;

async function main() {
  const missing = requiredEnvironment.filter((name) => !process.env[name]);
  if (!baseUrl) missing.unshift('BASE_URL or TRACKING_URL');
  if (missing.length > 0) {
    throw new Error(`Missing verification environment: ${missing.join(', ')}`);
  }

  const token = process.env.PASSENGER_TOKEN;
  const operatorToken = process.env.OPERATOR_TOKEN;
  const tripId = process.env.TRIP_ID;
  const shuttleTripId = process.env.SHUTTLE_TRIP_ID;
  const routeUrl = trackingUrl(`trips/${tripId}/route-geometry`);
  const shuttleUrl = trackingUrl(`shuttle-trips/${shuttleTripId}/passenger-context`);
  const operatorShuttleUrl = trackingUrl(`shuttle-trips/${shuttleTripId}/operator-context`);

  const route = await request(routeUrl, token);
  assertStatus(route, 200, 'route geometry happy path');
  const routeData = assertSuccessEnvelope(route, 'route geometry');
  assertAllowedKeys(routeData, [
    'tripId',
    'geometry',
    'originStation',
    'intermediateStops',
    'destinationStation',
  ], 'route data');
  assertRouteContext(routeData);
  assertHeader(route, 'cache-control', 'private, max-age=600');
  assertHeader(route, 'vary', 'Authorization');
  if (route.text.includes('alertRecipientUserIds')) {
    throw new Error('Route response leaked alertRecipientUserIds');
  }

  const etag = route.response.headers.get('etag');
  if (!etag || !/^"[a-f0-9]{64}"$/.test(etag)) {
    throw new Error(`Route response has invalid strong ETag: ${etag ?? '<missing>'}`);
  }
  const notModified = await request(routeUrl, token, { 'If-None-Match': etag });
  assertStatus(notModified, 304, 'ETag roundtrip');
  if (notModified.text.length !== 0) throw new Error('304 response must have an empty body');

  const missingToken = await request(routeUrl);
  assertStatus(missingToken, 401, 'missing token');
  assertErrorEnvelopeOneOf(missingToken, ['UNAUTHORIZED', 'AUTH_TOKEN_INVALID']);

  const unknownTrip = await request(
    trackingUrl(`trips/${randomUUID()}/route-geometry`),
    token,
  );
  if (unknownTrip.response.status !== 403 && unknownTrip.response.status !== 404) {
    throw new Error(`Unknown/non-owned trip expected 403 or 404, got ${unknownTrip.response.status}`);
  }

  const passenger = await request(shuttleUrl, token);
  assertStatus(passenger, 200, 'Shuttle passenger context');
  const passengerData = assertSuccessEnvelope(passenger, 'Shuttle passenger context');
  assertPassengerContext(passengerData, passenger.text);
  assertHeader(passenger, 'cache-control', 'private, no-store');

  const operator = await request(operatorShuttleUrl, operatorToken);
  assertStatus(operator, 200, 'Shuttle operator context');
  const operatorData = assertSuccessEnvelope(operator, 'Shuttle operator context');
  assertOperatorContext(operatorData, operator.text);
  assertHeader(operator, 'cache-control', 'private, no-store');

  const passengerDeniedOperatorContext = await request(operatorShuttleUrl, token);
  assertStatus(passengerDeniedOperatorContext, 403, 'passenger operator-context denial');
  assertErrorEnvelope(passengerDeniedOperatorContext, 'TRACKING_ACCESS_DENIED');

  if (process.env.OTHER_OPERATOR_TOKEN) {
    const otherOperator = await request(operatorShuttleUrl, process.env.OTHER_OPERATOR_TOKEN);
    assertStatus(otherOperator, 403, 'other-tenant operator-context denial');
    assertErrorEnvelope(otherOperator, 'TRACKING_ACCESS_DENIED');
  }

  const postPickupShuttleTripId = process.env.POST_PICKUP_SHUTTLE_TRIP_ID;
  if (postPickupShuttleTripId) {
    const postPickup = await request(
      trackingUrl(`shuttle-trips/${postPickupShuttleTripId}/passenger-context`),
      token,
    );
    assertStatus(postPickup, 200, 'post-pickup passenger access');
    const postPickupData = assertSuccessEnvelope(postPickup, 'post-pickup passenger context');
    if (!postPickupData.ownPickups.some((pickup) =>
      pickup.status === 'PICKED_UP' && pickup.stopsBeforePickup === 0)) {
      throw new Error('Post-pickup fixture did not return PICKED_UP with stopsBeforePickup=0');
    }
  }

  console.log('Tracking map context verification passed.');
}

function trackingUrl(path) {
  const normalizedBase = baseUrl.replace(/\/$/, '');
  const prefix = normalizedBase.endsWith('/v1/tracking') ? normalizedBase : `${normalizedBase}/v1/tracking`;
  return `${prefix}/${path}`;
}

async function request(url, token, extraHeaders = {}) {
  const headers = { ...extraHeaders };
  if (token) headers.Authorization = `Bearer ${token}`;
  const timeoutMs = Number(process.env.VERIFY_TIMEOUT_MS || 10_000);
  const response = await fetch(url, {
    headers,
    signal: AbortSignal.timeout(timeoutMs),
  });
  const text = await response.text();
  let json;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      throw new Error(`${url} returned non-JSON body: ${text.slice(0, 200)}`);
    }
  }
  return { response, text, json };
}

function assertStatus(result, expected, label) {
  if (result.response.status !== expected) {
    throw new Error(`${label} expected HTTP ${expected}, got ${result.response.status}: ${result.text}`);
  }
}

function assertSuccessEnvelope(result, label) {
  const envelope = result.json;
  if (!envelope || envelope.success !== true || envelope.statusCode !== 200 || !envelope.data) {
    throw new Error(`${label} returned an invalid ApiResponse envelope`);
  }
  return envelope.data;
}

function assertErrorEnvelope(result, expectedCode) {
  if (!result.json || result.json.success !== false || result.json.error?.code !== expectedCode) {
    throw new Error(`Expected error envelope ${expectedCode}, got: ${result.text}`);
  }
}

function assertErrorEnvelopeOneOf(result, expectedCodes) {
  if (!result.json
    || result.json.success !== false
    || !expectedCodes.includes(result.json.error?.code)) {
    throw new Error(`Expected error envelope ${expectedCodes.join(' or ')}, got: ${result.text}`);
  }
}

function assertRouteContext(data) {
  if (!Array.isArray(data.intermediateStops)) throw new Error('intermediateStops must be an array');
  assertNullableMarker(data.originStation, ['stationId', 'name', 'latitude', 'longitude'], 'originStation');
  assertNullableMarker(data.destinationStation, ['stationId', 'name', 'latitude', 'longitude'], 'destinationStation');
  for (const stop of data.intermediateStops) {
    assertAllowedKeys(stop, ['stopId', 'name', 'sequence', 'latitude', 'longitude'], 'intermediate stop');
    assertCoordinate(stop, 'intermediate stop');
  }
  if (data.geometry === null) return;
  assertAllowedKeys(data.geometry, ['source', 'points'], 'geometry');
  if (data.geometry.source !== 'ROUTE_POLYLINE') throw new Error('Public geometry source must be ROUTE_POLYLINE');
  if (!Array.isArray(data.geometry.points) || data.geometry.points.length > 1_000) {
    throw new Error('Public geometry must contain at most 1,000 points');
  }
  data.geometry.points.forEach((point, index) => {
    assertAllowedKeys(point, ['latitude', 'longitude'], `geometry point ${index}`);
    assertCoordinate(point, `geometry point ${index}`);
    const previous = data.geometry.points[index - 1];
    if (previous && previous.latitude === point.latitude && previous.longitude === point.longitude) {
      throw new Error(`Geometry contains consecutive duplicate at index ${index}`);
    }
  });
}

function assertPassengerContext(data, rawText) {
  assertAllowedKeys(data, ['shuttleTripId', 'mainTripId', 'ownPickups', 'station'], 'passenger context');
  if (!Array.isArray(data.ownPickups) || data.ownPickups.length === 0) {
    throw new Error('Passenger context must contain at least one own pickup');
  }
  for (const pickup of data.ownPickups) {
    assertAllowedKeys(pickup, [
      'bookingId',
      'pickupOrder',
      'serviceAddress',
      'serviceOrder',
      'roadDistanceMeters',
      'latitude',
      'longitude',
      'status',
      'stopsBeforePickup',
    ], 'own pickup');
    assertCoordinate(pickup, 'own pickup');
    if (!['PENDING', 'PICKED_UP'].includes(pickup.status)) {
      throw new Error(`Unexpected own pickup status: ${pickup.status}`);
    }
    if (pickup.status === 'PICKED_UP' && pickup.stopsBeforePickup !== 0) {
      throw new Error('PICKED_UP must have stopsBeforePickup=0');
    }
  }
  assertNullableMarker(data.station, [
    'stationId',
    'name',
    'latitude',
    'longitude',
    'pickupOrder',
  ], 'station');
  for (const forbidden of ['passengerUserId', 'address', 'isOwnPickup', 'stops']) {
    if (rawText.includes(`"${forbidden}"`)) throw new Error(`Passenger response leaked ${forbidden}`);
  }
}

function assertOperatorContext(data, rawText) {
  assertAllowedKeys(data, [
    'shuttleTripId',
    'mainTripId',
    'direction',
    'status',
    'stops',
    'station',
  ], 'operator context');
  if (!['INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION'].includes(data.direction)) {
    throw new Error(`Unexpected Shuttle direction: ${data.direction}`);
  }
  if (!Array.isArray(data.stops) || data.stops.length === 0) {
    throw new Error('Operator context must contain Shuttle stops');
  }
  for (const stop of data.stops) {
    assertAllowedKeys(stop, [
      'pickupOrder',
      'bookingId',
      'latitude',
      'longitude',
      'status',
      'isStation',
      'serviceAddress',
      'serviceOrder',
      'roadDistanceMeters',
    ], 'operator stop');
    assertCoordinate(stop, 'operator stop');
    if (!['PENDING', 'PICKED_UP', 'DELIVERED', 'NO_SHOW', 'CANCELLED'].includes(stop.status)) {
      throw new Error(`Unexpected operator stop status: ${stop.status}`);
    }
  }
  assertNullableMarker(data.station, [
    'stationId',
    'name',
    'latitude',
    'longitude',
    'pickupOrder',
  ], 'operator station');
  for (const forbidden of ['passengerUserId', 'displayName', 'phone', 'isOwnPickup', 'roadDistanceSnapshotMeters']) {
    if (rawText.includes(`"${forbidden}"`)) throw new Error(`Operator response leaked ${forbidden}`);
  }
}

function assertNullableMarker(marker, allowedKeys, label) {
  if (marker === null) return;
  if (!marker || typeof marker !== 'object') throw new Error(`${label} must be an object or null`);
  assertAllowedKeys(marker, allowedKeys, label);
  assertCoordinate(marker, label);
}

function assertCoordinate(point, label) {
  if (!Number.isFinite(point.latitude)
    || !Number.isFinite(point.longitude)
    || point.latitude < -90
    || point.latitude > 90
    || point.longitude < -180
    || point.longitude > 180) {
    throw new Error(`${label} has invalid coordinates`);
  }
}

function assertAllowedKeys(value, allowedKeys, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} must be an object`);
  }
  const unexpected = Object.keys(value).filter((key) => !allowedKeys.includes(key));
  if (unexpected.length > 0) throw new Error(`${label} contains unexpected fields: ${unexpected.join(', ')}`);
}

function assertHeader(result, name, expected) {
  const actual = result.response.headers.get(name);
  if (actual !== expected) throw new Error(`Expected ${name}: ${expected}, got ${actual ?? '<missing>'}`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
