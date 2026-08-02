import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  PARCEL_STATES,
  POLLING,
  REQUIRED_OUTBOX,
  TRIP_STATES,
  buildApiFailureDiagnostic,
  buildRedactedSummary,
  buildTimeAdvanceSql,
  buildTripIdPredicate,
  chooseTargetSchedule,
  idempotencyRedisKeys,
} from './run-day30-sprint4-demo.mjs';

test('day30 API failure diagnostic is useful and redacts credentials', () => {
  const diagnostic = buildApiFailureDiagnostic(
    {
      status: 422,
      raw: 'raw response must not be disclosed',
      headers: { Authorization: 'Bearer header-secret' },
      body: {
        success: false,
        statusCode: 422,
        error: {
          code: 'VALIDATION_ERROR',
          message:
            'Invalid request -----BEGIN PRIVATE KEY-----\nmessage-private-secret\n-----END PRIVATE KEY----- Bearer bearer-secret Idempotency-Key: idem-secret password=message-password SECRET:message-secret Token=message-token credential: message-credential eyJabc.def.ghi',
          fields: {
            validFrom: ['Must be today or later'],
            accessToken: 'field-token-secret',
            nested: {
              privateKey: '-----BEGIN PRIVATE KEY-----private-secret-----END PRIVATE KEY-----',
              detail:
                'safe diagnostic detail password=nested-password secret: nested-secret TOKEN=nested-token Credential: nested-credential',
            },
          },
          unexpected: 'must not be disclosed',
        },
        data: { password: 'body-password-secret' },
        meta: { traceId: 'trace-must-not-be-disclosed' },
      },
    },
    201,
    'operator creates DriverSchedule',
  );

  assert.match(diagnostic, /expected HTTP 201, got 422/);
  assert.match(diagnostic, /"code":"VALIDATION_ERROR"/);
  assert.match(
    diagnostic,
    /"message":"Invalid request \[PRIVATE KEY REDACTED\] Bearer \[REDACTED\]/,
  );
  assert.match(diagnostic, /password=\[REDACTED\]/);
  assert.match(diagnostic, /SECRET:\[REDACTED\]/);
  assert.match(diagnostic, /Token=\[REDACTED\]/);
  assert.match(diagnostic, /credential:\[REDACTED\]/);
  assert.match(diagnostic, /"validFrom":\["Must be today or later"\]/);
  assert.match(diagnostic, /"accessToken":"\[REDACTED\]"/);
  assert.match(diagnostic, /"privateKey":"\[REDACTED\]"/);
  assert.match(
    diagnostic,
    /"detail":"safe diagnostic detail password=\[REDACTED\] secret:\[REDACTED\] TOKEN=\[REDACTED\] Credential:\[REDACTED\]"/,
  );
  assert.doesNotMatch(
    diagnostic,
    /BEGIN PRIVATE KEY|END PRIVATE KEY|raw response|header-secret|bearer-secret|idem-secret|eyJabc|field-token-secret|private-secret|message-password|message-secret|message-token|message-credential|nested-password|nested-secret|nested-token|nested-credential|unexpected|body-password|trace-must/,
  );
});

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
  const parcelSchema = fs.readFileSync(
    new URL('../db-schema/parcel/schema.sql', import.meta.url),
    'utf8',
  );
  const parcelTable = parcelSchema.match(/CREATE TABLE parcels \(([\s\S]*?)\n\);/)?.[1];
  assert.ok(parcelTable, 'canonical Parcel table DDL must be discoverable');
  const requiredParcelColumns = parcelTable
    .split(/\r?\n/)
    .filter((line) => /\bNOT NULL\b/.test(line) && !/\bDEFAULT\b/.test(line))
    .map((line) => line.trim().match(/^([a-z_]+)/)?.[1])
    .filter(Boolean);
  const fixtureColumns = source
    .match(/INSERT INTO vietride_parcel\.parcels\s*\(([\s\S]*?)\)\s*VALUES/)?.[1]
    .split(',')
    .map((column) => column.trim());
  assert.ok(fixtureColumns, 'Day30 Parcel fixture column list must be discoverable');
  assert.deepEqual(
    requiredParcelColumns.filter((column) => !fixtureColumns.includes(column)),
    [],
    'Day30 Parcel fixture must cover every canonical NOT NULL column without a database default',
  );
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
  assert.match(
    source,
    /size_category,estimated_size_category,actual_size_category,[\s\S]*?estimated_gross_price_vnd,final_gross_price_vnd,[\s\S]*?'READY_TO_LOAD'/,
    'Day30 Parcel fixture must represent a fully paid and reweighed current-schema load candidate',
  );
  assert.match(source, /function attachReadyToLoadParcel/);
  for (const cleanupTable of [
    'parcel_delivery_tokens',
    'parcel_cargo_recovery_operations',
    'platform_parcel_stats',
    'parcel_status_history',
    'parcel_stats',
  ]) {
    assert.match(source, new RegExp(`DELETE FROM vietride_parcel\\.${cleanupTable}`));
    assert.match(
      source,
      new RegExp(`SELECT count\\(\\*\\) FROM vietride_parcel\\.${cleanupTable}`),
    );
  }
  assert.match(source, /SET LOCAL session_replication_role = replica/);
  assert.match(source, /trg_parcel_status_history_immutable/);
  assert.match(
    source,
    /post\('\/v1\/operator\/driver-schedules', tokens\.operatorAdmin, \{\s*body:/,
    'DriverSchedule create must remain a no-key Day-43 exemption',
  );
  assert.doesNotMatch(
    source,
    /post\('\/v1\/operator\/driver-schedules', tokens\.operatorAdmin, \{\s*key:/,
    'Day30 must not hide a key requirement on the exempt DriverSchedule create contract',
  );

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
