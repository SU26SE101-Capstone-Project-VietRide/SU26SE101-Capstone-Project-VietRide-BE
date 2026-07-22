import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {
  assertApiEnvelope,
  assertCargoEvent,
  assertExactObjectKeys,
  assertIsoTimestamp,
} from './run-day29-sprint4-e2e.mjs';

test('day29 runner assertions', () => {
  const timestamp = '2026-07-22T10:00:00.000Z';
  const success = {
    status: 200,
    body: {
      success: true,
      statusCode: 200,
      data: { parcelId: '11111111-1111-4111-8111-111111111111' },
      meta: { traceId: 'day29-test-trace', timestamp },
    },
  };
  const error = {
    status: 403,
    body: {
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN', message: 'Forbidden' },
      meta: { traceId: 'day29-test-trace', timestamp },
    },
  };
  assert.equal(assertApiEnvelope(success, 200, 'success helper'), success.body);
  assert.equal(assertApiEnvelope(error, 403, 'error helper', 'FORBIDDEN'), error.body);
  assert.throws(
    () => assertApiEnvelope({ ...success, body: { ...success.body, statusCode: 201 } }, 200, 'bad'),
    /statusCode mismatch/,
  );
  assert.throws(
    () =>
      assertApiEnvelope(
        { ...error, body: { ...error.body, success: true } },
        403,
        'bad',
        'FORBIDDEN',
      ),
    /success mismatch/,
  );
  assert.throws(
    () =>
      assertApiEnvelope(
        { ...error, body: { ...error.body, error: { code: 'FORBIDDEN' } } },
        403,
        'bad',
        'FORBIDDEN',
      ),
    /error\.message missing/,
  );
  assertIsoTimestamp(timestamp, 'timestamp helper');
  assert.throws(() => assertIsoTimestamp('not-a-date', 'bad timestamp'), /ISO-8601/);
  assertExactObjectKeys({ a: 1, b: 2 }, ['b', 'a'], 'exact-key helper');
  assert.throws(
    () => assertExactObjectKeys({ a: 1, extra: 2 }, ['a'], 'bad keys'),
    /expected keys/,
  );

  const cargo = {
    eventId: '22222222-2222-4222-8222-222222222222',
    occurredAt: timestamp,
    tripId: '33333333-3333-4333-8333-333333333333',
    operatorId: '44444444-4444-4444-8444-444444444444',
    loadedWeightKg: 6,
    maxCargoWeightKg: 7,
    percentFull: 85.71,
  };
  const expectedCargo = {
    tripId: cargo.tripId,
    operatorId: cargo.operatorId,
    loadedWeightKg: 6,
    maxCargoWeightKg: 7,
    percentFull: 85.71,
  };
  assert.doesNotThrow(() => assertCargoEvent(cargo, expectedCargo));
  assert.throws(() => assertCargoEvent({ ...cargo, extra: true }, expectedCargo), /expected keys/);
  assert.throws(
    () => assertCargoEvent({ ...cargo, percentFull: '85.71' }, expectedCargo),
    /must be numeric/,
  );

  const source = fs.readFileSync(new URL('./run-day29-sprint4-e2e.mjs', import.meta.url), 'utf8');
  for (const marker of [
    'assertApiEnvelope(result, status, label, code)',
    'assertCargoEvent(cargoOutbox.payload',
    'RabbitMQ MessageId/Outbox id mismatch',
    'foreign-tenant assistant denied',
    'unassigned driver cannot start trip',
    'wrong-stop unload rejected',
    'completedByUserId mismatch',
    'parcel.parcel.loaded:${row.id}',
    'parcel.parcel.unloaded:${unloadOutbox.id}',
    'Assertions passed:',
  ])
    assert.equal(
      source.includes(marker),
      true,
      `missing Day-29 runner assertion marker: ${marker}`,
    );
  assert.equal(
    source.includes('POST /v1/operator/trips'),
    false,
    'runner must not call public/manual Trip-create',
  );
  assert.equal(source.includes('console.log(driver'), false, 'runner must not print credentials');
});
