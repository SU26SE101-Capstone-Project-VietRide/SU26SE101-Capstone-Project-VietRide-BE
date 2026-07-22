import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  PARCEL_STATES,
  POLLING,
  REQUIRED_OUTBOX,
  TRIP_STATES,
  buildRedactedSummary,
  buildTimeAdvanceSql,
  buildTripIdPredicate,
  chooseTargetSchedule,
  idempotencyRedisKeys,
} from './run-day30-sprint4-demo.mjs';

test('day30 runner contract', () => {
  const fixtureNow = new Date('2026-07-22T10:15:00.000Z');
  const target = chooseTargetSchedule(fixtureNow);
  assert.equal(target.targetDate, '2026-07-23');
  assert.equal(target.dayOfWeek, 4);
  assert.equal(target.departureTime, '12:00:00');
  assert.ok(target.departureDateTime.getTime() > fixtureNow.getTime() + 30 * 60 * 1000);
  assert.ok(target.departureDateTime.getTime() <= fixtureNow.getTime() + 14 * 86_400_000);

  const timeSql = buildTimeAdvanceSql(
    '10000000-0000-4000-8000-000000000001',
    '10000000-0000-4000-8000-000000000002',
    fixtureNow,
  );
  const setClause = timeSql.match(/SET\s+([\s\S]*?)\nWHERE/)?.[1];
  assert.equal(setClause, "departure_date_time='2026-07-22T10:44:00.000Z'");
  assert.match(timeSql, /AND status='SCHEDULED'/);
  assert.match(timeSql, /AND source='AUTO_FROM_SCHEDULE'/);
  assert.doesNotMatch(setClause, /status|actor|outbox|idempotency/i);

  const rawKey = '10000000-0000-4000-8000-000000000003';
  const redisKeys = idempotencyRedisKeys('trip', rawKey);
  assert.equal(redisKeys.length, 2);
  assert.ok(redisKeys.every((key) => key.startsWith('trip:idem:v2:')));
  assert.ok(redisKeys.every((key) => !key.includes(rawKey)));

  assert.equal(buildTripIdPredicate('trip_id', []), 'FALSE');
  assert.equal(
    buildTripIdPredicate('trip_id', [
      '10000000-0000-4000-8000-000000000004',
      '10000000-0000-4000-8000-000000000005',
    ]),
    "trip_id IN ('10000000-0000-4000-8000-000000000004','10000000-0000-4000-8000-000000000005')",
  );

  const outboxCounts = Object.fromEntries(REQUIRED_OUTBOX.map((eventType) => [eventType, 1]));
  const duplicateCounts = Object.fromEntries(REQUIRED_OUTBOX.map((eventType) => [eventType, 0]));
  const summary = buildRedactedSummary({
    failureInjection: false,
    cleanupResidue: 0,
    outboxCounts,
    duplicateCounts,
    replayCount: 1,
    duplicateTransitionCount: 0,
    preAdvanceBeyondThirtyMinutes: true,
  });
  assert.deepEqual(summary.tripStates, TRIP_STATES);
  assert.deepEqual(summary.parcelStates, PARCEL_STATES);
  assert.deepEqual(summary.polling, POLLING);
  assert.equal(summary.redacted, true);
  assert.equal(summary.autoFromSchedule, true);
  assert.equal(summary.duplicateOutboxCount, 0);

  const source = fs.readFileSync(new URL('./run-day30-sprint4-demo.mjs', import.meta.url), 'utf8');
  assert.doesNotMatch(source, /INSERT\s+INTO\s+vietride_trip\.trips/i);
  assert.match(source, /http:\/\/localhost:3000/);
  assert.match(source, /try\s*{/);
  assert.match(source, /finally\s*{/);
  assert.match(source, /DAY30_REDACTED_SUMMARY=/);
  assert.match(source, /generatedTripIds = new Set/);
  assert.match(source, /for \(const trip of trips\)/);
  assert.match(
    source,
    /assert\(typeof schedule\.id === 'string'[\s\S]*?generatedScheduleId = schedule\.id;[\s\S]*?assert\(schedule\.operatorId/,
  );
  assert.match(source, /function discoverGeneratedTripIds/);
  assert.match(source, /schedulePredicate\('driver_schedule_id'\)/);
  assert.match(source, /vehicle_types WHERE id=/);
  assert.match(source, /const phones = Object.freeze/);

  const liveWrapper = fs.readFileSync(
    new URL('./run-day30-sprint4-demo-live-wrapper.mjs', import.meta.url),
    'utf8',
  );
  assert.match(liveWrapper, /1200000|1_200_000/);
  assert.match(liveWrapper, /stdout/);
  assert.match(liveWrapper, /stderr/);
  assert.match(liveWrapper, /DAY30_FAILURE_INJECTION=EXECUTED/);
  assert.match(liveWrapper, /DAY30_RUN=PASS/);
  assert.match(liveWrapper, /process\.env\.ComSpec/);
  assert.match(liveWrapper, /npm\.cmd run e2e:day30/);
});
