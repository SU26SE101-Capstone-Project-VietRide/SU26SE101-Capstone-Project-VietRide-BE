import assert from 'node:assert/strict';
import test from 'node:test';
import { day36StopSnapshotFixtureSql } from './day36-stop-snapshot-fixture.mjs';

const ids = {
  operatorA: '36000000-0000-4000-8000-000000000001',
  stop: '36000000-0000-4000-8000-000000000104',
  mainTrip: '36000000-0000-4000-8000-000000000131',
};

test('seeds an active Stop and the authoritative main TripStop snapshot', () => {
  const sql = day36StopSnapshotFixtureSql(ids);

  assert.match(sql, /INSERT INTO stops/);
  assert.match(sql, new RegExp(`'${ids.stop}','${ids.operatorA}'`));
  assert.match(sql, /is_active=true,\s+deleted_at=NULL/);
  assert.match(sql, /INSERT INTO trip_stops/);
  assert.match(sql, new RegExp(`'${ids.mainTrip}','${ids.stop}',1`));
  assert.match(sql, /'PENDING',true,true/);
});
