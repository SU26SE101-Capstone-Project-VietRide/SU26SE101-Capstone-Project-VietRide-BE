import assert from 'node:assert/strict';
import test from 'node:test';

import {
  UI25_FOLDER,
  assertPostmanArtifacts,
  buildRuntimeArtifacts,
  ui25RunTimeWindow,
  ui25TimeWindow,
} from './run-ui-gaps-gateway-e2e.mjs';

test('UI-25 cumulative Postman folder covers every public UI-gap route through Gateway', () => {
  const result = assertPostmanArtifacts();

  assert.equal(UI25_FOLDER, 'UI Gaps - Gateway Real Stack');
  assert.ok(result.requestCount >= 41);
  assert.deepEqual(result.nonGatewayUrls, []);
  assert.deepEqual(result.internalUrls, [
    '{{baseUrl}}/internal/v1/reports/platform/bookings?from={{ui25ReportFromUtc}}&to={{ui25ReportToUtc}}',
  ]);
});

test('UI-25 committed environment contains placeholders only for runtime secrets and fixture IDs', () => {
  const result = assertPostmanArtifacts();

  assert.deepEqual(result.nonEmptySensitiveValues, []);
  assert.ok(result.environmentKeys.includes('ui25AdminPolicyId'));
  assert.ok(result.environmentKeys.includes('ui25OperatorPolicyId'));
  assert.ok(result.environmentKeys.includes('ui25FareBatchKey'));
  assert.ok(result.environmentKeys.includes('ui25FareWrongRoleKey'));
  assert.ok(result.environmentKeys.includes('ui25PolicyValidationKey'));
});

test('fixture timestamps stay inside one requested ICT day near midnight', () => {
  for (const now of [new Date('2026-07-29T17:01:00Z'), new Date('2026-07-29T21:59:00Z')]) {
    const time = ui25TimeWindow(now);

    assert.equal(time.currentDate, '2026-07-30');
    assert.equal(time.tomorrowDate, '2026-07-31');
    assert.equal(time.month, '2026-07');
    assert.equal(time.fixtureInstantUtc, '2026-07-30T05:00:00.000Z');
    assert.equal(time.reportFromUtc, '2026-07-29T17:00:00.000Z');
    assert.equal(time.reportToUtc, '2026-07-30T17:00:00.000Z');
  }
});

test('each run gets cache-distinct report bounds that still contain the fixture day', () => {
  const now = new Date('2026-07-29T17:01:00Z');
  const first = ui25RunTimeWindow(now, '00000001-0000-4000-8000-000000000000');
  const second = ui25RunTimeWindow(now, '00000002-0000-4000-8000-000000000000');

  assert.notEqual(first.reportFromUtc, second.reportFromUtc);
  assert.notEqual(first.reportToUtc, second.reportToUtc);
  assert.ok(first.reportFromUtc > '2026-07-29T17:00:00.000Z');
  assert.ok(first.reportToUtc < '2026-07-30T17:00:00.000Z');
  assert.ok(first.reportFromUtc < first.fixtureInstantUtc);
  assert.ok(first.reportToUtc > first.fixtureInstantUtc);
});

test('runtime Postman artifacts are filtered, injected in memory and leave the source objects unchanged', () => {
  const collection = {
    item: [
      { name: 'Unrelated folder', item: [] },
      { name: UI25_FOLDER, item: [{ name: 'Request', request: { url: { raw: '{{baseUrl}}/v1/test' } } }] },
    ],
  };
  const environment = {
    values: [
      { key: 'baseUrl', value: 'http://localhost:3000', enabled: true },
      { key: 'systemAdminAccessToken', value: '', enabled: true },
    ],
  };

  const runtime = buildRuntimeArtifacts(collection, environment, {
    systemAdminAccessToken: 'runtime-token',
  });

  assert.deepEqual(runtime.collection.item.map((item) => item.name), [UI25_FOLDER]);
  assert.equal(
    runtime.environment.values.find((item) => item.key === 'systemAdminAccessToken').value,
    'runtime-token',
  );
  assert.equal(environment.values[1].value, '');
  assert.equal(collection.item.length, 2);
});
