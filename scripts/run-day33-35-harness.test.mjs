import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const day33 = fs.readFileSync('scripts/run-day33-trip-disruption-e2e.mjs', 'utf8');
const day34 = fs.readFileSync('scripts/run-day34-vehicle-substitution-e2e.mjs', 'utf8');
const postman = JSON.parse(
  fs.readFileSync('docs/api/postman/vietride.postman_collection.json', 'utf8'),
);

test('Day33 uses current Parcel derived fields and proves mixed pickup classification', () => {
  for (const requiredField of [
    'estimated_size_category',
    'estimated_length_cm',
    'estimated_width_cm',
    'estimated_height_cm',
    'estimated_volume_m3',
    'estimated_dim_weight_kg',
    'estimated_chargeable_weight_kg',
  ]) {
    assert.match(day33, new RegExp(`\\b${requiredField}\\b`));
  }

  assert.match(day33, /retainedRouteBooking/);
  assert.match(day33, /terminalRouteBooking/);
  assert.match(day33, /value === `1\|0\|0\|/);
  assert.match(day33, /type = 'TRIP_ROUTE_CHANGED'/);
  assert.match(day33, /value === '3\|1\|0\|4'/);
  assert.match(day33, /countPublishingOwnedOutbox/);
  assert.match(day33, /integration_inbox WHERE message_id IN/);
  assert.doesNotMatch(day33, /vietride_payment\.integration_inbox/);
  assert.match(day33, /notification:idem:\*:\$\{messageId\}/);
  assert.match(day33, /stablePasses === 3/);
  assert.match(day33, /DELETE FROM vietride_parcel\.parcel_status_history WHERE parcel_id/);
  assert.match(day33, /SET LOCAL session_replication_role = replica/);
  assert.match(day33, /trg_parcel_status_history_immutable/);
});

test('Days34-35 prove empty replacement, conservation, replay, escalation, and cleanup', () => {
  assert.match(day34, /replacement cargo starts empty/);
  assert.match(day34, /source plus target cargo conservation after confirmation/);
  assert.match(day34, /confirmed cargo ledger topology and replay no-op/);
  assert.match(day34, /escalation retains source cargo/);
  assert.match(day34, /Day34-35 fixture cleanup verified/);
  assert.match(day34, /countPublishingOwnedOutbox/);
  assert.match(day34, /notification:idem:\*:\$\{messageId\}/);
  assert.match(day34, /stablePasses !== 3/);
  assert.match(day34, /SET LOCAL session_replication_role = replica/);
  assert.match(day34, /trg_parcel_status_history_immutable/);
  assert.match(day34, /SELECT weight_kg FROM vietride_trip\.trip_cargo_parcels/);

  const day35 = postman.item.find(
    (folder) => folder.name === 'Day35 - Parcel cargo transfer conservation',
  );
  assert.ok(day35, 'Day35 Newman folder must exist');
  assert.equal(day35.item.length, 2);
  assert.equal(day35.item[0].request.method, 'POST');
  assert.equal(day35.item[1].request.method, 'POST');
  assert.equal(
    day35.item[0].request.header.find((header) => header.key === 'Idempotency-Key').value,
    '{{day35ConfirmKey}}',
  );
  assert.equal(
    day35.item[1].request.header.find((header) => header.key === 'Idempotency-Key').value,
    '{{day35ConfirmKey}}',
  );
});

test('runners do not delete Docker containers or volumes', () => {
  for (const source of [day33, day34]) {
    assert.doesNotMatch(source, /docker[^\n]*(?:volume\s+(?:rm|prune)|compose\s+down|-v\b)/i);
    assert.doesNotMatch(source, /execFileSync\(['"]docker['"],\s*\[['"](?:rm|rmi)['"]/i);
  }
});
