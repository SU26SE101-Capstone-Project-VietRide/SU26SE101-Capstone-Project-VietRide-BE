import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const day39 = fs.readFileSync('scripts/run-day39-driver-ops-e2e.mjs', 'utf8');
const day40 = fs.readFileSync('scripts/run-day40-admin-reports-e2e.mjs', 'utf8');
const day39Compose = fs.readFileSync('infra/docker/docker-compose.day39-e2e.yml', 'utf8');
const day40Compose = fs.readFileSync('infra/docker/docker-compose.day40-e2e.yml', 'utf8');

test('Day 39 uses the current Parcel schema and synchronous Trip arrival snapshots', () => {
  for (const column of [
    'estimated_size_category',
    'estimated_length_cm',
    'estimated_width_cm',
    'estimated_height_cm',
    'estimated_volume_m3',
    'estimated_dim_weight_kg',
    'estimated_chargeable_weight_kg',
  ]) {
    assert.match(day39, new RegExp(`\\b${column}\\b`));
  }

  const preAnchor = day39.indexOf('Parcel unload rejects missing stop and destination anchors');
  const tripAnchor = day39.indexOf("idemKey('parcel-stop-anchor')");
  const successfulUnload = day39.indexOf("[ids.stopParcel, 'unload-stop']");
  assert.ok(preAnchor > 0 && tripAnchor > preAnchor && successfulUnload > tripAnchor);
  assert.match(day39, /DROP_OFF_STOP_NOT_ARRIVED/);
  assert.match(day39, /DESTINATION_TERMINAL_NOT_ARRIVED/);
  assert.match(day39, /\[ids\.terminalParcel, 'unload-terminal'\]/);
  assert.match(day39, /Arrival fixtures are not assigned to the expected DRIVER and ASSISTANT/);
  assert.match(day39, /Unassigned Assistant stop arrival was not forbidden/);
  assert.match(day39, /Cross-tenant Assistant stop arrival was not forbidden/);
  assert.match(day39Compose, /day39-e2e\.firebasestorage\.app/);
  assert.match(day39, /incidentPhotoUrl\(ids\.assistant, 'support\.jpg'\)/);
  assert.match(day39, /FROM parcel_delivery_tokens/);
  assert.match(day39, /token_hash ~ '\^\[0-9a-f\]\{64\}\$'/);
  assert.doesNotMatch(day39, /SELECT delivery_token FROM parcels/);
  assert.doesNotMatch(day39, /^\s*delivery_token(?:_expires_at|_revoked_at)?:/m);
});

test('Day 40 publishes valid Inbox identities and proves rollback plus broker retry', () => {
  assert.match(day40, /transportId = payload\?\.eventId/);
  assert.match(day40, /transportId === payload\?\.eventId/);
  assert.doesNotMatch(
    day40,
    /publish\('trip\.station\.merged', payload(?:AB|BC)?, randomUUID\(\)\)/,
  );
  assert.match(day40, /consumer_name='booking\.station-merged'/);
  assert.match(
    day40,
    /Transient Station lock-plan drift rolls back then retries the true Inbox path/,
  );
  assert.match(day40, /booking\.station-merged\.retry/);
  assert.match(day40, /wait_event='advisory'/);
  assert.match(
    day40,
    /Transient Station event partially committed its domain write or Inbox marker/,
  );
  assert.match(day40, /Broker retry did not commit the Station redirect and Inbox marker together/);
  assert.match(day40, /source_event_id='\$\{driftEventId\}'/);
});

test('Day 40 fixtures and assertions exercise Day 42 reconciliation and canonical 503', () => {
  assert.match(day40, /estimated_size_category/);
  assert.match(day40, /INSERT INTO operator_ledger_entries/);
  assert.match(day40, /DELETE FROM platform_booking_stats/);
  assert.match(day40, /SELECT rebuild_platform_booking_stats\(\)/);
  assert.match(day40, /expectError\(mismatch, 503, 'UPSTREAM_UNAVAILABLE'\)/);
  assert.match(day40, /expectError\(unavailable, 503, 'UPSTREAM_UNAVAILABLE'\)/);
  assert.match(day40, /Unreconciled platform report was incorrectly promoted to cache/);
  assert.match(day40, /Payment ledger reconciliation consumer timeout/);
  assert.doesNotMatch(day40, /expectError\(unavailable, 502, 'UPSTREAM_UNAVAILABLE'\)/);
});

test('Days 39 and 40 tear down only invocation-owned Compose resources', () => {
  for (const [source, composeSource, day] of [
    [day39, day39Compose, '39'],
    [day40, day40Compose, '40'],
  ]) {
    assert.ok(source.includes(`const composeProject = \`day${day}-e2e-\${invocationId}\`;`));
    assert.match(source, /'-p',\s*composeProject/);
    assert.equal((source.match(/'down', '-v', '--remove-orphans'/g) ?? []).length, 1);
    assert.match(source, /if \(stackOwned\)/);
    assert.match(composeSource, new RegExp(`DAY${day}_CONTAINER_PREFIX`));
    assert.match(composeSource, new RegExp(`DAY${day}_COMPOSE_PROJECT`));
  }
});
