import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildParcelSettlementE2ePorts,
  resolveParcelSettlementE2ePorts,
} from './parcel-settlement-e2e-ports.mjs';

test('derives every compose port and service URL from one safe base', () => {
  const ports = buildParcelSettlementE2ePorts(34000);

  assert.equal(ports.env.POSTGRES_PORT, '34000');
  assert.equal(ports.env.PARCEL_PORT, '34008');
  assert.equal(ports.env.GATEWAY_PORT, '34010');
  assert.equal(ports.urls.identity, 'http://localhost:34004');
  assert.equal(ports.urls.parcel, 'http://localhost:34008');
  assert.equal(ports.urls.gateway, 'http://localhost:34010');
  assert.equal(new Set(Object.values(ports.env)).size, 11);
});

test('deterministically advances to the next block when the default block is unavailable', async () => {
  const checkedBlocks = [];
  const ports = await resolveParcelSettlementE2ePorts({}, async (values) => {
    checkedBlocks.push(values);
    return values[0] === 34020;
  });

  assert.deepEqual(
    checkedBlocks.map((values) => values[0]),
    [34000, 34020],
  );
  assert.equal(ports.base, 34020);
  assert.equal(ports.env.PARCEL_PORT, '34028');
});

test('honors an explicit base and fails instead of silently moving it', async () => {
  const available = await resolveParcelSettlementE2ePorts(
    { PARCEL_SETTLEMENT_E2E_PORT_BASE: '36000' },
    async () => true,
  );
  assert.equal(available.base, 36000);

  await assert.rejects(
    resolveParcelSettlementE2ePorts(
      { PARCEL_SETTLEMENT_E2E_PORT_BASE: '36000' },
      async () => false,
    ),
    /configured block starting at 36000/,
  );
});

test('rejects invalid port bases before invoking the availability check', async () => {
  let checked = false;
  await assert.rejects(
    resolveParcelSettlementE2ePorts({ PARCEL_SETTLEMENT_E2E_PORT_BASE: 'not-a-port' }, async () => {
      checked = true;
      return true;
    }),
    /must be an integer/,
  );
  assert.equal(checked, false);
});
