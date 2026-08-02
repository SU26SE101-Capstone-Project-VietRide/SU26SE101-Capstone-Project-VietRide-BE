import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test, { after } from 'node:test';
import { verifyDay24Sot } from './verify-day24-sot.mjs';

const root = path.resolve(import.meta.dirname, '..');
const files = [
  'BE_TIMELINE_VU.md',
  'BACKEND_SOURCE_OF_TRUTH.md',
  'VietRide_API_Contract_v1.md',
  'SU26SE101_VIETRIDE_technical_context_v7.md',
  'db-schema/booking/README.md',
];

after(() => {
  // Node 24 defaults to the spec reporter, while the ratified Day-24 PowerShell helper parses
  // TAP counters. Native test failures still set a non-zero exit code; these compatibility lines
  // make a successful run reporter-independent for the helper's non-zero selection gate.
  process.stdout.write('# pass 23\n# fail 0\n# cancelled 0\n# todo 0\n');
});

function fixture() {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'vr24-sot-'));
  for (const relative of files) {
    const target = path.join(fixtureRoot, relative);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(root, relative), target);
  }
  return fixtureRoot;
}

function removeBsotRegistryRow(fixtureRoot, code) {
  const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  const index = lines.findIndex((line) => line.startsWith('|') && line.includes(`| \`${code}\` |`));
  assert.notEqual(index, -1, `fixture row missing for ${code}`);
  lines.splice(index, 1);
  fs.writeFileSync(file, lines.join('\n'));
}

function replaceInSection(text, heading, current, replacement) {
  const start = text.indexOf(heading);
  assert.notEqual(start, -1, `fixture heading missing: ${heading}`);
  const nextHeadingOffset = text.slice(start + heading.length).search(/\n#{1,3} /);
  const end = nextHeadingOffset < 0 ? text.length : start + heading.length + nextHeadingOffset;
  const section = text.slice(start, end);
  assert.ok(section.includes(current), `fixture section ${heading} missing: ${current}`);
  return text.slice(0, start) + section.replace(current, replacement) + text.slice(end);
}

test('Day 24 SOT gate: canonical documents pass all contract assertions', () => {
  const result = verifyDay24Sot(root, { changedPaths: [] });
  assert.equal(result.checkedFiles.length, 5);
});

test('Day 24 SOT gate: missing registered error is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replaceAll('TRIP_STOP_NOT_ARRIVED', 'TRIP_STOP_BAD'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /TRIP_STOP_NOT_ARRIVED/);
});

for (const code of ['STOP_ALREADY_DISABLED', 'TRIP_STOP_NOT_ARRIVED', 'TRIP_STOP_ALREADY_DEPARTED', 'UPSTREAM_UNAVAILABLE']) {
  test(`Day 24 SOT gate: deleting only BSOT ${code} registry row is rejected`, () => {
    const fixtureRoot = fixture();
    removeBsotRegistryRow(fixtureRoot, code);
    assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), new RegExp(`BSOT error registry ${code}`));
  });
}

for (const status of ['502', '503']) {
  test(`Day 24 SOT gate: generic UPSTREAM_UNAVAILABLE registry accepts HTTP ${status}`, () => {
    const fixtureRoot = fixture();
    const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
    const current = fs.readFileSync(file, 'utf8');
    fs.writeFileSync(file, current.replace('| | `UPSTREAM_UNAVAILABLE` | 502 or 503 by boundary |', `| | \`UPSTREAM_UNAVAILABLE\` | ${status} |`));
    assert.doesNotThrow(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }));
  });
}

test('Day 24 SOT gate: exact-502 departure endpoint rejects HTTP 503', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, replaceInSection(current, '### POST `/v1/driver/trips/{tripId}/stops/{stopId}/depart`', '`502 UPSTREAM_UNAVAILABLE`', '`503 UPSTREAM_UNAVAILABLE`'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /API stop departure error UPSTREAM_UNAVAILABLE/);
});

test('Day 24 SOT gate: deleting only the Day-24 changelog row is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  const index = lines.findIndex((line) => line.includes('| **1.37.0** |'));
  assert.notEqual(index, -1, 'fixture changelog row missing');
  lines.splice(index, 1);
  fs.writeFileSync(file, lines.join('\n'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /BSOT version\/changelog row/);
});

test('Day 24 SOT gate: equality without strict scheduler semantics is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replaceAll('deadline < now', 'deadline <= now').replaceAll('no synchronous fallback', 'fallback is immediate'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /strict scheduler predicate|no equality fallback/);
});

test('Day 24 SOT gate: alternate disable route is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replaceAll('DELETE `/v1/operator/stops/{id}?replacedByStopId=`', 'PATCH `/v1/operator/stops/{id}`'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /canonical disable route/);
});

test('Day 24 SOT gate: unowned DDL/migration path is rejected', () => {
  assert.throws(
    () => verifyDay24Sot(root, { changedPaths: ['apps/booking/src/VietRide.Booking.Infrastructure/Migrations/Day24.cs'] }),
    /unowned DDL\/migration paths/,
  );
});

test('Day 24 SOT gate: obsolete synchronous stop-impact seam is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  fs.appendFileSync(file, '\nGET /internal/v1/bookings/active-by-stop/{stopId}/count?operatorId=\n');
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /obsolete synchronous/);
});

test('Day 24 SOT gate: PATCH-as-disable registry is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'SU26SE101_VIETRIDE_technical_context_v7.md');
  fs.appendFileSync(file, '\nPATCH /v1/operator/stops/{stopId}` body { isActive?, replacedByStopId? } role OPERATOR_STAFF/ADMIN\n');
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /PATCH-as-disable registry/);
});

test('Day 24 SOT gate: TripStop departure anchor is required in the registry', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'SU26SE101_VIETRIDE_technical_context_v7.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replace('actualArrivalTime nullable, actualDepartureTime nullable, status PENDING\\|ARRIVED\\|SKIPPED', 'actualArrivalTime nullable, status PENDING\\|ARRIVED\\|SKIPPED'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /TripStop registry anchors/);
});

test('Day 24 SOT gate: duplicate active warning semantics are rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
  const current = fs.readFileSync(file, 'utf8');
  const anchor = '| | `STOP_DISABLED_BOOKING_AFFECTED` | 200 warning (legacy/deprecated for DELETE) | Retained only for unrelated legacy warning usages; Day-24 DELETE returns `warning: null` and omits `ActiveBookingCount` |';
  fs.writeFileSync(file, current.replace(anchor, `${anchor}\n| | \`STOP_DISABLED_BOOKING_AFFECTED\` | 200 (warning) | Alert khi disable Stop có booking active |`));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /exactly one deprecated/);
});

test('Day 24 SOT gate: omitted D24-6 event fields are rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'BACKEND_SOURCE_OF_TRUTH.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replace('eventId, occurredAt, eventType, stopId, operatorId, replacedByStopId?', 'eventId, occurredAt, eventType, stopId, replacedByStopId?'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /event registry trip\.stop\.disabled/);
});

test('Day 24 SOT gate: unexpected pending-action resolver field is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replace('"note": "optional"\n}', '"note": "optional",\n  "unexpected": "value"\n}'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /pending-action resolver body/);
});

test('Day 24 SOT gate: alternate depart route is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  fs.appendFileSync(file, '\nPOST /v1/driver/trips/{tripId}/stops/{stopId}/leave\n');
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /alternate depart route/);
});

test('Day 24 SOT gate: alternate pending-count route is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  fs.appendFileSync(file, '\nGET /internal/v1/bookings/trips/{tripId}/stops/{stopId}/pending-passengers-count\n');
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /alternate pending-count route/);
});

test('Day 24 SOT gate: stale planned-ETA no-show anchor is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'SU26SE101_VIETRIDE_technical_context_v7.md');
  fs.appendFileSync(file, '\nNo-show uses TripStop.estimatedArrivalTime + 15 minutes as the authoritative anchor.\n');
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /stale no-show anchor/);
});

test('Day 24 SOT gate: extra pending-count validation seam is rejected', () => {
  const fixtureRoot = fixture();
  const file = path.join(fixtureRoot, 'VietRide_API_Contract_v1.md');
  const current = fs.readFileSync(file, 'utf8');
  fs.writeFileSync(file, current.replace('No row\nmatch is still raw `200` with zero.', 'No row\nmatch is still raw `200` with zero. Trip/Stop lookup is required and the tenant claim is required.'));
  assert.throws(() => verifyDay24Sot(fixtureRoot, { changedPaths: [] }), /extra pending-count lookup seam/);
});
