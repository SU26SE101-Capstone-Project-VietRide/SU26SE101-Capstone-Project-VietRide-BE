import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {
  assertGatewayOnlyUrl,
  assertPostmanArtifacts,
  classifyScheduleChange,
  day23ErrorMatrix,
  isUuidV4,
  runIsolatedGatewayJourney,
  settleInFlightBeforeCleanup,
} from './run-day23-schedule-change-local.mjs';

test('severity boundaries use absolute delta and Asia/Ho_Chi_Minh calendar dates', () => {
  const base = '2026-07-20T01:00:00.000Z';
  assert.equal(classifyScheduleChange(base, '2026-07-20T03:00:00.000Z'), 'MINOR');
  assert.equal(classifyScheduleChange(base, '2026-07-20T03:00:00.001Z'), 'MEDIUM');
  assert.equal(classifyScheduleChange(base, '2026-07-20T06:59:59.999Z'), 'MEDIUM');
  assert.equal(classifyScheduleChange(base, '2026-07-20T07:00:00.000Z'), 'MAJOR');
  assert.equal(classifyScheduleChange('2026-07-20T16:30:00.000Z', '2026-07-20T17:30:00.000Z'), 'MAJOR');
});

test('UUID-v4 guard accepts runtime keys and rejects placeholders/non-v4 values', () => {
  assert.equal(isUuidV4('23000000-0000-4000-8000-000000000001'), true);
  assert.equal(isUuidV4('23000000-0000-3000-8000-000000000001'), false);
  assert.equal(isUuidV4('{{day23ResolveKey}}'), false);
});

test('Gateway fence rejects direct-service, internal, clock/job, and alias paths', () => {
  assert.equal(assertGatewayOnlyUrl('{{baseUrl}}/v1/operator/driver-schedules/23000000-0000-4000-8000-000000000001?applyTo=ALL_PENDING'), true);
  assert.throws(() => assertGatewayOnlyUrl('http://localhost:5002/v1/operator/driver-schedules/x'));
  assert.throws(() => assertGatewayOnlyUrl('{{baseUrl}}/internal/v1/jobs/run'));
  assert.throws(() => assertGatewayOnlyUrl('{{baseUrl}}/v1/operator/trips/x/schedule'));
  assert.throws(() => assertGatewayOnlyUrl('{{baseUrl}}/v1/bookings/b/pending-actions/a/reject'));
});

test('decision-complete resolver error matrix is represented', () => {
  assert.equal(day23ErrorMatrix.length, 12);
  assert.deepEqual(Object.fromEntries(day23ErrorMatrix), {
    AUTH_TOKEN_INVALID: 401, FORBIDDEN: 403, BOOKING_NOT_FOUND: 404,
    BOOKING_PENDING_ACTION_NOT_FOUND: 404, BOOKING_PENDING_ACTION_NOT_RESOLVABLE: 409,
    BOOKING_PENDING_ACTION_SUPERSEDED: 409, BOOKING_PENDING_ACTION_ALREADY_RESOLVED: 409,
    BOOKING_PENDING_ACTION_EXPIRED: 409, IDEMPOTENCY_REQUEST_PENDING: 409,
    IDEMPOTENCY_KEY_REQUIRED: 422, IDEMPOTENCY_KEY_MISMATCH: 422, VALIDATION_ERROR: 422,
  });
});

test('Postman artifacts expose only the canonical Day-23 Gateway journey', () => {
  assert.doesNotThrow(() => assertPostmanArtifacts());
});

test('runtime lifecycle cleans resources after setup and journey success', async () => {
  const calls = [];
  const state = { recorded: [] };
  await runIsolatedGatewayJourney({
    state,
    setup: async (owned) => { owned.recorded.push('fixture'); calls.push('setup'); },
    execute: async () => calls.push('execute'),
    cleanup: async (owned) => { calls.push(`cleanup:${owned.recorded.length}`); owned.recorded.length = 0; },
    assertClean: async (owned) => { calls.push('assertClean'); assert.equal(owned.recorded.length, 0); },
  });
  assert.deepEqual(calls, ['setup', 'execute', 'cleanup:1', 'assertClean']);
});

test('partial setup failure still cleans every resource recorded before failure', async () => {
  const calls = [];
  const state = { recorded: [] };
  await assert.rejects(runIsolatedGatewayJourney({
    state,
    setup: async (owned) => { owned.recorded.push('operator', 'trip'); calls.push('setup-partial'); throw new Error('setup failed'); },
    execute: async () => calls.push('execute'),
    cleanup: async (owned) => { calls.push(`cleanup:${owned.recorded.join(',')}`); owned.recorded.length = 0; },
    assertClean: async (owned) => { calls.push('assertClean'); assert.equal(owned.recorded.length, 0); },
  }), /setup failed/);
  assert.deepEqual(calls, ['setup-partial', 'cleanup:operator,trip', 'assertClean']);
});

test('journey and cleanup failures are both retained in AggregateError', async () => {
  await assert.rejects(runIsolatedGatewayJourney({
    state: {},
    setup: async () => {},
    execute: async () => { throw new Error('journey failed'); },
    cleanup: async () => { throw new Error('cleanup failed'); },
    assertClean: async () => {},
  }), (error) => error instanceof AggregateError && error.errors.length === 2);
});

test('pending probe uses an explicit lock handshake with bounded cleanup', () => {
  const source = fs.readFileSync(new URL('./run-day23-schedule-change-local.mjs', import.meta.url), 'utf8');
  assert.equal(source.includes('pg_sleep'), false);
  for (const marker of ['LOCK_ACQUIRED', 'LOCK_RELEASED', 'ROLLBACK', 'pending probe locker exit', 'settleInFlightBeforeCleanup']) {
    assert.equal(source.includes(marker), true, `missing pending probe marker ${marker}`);
  }
});

test('faulted pending probe settles its in-flight request before outer cleanup starts', async () => {
  const calls = [];
  let settleRequest;
  const inFlight = new Promise((resolve) => { settleRequest = resolve; });
  await assert.rejects(runIsolatedGatewayJourney({
    state: {},
    setup: async () => {},
    execute: async () => {
      await settleInFlightBeforeCleanup(inFlight, () => {
        calls.push('abort');
        setImmediate(() => { calls.push('request-settled'); settleRequest(); });
      }, 0);
      calls.push('probe-failed');
      throw new Error('injected pending-probe failure');
    },
    cleanup: async () => calls.push('cleanup'),
    assertClean: async () => calls.push('assertClean'),
  }), /injected pending-probe failure/);
  assert.deepEqual(calls, ['abort', 'request-settled', 'probe-failed', 'cleanup', 'assertClean']);
});
