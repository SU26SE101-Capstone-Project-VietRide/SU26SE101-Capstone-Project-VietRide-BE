// Day-24 focused evidence companion. Recurring behavior is executed only by the two named
// PostgreSQL TestServer fixtures; this helper validates their deterministic seams and the
// deliberately bounded Postman/static evidence without starting a scheduler or live journey.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const root = process.cwd();
const collectionPath = 'docs/api/postman/vietride.postman_collection.json';
const environmentPath = 'docs/api/postman/vietride.local.postman_environment.json';
const evidencePath = 'docs/handoff/day-24-evidence.md';

export const focusedEnvironment = Object.freeze({
  DAY24_EVIDENCE_MODE: 'FOCUSED_INTEGRATION',
  DAY24_FIXTURE_MODE: 'ISOLATED_TESTSERVER',
  DAY24_FROZEN_CLOCK_UTC: '2026-07-18T12:00:00Z',
  DAY24_JOB_TRIGGER: 'DIRECT',
  DAY24_POLL_ATTEMPTS: '20',
  DAY24_POLL_INTERVAL_MS: '100',
});

export const namedBookingFixtures = Object.freeze([
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day24StopDisabledAutoFallbackIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day24NoShowDetectionIntegrationTests.cs',
]);

export function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), 'utf8');
}

function readJson(relativePath) {
  return JSON.parse(read(relativePath));
}

function flattenRequests(items, result = []) {
  for (const item of items ?? []) {
    if (item.request) result.push(item);
    flattenRequests(item.item, result);
  }
  return result;
}

function requestUrl(request) {
  return typeof request.url === 'string' ? request.url : request.url?.raw ?? '';
}

export function assertFocusedEnvironment(env = process.env) {
  for (const [key, expected] of Object.entries(focusedEnvironment)) {
    assert(env[key] === expected, `${key} must equal ${expected}`);
  }
  const attempts = Number(env.DAY24_POLL_ATTEMPTS);
  const interval = Number(env.DAY24_POLL_INTERVAL_MS);
  assert(Number.isInteger(attempts) && attempts > 0, 'DAY24_POLL_ATTEMPTS must be positive');
  assert(Number.isInteger(interval) && interval > 0, 'DAY24_POLL_INTERVAL_MS must be positive');
  assert(attempts * interval <= 2_000, 'Day-24 notification poll must remain bounded to 2 seconds');
  return true;
}

export function assertPublicGatewayUrl(url) {
  const parsed = new URL(url.replace('{{baseUrl}}', 'http://localhost:3000'));
  assert(parsed.origin === 'http://localhost:3000', `Public request bypasses Gateway: ${url}`);
  assert(parsed.pathname.startsWith('/v1/'), `Public request is outside /v1: ${url}`);
  assert(!parsed.pathname.startsWith('/internal/'), `Internal route exposed through Gateway: ${url}`);
  return true;
}

export function assertRawInternalUrl(url) {
  const parsed = new URL(url.replace('{{bookingBaseUrl}}', 'http://localhost:5003'));
  assert(parsed.origin === 'http://localhost:5003', `Raw request must target Booking: ${url}`);
  assert(/^\/internal\/v1\/bookings\/trips\/[^/]+\/stops\/[^/]+\/pending-passenger-count$/.test(parsed.pathname), `Unexpected raw internal route: ${url}`);
  assert(parsed.searchParams.has('operatorId'), `Raw pending count lacks operatorId: ${url}`);
  return true;
}

export function assertPostmanArtifacts() {
  const collection = readJson(collectionPath);
  const environment = readJson(environmentPath);
  const folder = collection.item.find((item) => item.name === 'Day 24 - Stop disable and no-show evidence');
  assert(folder, 'Postman Day-24 folder is missing');
  const requests = flattenRequests(folder.item);
  assert(requests.length >= 12, 'Postman Day-24 matrix is incomplete');
  for (const item of requests) {
    const url = requestUrl(item.request);
    if (url.includes('{{bookingBaseUrl}}')) assertRawInternalUrl(url);
    else assertPublicGatewayUrl(url);
  }

  const serialized = JSON.stringify(folder);
  for (const marker of [
    'STOP_ALREADY_DISABLED', 'BOOKING_PENDING_ACTION_ALREADY_RESOLVED',
    'IDEMPOTENCY_KEY_MISMATCH', 'TRIP_STOP_ALREADY_DEPARTED',
    'TRIP_STOP_NOT_ARRIVED', 'AUTH_TOKEN_INVALID', 'VALIDATION_ERROR',
    'pendingPassengerCount', 'eventEmitted',
  ]) assert(serialized.includes(marker), `Postman assertion missing ${marker}`);

  const keys = new Map(environment.values.map((item) => [item.key, item.value]));
  for (const key of [
    'bookingBaseUrl', 'day24OperatorAdminAccessToken', 'day24PassengerAccessToken',
    'day24DriverAccessToken', 'day24InternalJwt', 'day24StopId', 'day24TripId',
    'day24TripStopId', 'day24BookingId', 'day24PendingActionId',
  ]) assert(keys.has(key), `Postman environment key missing ${key}`);
  for (const key of ['day24OperatorAdminAccessToken', 'day24PassengerAccessToken', 'day24DriverAccessToken', 'day24InternalJwt']) {
    assert(keys.get(key) === '', `Committed credential value is forbidden: ${key}`);
  }
  return requests.length;
}

export function assertNamedBookingFixtures() {
  const [fallbackPath, noShowPath] = namedBookingFixtures;
  const fallback = read(fallbackPath);
  const noShow = read(noShowPath);
  for (const [name, source] of [['fallback', fallback], ['no-show', noShow]]) {
    assert(source.includes('IClock'), `${name} fixture does not freeze IClock`);
    assert(source.includes('ExecuteAsync(CancellationToken.None)'), `${name} fixture does not invoke the job directly`);
    assert(!source.includes('Task.Delay('), `${name} fixture waits on wall-clock scheduling`);
  }
  for (const marker of ['EqualityIsUntouched', 'deadline.AddMinutes(5)', 'OutboxEvents', 'AUTO_FALLBACK_DESTINATION']) {
    assert(fallback.includes(marker), `Fallback fixture marker missing ${marker}`);
  }
  for (const marker of ['CoversAllPendingAndMixedThreeOfFive', 'AllBoardedFiveOfFive', 'NO_SHOW', 'PARTIAL_NO_SHOW', 'MarkNoShow', 'UpstreamFailure_FailsClosed']) {
    assert(noShow.includes(marker), `No-show fixture marker missing ${marker}`);
  }
  const fixture = read('apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day24StopDisabledAutoFallbackFixture.cs');
  assert(fixture.includes('vietride_booking_day24_{Guid.NewGuid():N}'), 'Booking fixture database lacks the exact UUID-suffixed Day-24 prefix');
  assert(fixture.includes('CreateDatabaseAsync') && fixture.includes('DropDatabaseAsync'), 'Booking fixture lacks isolated create/drop lifecycle');
  return true;
}

export function assertEvidenceTraceability() {
  const evidence = read(evidencePath);
  for (const marker of [
    'FOCUSED_INTEGRATION', 'Day24StopDisabledAutoFallbackIntegrationTests',
    'Day24NoShowDetectionIntegrationTests', 'deadline < now', '3/5', '5/5',
    'Idempotency-Key', 'pendingPassengerCount', 'TRIP_STOP_ALREADY_DEPARTED',
    'eventId == OutboxEvent.Id == RabbitMQ MessageId', 'maximum 2 seconds',
    'does not claim a live cross-service journey', 'does not claim full regression',
  ]) assert(evidence.includes(marker), `Evidence document marker missing ${marker}`);
  return true;
}

export async function boundedPoll(probe, predicate, {
  attempts = Number(process.env.DAY24_POLL_ATTEMPTS ?? 20),
  intervalMs = Number(process.env.DAY24_POLL_INTERVAL_MS ?? 100),
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
} = {}) {
  assert(Number.isInteger(attempts) && attempts > 0, 'poll attempts must be positive');
  assert(Number.isInteger(intervalMs) && intervalMs > 0, 'poll interval must be positive');
  assert(attempts * intervalMs <= 2_000, 'poll exceeds the 2-second Day-24 bound');
  let last;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    last = await probe(attempt);
    if (predicate(last)) return last;
    if (attempt + 1 < attempts) await sleep(intervalMs);
  }
  throw new Error(`Day-24 bounded poll exhausted after ${attempts} attempts`);
}

export function runFocusedIntegrationChecks(env = process.env) {
  assertFocusedEnvironment(env);
  assertNamedBookingFixtures();
  const requestCount = assertPostmanArtifacts();
  assertEvidenceTraceability();
  console.log('PASS | named Booking frozen-clock fixtures use direct job invocation and isolated database cleanup');
  console.log(`PASS | Day-24 Postman boundary matrix (${requestCount} requests)`);
  console.log('PASS | focused evidence traceability and 2-second notification poll bound');
}

async function main() {
  const args = process.argv.slice(2);
  for (const arg of args) assert(['--focused-integration', '--help', '-h'].includes(arg), `Unknown argument: ${arg}`);
  if (args.includes('--help') || args.includes('-h')) {
    console.log('Usage: node scripts/day24-stop-noshow-e2e.mjs --focused-integration');
    return;
  }
  assert(args.includes('--focused-integration'), 'Use --focused-integration; full regression belongs to /audit-day 24');
  runFocusedIntegrationChecks();
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) await main();
