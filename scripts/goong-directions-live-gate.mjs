import { readFileSync } from 'node:fs';
import { performance } from 'node:perf_hooks';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const DEFAULT_BASE_URL = 'https://rsapi.goong.io';
const DEFAULT_MAX_DESTINATIONS = 10;
const DEFAULT_TIMEOUT_MS = 5_000;
const ENDPOINT_TOLERANCE_DEGREES = 0.02;
const MAX_FIXTURE_ROUTES = 200;
const MAX_POINTS_PER_ROUTE = 30;

export class LiveGateError extends Error {
  constructor(code, context = {}) {
    super(code);
    this.name = 'LiveGateError';
    this.code = code;
    this.context = context;
  }
}

export function parseArguments(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index += 2) {
    const flag = argv[index];
    const value = argv[index + 1];
    if (!flag?.startsWith('--') || value === undefined) {
      throw new LiveGateError('INVALID_ARGUMENTS');
    }
    values.set(flag, value);
  }

  const fixturePath = values.get('--fixture');
  if (!fixturePath) throw new LiveGateError('FIXTURE_PATH_REQUIRED');

  return {
    fixturePath,
    minimumRoutes: parsePositiveInteger(values.get('--minimum-routes') ?? '50', 'MINIMUM_ROUTES'),
    minimumMultipointRoutes: parsePositiveInteger(
      values.get('--minimum-multipoint-routes') ?? '5',
      'MINIMUM_MULTIPOINT_ROUTES',
    ),
    timeoutMs: parsePositiveInteger(
      values.get('--timeout-ms') ?? String(DEFAULT_TIMEOUT_MS),
      'TIMEOUT_MS',
    ),
  };
}

export function validateFixture(fixture, minimumRoutes, minimumMultipointRoutes) {
  if (!fixture || typeof fixture !== 'object' || !Array.isArray(fixture.routes)) {
    throw new LiveGateError('FIXTURE_ROUTES_INVALID');
  }
  if (fixture.routes.length < minimumRoutes || fixture.routes.length > MAX_FIXTURE_ROUTES) {
    throw new LiveGateError('FIXTURE_ROUTE_COUNT_INVALID', {
      actual: fixture.routes.length,
      minimum: minimumRoutes,
      maximum: MAX_FIXTURE_ROUTES,
    });
  }

  const routes = fixture.routes.map((route, routeIndex) => {
    if (
      !route ||
      typeof route !== 'object' ||
      typeof route.name !== 'string' ||
      !route.name.trim()
    ) {
      throw new LiveGateError('FIXTURE_ROUTE_NAME_INVALID', { routeIndex });
    }
    if (
      !Array.isArray(route.points) ||
      route.points.length < 2 ||
      route.points.length > MAX_POINTS_PER_ROUTE
    ) {
      throw new LiveGateError('FIXTURE_ROUTE_POINTS_INVALID', {
        routeIndex,
        actual: Array.isArray(route.points) ? route.points.length : 0,
      });
    }
    const points = route.points.map((point, pointIndex) => {
      if (
        !point ||
        !Number.isFinite(point.lat) ||
        !Number.isFinite(point.lng) ||
        point.lat < 8 ||
        point.lat > 24 ||
        point.lng < 102 ||
        point.lng > 110
      ) {
        throw new LiveGateError('FIXTURE_POINT_OUTSIDE_VIETNAM', { routeIndex, pointIndex });
      }
      return { lat: point.lat, lng: point.lng };
    });
    return { name: route.name.trim(), points };
  });

  const multipointRoutes = routes.filter(
    (route) => route.points.length >= 11 && route.points.length <= 30,
  ).length;
  if (multipointRoutes < minimumMultipointRoutes) {
    throw new LiveGateError('FIXTURE_MULTIPOINT_COUNT_INVALID', {
      actual: multipointRoutes,
      minimum: minimumMultipointRoutes,
    });
  }
  return { routes, multipointRoutes };
}

export async function runGoongLiveGate({
  fixture,
  apiKey,
  baseUrl = DEFAULT_BASE_URL,
  maxDestinations = DEFAULT_MAX_DESTINATIONS,
  minimumRoutes = 50,
  minimumMultipointRoutes = 5,
  timeoutMs = DEFAULT_TIMEOUT_MS,
  fetchImpl = fetch,
}) {
  if (typeof apiKey !== 'string' || apiKey.trim().length === 0) {
    throw new LiveGateError('GOONG_API_KEY_MISSING');
  }
  const destinationLimit = parsePositiveInteger(String(maxDestinations), 'MAX_DESTINATIONS');
  if (destinationLimit > DEFAULT_MAX_DESTINATIONS) {
    throw new LiveGateError('MAX_DESTINATIONS_EXCEEDS_CONTRACT', {
      actual: destinationLimit,
      maximum: DEFAULT_MAX_DESTINATIONS,
    });
  }
  const requestTimeoutMs = parsePositiveInteger(String(timeoutMs), 'TIMEOUT_MS');
  const endpoint = createEndpoint(baseUrl);
  const validated = validateFixture(fixture, minimumRoutes, minimumMultipointRoutes);
  const latencies = [];
  let requestCount = 0;
  let legCount = 0;

  for (let routeIndex = 0; routeIndex < validated.routes.length; routeIndex += 1) {
    const route = validated.routes[routeIndex];
    let origin = route.points[0];
    let routeLegCount = 0;
    for (let offset = 1, chunkIndex = 0; offset < route.points.length; chunkIndex += 1) {
      const targets = route.points.slice(offset, offset + destinationLimit);
      const result = await requestChunk({
        endpoint,
        apiKey: apiKey.trim(),
        origin,
        targets,
        timeoutMs: requestTimeoutMs,
        fetchImpl,
        routeIndex,
        chunkIndex,
      });
      requestCount += 1;
      routeLegCount += result.legs.length;
      legCount += result.legs.length;
      latencies.push(result.latencyMs);
      origin = targets.at(-1);
      offset += targets.length;
    }
    if (routeLegCount !== route.points.length - 1) {
      throw new LiveGateError('FULL_CHAIN_LEG_COUNT_INVALID', {
        routeIndex,
        actual: routeLegCount,
        expected: route.points.length - 1,
      });
    }
  }

  const p95Ms = calculatePercentile(latencies, 0.95);
  if (!Number.isFinite(p95Ms) || p95Ms >= requestTimeoutMs) {
    throw new LiveGateError('P95_TIMEOUT_BUDGET_EXCEEDED', {
      p95Ms: Math.round(p95Ms),
      timeoutMs: requestTimeoutMs,
    });
  }

  return {
    routes: validated.routes.length,
    multipointRoutes: validated.multipointRoutes,
    requests: requestCount,
    legs: legCount,
    p95Ms: Math.round(p95Ms),
    timeoutMs: requestTimeoutMs,
  };
}

async function requestChunk({
  endpoint,
  apiKey,
  origin,
  targets,
  timeoutMs,
  fetchImpl,
  routeIndex,
  chunkIndex,
}) {
  const requestUrl = new URL(endpoint);
  requestUrl.searchParams.set('origin', formatCoordinate(origin));
  requestUrl.searchParams.set('destination', targets.map(formatCoordinate).join(';'));
  requestUrl.searchParams.set('vehicle', 'car');
  requestUrl.searchParams.set('alternatives', 'false');
  requestUrl.searchParams.set('api_key', apiKey);

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  const startedAt = performance.now();
  let response;
  try {
    response = await fetchImpl(requestUrl, { method: 'GET', signal: controller.signal });
    if (response.status !== 200) {
      throw new LiveGateError('GOONG_HTTP_STATUS_INVALID', {
        routeIndex,
        chunkIndex,
        status: response.status,
      });
    }
    const body = await response.json();
    const legs = readLegs(body, origin, targets, routeIndex, chunkIndex);
    return { latencyMs: performance.now() - startedAt, legs };
  } catch (error) {
    if (error instanceof LiveGateError) throw error;
    if (controller.signal.aborted || error?.name === 'AbortError') {
      throw new LiveGateError('GOONG_REQUEST_TIMEOUT', { routeIndex, chunkIndex, timeoutMs });
    }
    if (response) {
      throw new LiveGateError('GOONG_RESPONSE_MALFORMED', { routeIndex, chunkIndex });
    }
    throw new LiveGateError('GOONG_REQUEST_NETWORK_ERROR', { routeIndex, chunkIndex });
  } finally {
    clearTimeout(timeout);
  }
}

function readLegs(body, origin, targets, routeIndex, chunkIndex) {
  const legs = body?.routes?.[0]?.legs;
  if (!Array.isArray(legs) || legs.length !== targets.length) {
    throw new LiveGateError('GOONG_LEG_COUNT_INVALID', {
      routeIndex,
      chunkIndex,
      actual: Array.isArray(legs) ? legs.length : 0,
      expected: targets.length,
    });
  }

  const expectedStarts = [origin, ...targets.slice(0, -1)];
  return legs.map((leg, legIndex) => {
    const expectedStart = expectedStarts[legIndex];
    const expectedEnd = targets[legIndex];
    const distanceMeters = readMetric(leg?.distance?.value);
    const durationSeconds = readMetric(leg?.duration?.value);
    const actualStart = readCoordinate(leg?.start_location);
    const actualEnd = readCoordinate(leg?.end_location);
    if (distanceMeters === null || durationSeconds === null) {
      throw new LiveGateError('GOONG_METRIC_INVALID', { routeIndex, chunkIndex, legIndex });
    }
    if (
      !actualStart ||
      !actualEnd ||
      !coordinateMatchesOrder(actualStart, expectedStart, expectedStarts, legIndex) ||
      !coordinateMatchesOrder(actualEnd, expectedEnd, targets, legIndex)
    ) {
      throw new LiveGateError('GOONG_ENDPOINT_ORDER_INVALID', {
        routeIndex,
        chunkIndex,
        legIndex,
      });
    }
    return { distanceMeters, durationSeconds };
  });
}

function readMetric(value) {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null;
}

function readCoordinate(value) {
  return value && Number.isFinite(value.lat) && Number.isFinite(value.lng)
    ? { lat: value.lat, lng: value.lng }
    : null;
}

function coordinateMatchesOrder(actual, expected, candidates, expectedIndex) {
  if (!expected) return false;
  if (
    Math.abs(actual.lat - expected.lat) > ENDPOINT_TOLERANCE_DEGREES ||
    Math.abs(actual.lng - expected.lng) > ENDPOINT_TOLERANCE_DEGREES
  ) {
    return false;
  }
  const expectedDistance = coordinateDistanceSquared(actual, expected);
  return candidates.every(
    (candidate, index) =>
      index === expectedIndex ||
      sameCoordinate(candidate, expected) ||
      coordinateDistanceSquared(actual, candidate) >= expectedDistance,
  );
}

function coordinateDistanceSquared(left, right) {
  return (left.lat - right.lat) ** 2 + (left.lng - right.lng) ** 2;
}

function sameCoordinate(left, right) {
  return left.lat === right.lat && left.lng === right.lng;
}

function formatCoordinate(point) {
  return `${point.lat},${point.lng}`;
}

function createEndpoint(baseUrl) {
  try {
    return new URL('/v2/direction', baseUrl);
  } catch {
    throw new LiveGateError('GOONG_BASE_URL_INVALID');
  }
}

function parsePositiveInteger(value, label) {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new LiveGateError(`${label}_INVALID`);
  }
  return parsed;
}

function calculatePercentile(values, percentile) {
  if (values.length === 0) return Number.NaN;
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.max(0, Math.ceil(sorted.length * percentile) - 1)];
}

function formatReport(report) {
  return [
    'GOONG_LIVE_GATE=PASS',
    `routes=${report.routes}`,
    `multipointRoutes=${report.multipointRoutes}`,
    `requests=${report.requests}`,
    `legs=${report.legs}`,
    'http200Valid=100%',
    `p95Ms=${report.p95Ms}`,
    `timeoutMs=${report.timeoutMs}`,
  ].join(' ');
}

function formatFailure(error) {
  const safeError =
    error instanceof LiveGateError ? error : new LiveGateError('UNEXPECTED_FAILURE');
  const safeFields = Object.entries(safeError.context)
    .filter(([, value]) => typeof value === 'number' && Number.isFinite(value))
    .map(([key, value]) => `${key}=${value}`);
  return ['GOONG_LIVE_GATE=FAIL', `code=${safeError.code}`, ...safeFields].join(' ');
}

export async function runCli({
  argv = process.argv.slice(2),
  env = process.env,
  stdout = (line) => console.log(line),
  stderr = (line) => console.error(line),
  readFixture = (fixturePath) => JSON.parse(readFileSync(fixturePath, 'utf8')),
  fetchImpl = fetch,
} = {}) {
  try {
    const options = parseArguments(argv);
    const apiKey = env.GOONG_API_KEY?.trim();
    if (!apiKey) throw new LiveGateError('GOONG_API_KEY_MISSING');
    const fixture = readFixture(options.fixturePath);
    const report = await runGoongLiveGate({
      fixture,
      apiKey,
      baseUrl: env.GOONG_BASE_URL || DEFAULT_BASE_URL,
      maxDestinations: env.GOONG_MAX_DESTINATIONS_PER_REQUEST || String(DEFAULT_MAX_DESTINATIONS),
      minimumRoutes: options.minimumRoutes,
      minimumMultipointRoutes: options.minimumMultipointRoutes,
      timeoutMs: options.timeoutMs,
      fetchImpl,
    });
    stdout(formatReport(report));
    return 0;
  } catch (error) {
    stderr(formatFailure(error));
    return 1;
  }
}

const isMain = process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url;
if (isMain) process.exitCode = await runCli();
