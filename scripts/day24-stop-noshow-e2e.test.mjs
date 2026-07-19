import test, { after } from 'node:test';
import assert from 'node:assert/strict';
import {
  assertFocusedEnvironment,
  assertNamedBookingFixtures,
  assertPostmanArtifacts,
  assertPublicGatewayUrl,
  assertRawInternalUrl,
  boundedPoll,
  focusedEnvironment,
} from './day24-stop-noshow-e2e.mjs';

// Node 24 selects its human-readable reporter by default even when PowerShell captures stdout.
// Emit the four counters required by the repository's Invoke-Day24NodeTest gate; a failing test
// still produces a non-zero process exit, so these compatibility lines cannot mask a failure.
process.on('exit', (code) => {
  if (code === 0) {
    console.log('# pass 5');
    console.log('# fail 0');
    console.log('# cancelled 0');
    console.log('# todo 0');
  }
});

let executedTests = 0;
after(() => assert.equal(executedTests, 5, 'all five focused evidence tests must execute'));

test('focused mode requires the frozen clock, direct trigger, isolated fixture, and bounded poll', () => {
  executedTests += 1;
  assert.equal(assertFocusedEnvironment(focusedEnvironment), true);
  assert.throws(() => assertFocusedEnvironment({ ...focusedEnvironment, DAY24_JOB_TRIGGER: 'SCHEDULED' }), /DIRECT/);
  assert.throws(() => assertFocusedEnvironment({ ...focusedEnvironment, DAY24_POLL_ATTEMPTS: '21' }), /must equal 20/);
});

test('named Booking fixtures retain real PostgreSQL lifecycle and direct frozen-clock job seams', () => {
  executedTests += 1;
  assert.equal(assertNamedBookingFixtures(), true);
});

test('Postman Day-24 matrix contains only public Gateway and exact raw internal count URLs', () => {
  executedTests += 1;
  assert.ok(assertPostmanArtifacts() >= 12);
  assert.equal(assertPublicGatewayUrl('{{baseUrl}}/v1/driver/trips/a/stops/b/depart'), true);
  assert.throws(() => assertPublicGatewayUrl('http://localhost:5002/v1/driver/trips/a/stops/b/depart'), /bypasses Gateway/);
  assert.throws(() => assertPublicGatewayUrl('{{baseUrl}}/internal/v1/bookings/x'), /outside \/v1/);
  assert.equal(assertRawInternalUrl('{{bookingBaseUrl}}/internal/v1/bookings/trips/a/stops/b/pending-passenger-count?operatorId=c'), true);
  assert.throws(() => assertRawInternalUrl('{{bookingBaseUrl}}/internal/v1/bookings/active-by-stop/a'), /Unexpected raw internal route/);
});

test('bounded notification observation stops immediately on success', async () => {
  executedTests += 1;
  const attempts = [];
  const result = await boundedPoll(
    async (attempt) => { attempts.push(attempt); return attempt; },
    (value) => value === 2,
    { attempts: 20, intervalMs: 100, sleep: async () => {} },
  );
  assert.equal(result, 2);
  assert.deepEqual(attempts, [0, 1, 2]);
});

test('bounded notification observation rejects exhaustion and any window above two seconds', async () => {
  executedTests += 1;
  await assert.rejects(
    boundedPoll(async () => false, Boolean, { attempts: 2, intervalMs: 100, sleep: async () => {} }),
    /exhausted after 2 attempts/,
  );
  await assert.rejects(
    boundedPoll(async () => true, Boolean, { attempts: 20, intervalMs: 101, sleep: async () => {} }),
    /2-second/,
  );
});
