import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const CONTRACT_FILES = [
  'BE_TIMELINE_VU.md',
  'BACKEND_SOURCE_OF_TRUTH.md',
  'VietRide_API_Contract_v1.md',
  'SU26SE101_VIETRIDE_technical_context_v7.md',
  'db-schema/booking/README.md',
];

const forbiddenChangedPath = /^(?:db-schema\/.*(?:schema\.sql|\/migrations?\/.*)|apps\/.*\/(?:Migrations?|prisma)\/.*|.*\/(?:prisma\/.*|schema\.prisma))$/i;

function fail(message) {
  throw new Error(`[day24-sot] ${message}`);
}

function required(text, needle, label) {
  if (!text.includes(needle)) fail(`${label}: missing ${needle}`);
}

function requiredEverywhere(documents, needle, label, files = CONTRACT_FILES) {
  for (const file of files) required(documents[file], needle, `${label} (${file})`);
}

function requiredErrorRegistryRow(errorRegistry, code, httpStatus) {
  const rows = errorRegistry
    .split(/\r?\n/)
    .filter((line) => line.startsWith('|') && line.includes(`| \`${code}\` |`));
  const expected = `| \`${code}\` | ${httpStatus} |`;
  if (rows.length !== 1 || !rows[0].includes(expected)) {
    fail(`BSOT error registry ${code}: expected exactly one row with HTTP ${httpStatus}`);
  }
}

function requiredErrorStatus(section, code, httpStatus, label) {
  required(section, `\`${httpStatus} ${code}\``, `${label} ${code}`);
}

function forbidden(text, pattern, label) {
  if (pattern.test(text)) fail(`${label}: forbidden contradiction matched ${pattern}`);
}

function sectionBetween(text, heading, label) {
  const start = text.indexOf(heading);
  if (start < 0) fail(`${label}: missing heading ${heading}`);
  const lineStart = text.lastIndexOf('\n', start) + 1;
  const headingLevel = text.slice(lineStart).match(/^(#{1,6})[ \t]+/)?.[1].length ?? 3;
  const next = text.slice(start + heading.length).search(new RegExp(`\\n#{1,${headingLevel}}[ \\t]`, 'm'));
  return text.slice(start, next < 0 ? text.length : start + heading.length + next + 1);
}

function textBetween(text, startMarker, endMarker, label) {
  const start = text.indexOf(startMarker);
  if (start < 0) fail(`${label}: missing start marker ${startMarker}`);
  const end = text.indexOf(endMarker, start + startMarker.length);
  return text.slice(start, end < 0 ? text.length : end);
}

function assertJsonKeys(section, expected, label) {
  const match = section.match(/```json\s*([\s\S]*?)\s*```/);
  if (!match) fail(`${label}: missing JSON block`);
  let payload;
  try {
    payload = JSON.parse(match[1]);
  } catch (error) {
    fail(`${label}: invalid JSON block (${error.message})`);
  }
  const actual = Object.keys(payload);
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    fail(`${label}: expected fields ${expected.join(',')} but found ${actual.join(',')}`);
  }
}

function assertRegistryEventFields(bsot, routingKey, expected) {
  const escaped = routingKey.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const row = bsot.match(new RegExp('^\\| `'+escaped+'` \\|.*$', 'm'))?.[0];
  if (!row) fail(`event registry ${routingKey}: missing row`);
  const payload = row.match(/\{([^{}]+)\}/)?.[1];
  if (!payload) fail(`event registry ${routingKey}: missing flat exact payload`);
  const actual = payload.split(',').map((entry) => entry.trim().match(/^([A-Za-z][A-Za-z0-9]*)/)?.[1]);
  if (actual.some((field) => !field) || JSON.stringify(actual) !== JSON.stringify(expected)) {
    fail(`event registry ${routingKey}: expected fields ${expected.join(',')} but found ${actual.join(',')}`);
  }
}

function assertInlineEventFields(section, expected, label) {
  const payload = section.match(/\{([^{}]+)\}/)?.[1];
  if (!payload) fail(`${label}: missing flat exact payload`);
  const actual = payload.split(',').map((entry) => entry.trim().match(/^`?([A-Za-z][A-Za-z0-9]*)/)?.[1]);
  if (actual.some((field) => !field) || JSON.stringify(actual) !== JSON.stringify(expected)) {
    fail(`${label}: expected fields ${expected.join(',')} but found ${actual.join(',')}`);
  }
}

function changedPaths(root) {
  try {
    const output = execFileSync('git', ['status', '--short'], { cwd: root, encoding: 'utf8' });
    return output
      .split(/\r?\n/)
      .filter(Boolean)
      .map((line) => line.slice(3).trim().replaceAll('\\', '/'));
  } catch {
    return [];
  }
}

export function verifyDay24Sot(root = process.cwd(), options = {}) {
  const documents = {};
  for (const relative of CONTRACT_FILES) {
    const file = path.join(root, relative);
    if (!fs.existsSync(file)) fail(`missing contract file: ${relative}`);
    documents[relative] = fs.readFileSync(file, 'utf8');
  }

  const timeline = documents['BE_TIMELINE_VU.md'];
  const bsot = documents['BACKEND_SOURCE_OF_TRUTH.md'];
  const api = documents['VietRide_API_Contract_v1.md'];
  const technical = documents['SU26SE101_VIETRIDE_technical_context_v7.md'];
  const bookingSchema = documents['db-schema/booking/README.md'];

  required(timeline, '### Day 24 — Thu 2026-06-25 — Stop disable + No-show', 'Day-24 timeline');
  requiredEverywhere(documents, 'capturedNow + 24h', 'single captured clock');
  requiredEverywhere(documents, 'tripCurrentDeparture - 2h', 'current-departure deadline');
  required(bsot, 'deadline = min(capturedNow + 24h, tripCurrentDeparture - 2h)', 'D24-3 formula');
  required(api, 'deadline == now', 'passenger equality eligibility');
  required(technical, 'deadline == now', 'technical equality eligibility');
  required(bookingSchema, 'deadline == now', 'booking equality eligibility');
  requiredEverywhere(documents, 'deadline < now', 'strict scheduler predicate');
  requiredEverywhere(documents, 'no synchronous fallback', 'no equality fallback');
  required(bsot, 'only a later scheduler pass', 'post-equality scheduler pass');
  required(bsot, 'StopDisabledAutoFallbackJob` | Recurring | Every 5 phút', 'fallback job registry');
  required(bsot, 'NoShowDetectionJob` | Recurring | Every 5 phút', 'no-show job registry');

  const errorRegistry = sectionBetween(bsot, '### 5.9 Canonical Error Code Registry', 'BSOT error registry');
  for (const [code, httpStatus] of [
    ['STOP_ALREADY_DISABLED', '409'],
    ['TRIP_STOP_NOT_ARRIVED', '422'],
    ['TRIP_STOP_ALREADY_DEPARTED', '409'],
    ['UPSTREAM_UNAVAILABLE', '502'],
  ]) {
    requiredErrorRegistryRow(errorRegistry, code, httpStatus);
  }
  const changelog = sectionBetween(bsot, '## 13. Changelog', 'BSOT changelog');
  required(changelog, '| **1.37.0** | 2026-07-18 |', 'BSOT version/changelog row');
  required(changelog, 'Freeze Day-24 stop-disable', 'BSOT Day-24 changelog row');

  required(api, 'DELETE `/v1/operator/stops/{id}?replacedByStopId=`', 'canonical disable route');
  required(api, 'warning` is a present JSON property whose value is `null`', 'DELETE warning null');
  required(api, '`ActiveBookingCount` is omitted', 'DELETE count omission');
  required(technical, 'DELETE /v1/operator/stops/{stopId}?replacedByStopId=', 'technical DELETE route');
  required(bsot, 'DELETE is the\nsole disable route', 'BSOT DELETE route ownership');
  required(api, '`PATCH /v1/operator/stops/{id}` remains details-update-only', 'PATCH reconciliation');
  required(technical, 'PATCH\n/v1/operator/stops/{stopId}` remains details-update-only', 'technical PATCH reconciliation');
  requiredEverywhere(documents, 'STOP_DISABLED_BOOKING_AFFECTED', 'legacy warning marker');
  required(bsot, 'legacy/deprecated for DELETE', 'BSOT legacy warning marker');
  required(api, 'booking.stop_disabled.affected', 'async impact source');
  for (const [file, text] of Object.entries(documents)) {
    forbidden(text, /\/active-by-stop\//, `${file} obsolete synchronous stop-impact endpoint`);
  }
  forbidden(technical, /PATCH \/v1\/operator\/stops\/\{stopId\}` body \{ isActive/i, 'PATCH-as-disable registry');
  const legacyWarningRows = errorRegistry.match(/^\| \| `STOP_DISABLED_BOOKING_AFFECTED` .*$/gm) ?? [];
  if (legacyWarningRows.length !== 1 || !legacyWarningRows[0].includes('legacy/deprecated for DELETE')) {
    fail('BSOT error registry: STOP_DISABLED_BOOKING_AFFECTED must have exactly one deprecated DELETE row');
  }
  forbidden(errorRegistry, /STOP_DISABLED_BOOKING_AFFECTED` \| 200 \(warning\) \| Alert khi disable/i, 'active stop-disable warning semantics');

  const apiStopDelete = sectionBetween(api, '### DELETE `/v1/operator/stops/{id}?replacedByStopId=`', 'API stop DELETE contract');
  requiredErrorStatus(apiStopDelete, 'STOP_ALREADY_DISABLED', '409', 'API stop DELETE error');
  const apiDepart = sectionBetween(api, '### POST `/v1/driver/trips/{tripId}/stops/{stopId}/depart`', 'API stop departure contract');
  for (const [code, httpStatus] of [
    ['TRIP_STOP_NOT_ARRIVED', '422'],
    ['TRIP_STOP_ALREADY_DEPARTED', '409'],
    ['UPSTREAM_UNAVAILABLE', '502'],
  ]) {
    requiredErrorStatus(apiDepart, code, httpStatus, 'API stop departure error');
  }
  const bsotDeparture = sectionBetween(bsot, '#### Day-24 public Trip stop departure', 'BSOT stop departure contract');
  for (const [code, httpStatus] of [
    ['TRIP_STOP_NOT_ARRIVED', '422'],
    ['TRIP_STOP_ALREADY_DEPARTED', '409'],
    ['UPSTREAM_UNAVAILABLE', '502'],
  ]) {
    requiredErrorStatus(bsotDeparture, code, httpStatus, 'BSOT stop departure error');
  }
  const technicalErrorRegistry = sectionBetween(technical, '### Code & API Conventions', 'technical error registry');
  for (const code of ['STOP_ALREADY_DISABLED', 'TRIP_STOP_NOT_ARRIVED', 'TRIP_STOP_ALREADY_DEPARTED', 'UPSTREAM_UNAVAILABLE']) {
    required(technicalErrorRegistry, `\`${code}\``, `technical error registry ${code}`);
  }
  const technicalDeparture = textBetween(
    technical,
    '> **Day-24 departure endpoint:**',
    '> Hangfire job generate TripStop',
    'technical stop departure contract',
  );
  for (const [code, httpStatus] of [
    ['TRIP_STOP_NOT_ARRIVED', '422'],
    ['TRIP_STOP_ALREADY_DEPARTED', '409'],
    ['UPSTREAM_UNAVAILABLE', '502'],
  ]) {
    requiredErrorStatus(technicalDeparture, code, httpStatus, 'technical stop departure error');
  }

  const tripStopRegistryRow = technical.match(/^\| \*\*TripStop\*\* \|.*$/m)?.[0] ?? '';
  required(tripStopRegistryRow, 'actualArrivalTime nullable, actualDepartureTime nullable, status PENDING\\|ARRIVED\\|SKIPPED', 'TripStop registry anchors');
  required(technical, 'fallbackStationId: affectedField == "PICKUP"', 'conditional fallback metadata');
  required(bookingSchema, 'route origin for `PICKUP`, route destination', 'booking fallback metadata');

  required(api, '`POST /v1/bookings/{bookingId}/pending-action/{actionId}/accept-fallback`', 'singular fallback route');
  required(api, 'body shape is not specified by the ratified D24-2 record', 'fallback no-guess guard');
  required(bsot, 'D24-2 ratifies no additional fields', 'BSOT fallback no-guess guard');
  required(api, 'STOP_DISABLED_REFUSED', 'refusal reason');
  required(api, 'refundAmount:100% of totalAmount', '100% refusal refund');
  required(bsot, 'BOOKING_PENDING_ACTION_ALREADY_RESOLVED', 'terminal action conflict');
  required(api, 'Day-23 `SCHEDULE_CHANGE` resolver/body is unchanged', 'resolver non-broadening');
  const day23Resolver = sectionBetween(
    api,
    '### POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`',
    'Day-23 resolver',
  );
  required(day23Resolver, 'resolves only\na persisted `SCHEDULE_CHANGE`', 'Day-23 resolver reason');
  assertJsonKeys(day23Resolver, ['action', 'note'], 'Day-23 resolver body');
  forbidden(day23Resolver, /reason\s*[=:]\s*STOP_DISABLED/i, 'broadened Day-23 resolver reason');
  forbidden(day23Resolver, /accept-fallback/i, 'fallback alias inside Day-23 resolver');

  required(api, 'Booking.status = CONFIRMED', 'pending-count status');
  required(api, 'Passenger.boardingStatus = PENDING', 'pending-count boarding status');
  required(api, 'Booking.tripId = :tripId', 'pending-count trip predicate');
  required(api, 'Booking.pickupStopId = :stopId', 'pending-count stop predicate');
  required(api, 'Booking.operatorId = :operatorId', 'pending-count operator predicate');
  required(api, 'pendingPassengerCount": 0', 'pending-count raw zero');
  required(api, 'no Trip/Stop lookup, caller-service/tenant-claim authorization', 'pending-count seam restrictions');
  required(api, 'invalid Internal JWT returns `401 AUTH_TOKEN_INVALID`', 'pending-count auth error');
  const pendingCount = sectionBetween(
    api,
    '### GET `/internal/v1/bookings/trips/{tripId}/stops/{stopId}/pending-passenger-count?operatorId={operatorId}`',
    'pending-count contract',
  );
  forbidden(pendingCount, /Trip\/Stop lookup is required|validates? Trip\/Stop references?/i, 'extra pending-count lookup seam');
  forbidden(pendingCount, /tenant claim is required|callerService is required/i, 'extra pending-count authorization seam');
  forbidden(api, /pending-passengers-count|pending-passenger-counts|active-by-stop/i, 'alternate pending-count route');

  required(api, 'POST `/v1/driver/trips/{tripId}/stops/{stopId}/depart`', 'depart route');
  required(api, '`Trip.status=IN_PROGRESS`', 'depart Trip state');
  required(api, '`TripStop.status=ARRIVED`', 'depart stop state');
  required(api, '`TripStop.actualDepartureTime IS NULL`', 'depart null anchor');
  required(api, 'data is exactly `{ tripId, stopId, departedAt, pendingPassengerCount, eventEmitted }`', 'depart response');
  required(api, 'TRIP_STOP_ALREADY_DEPARTED', 'depart replay conflict');
  required(api, 'TRIP_STOP_NOT_ARRIVED', 'depart stop conflict');
  required(api, 'UPSTREAM_UNAVAILABLE', 'depart upstream conflict');
  required(api, 'actualDepartureTime`: nullable UTC datetime', 'raw Trip additive departure field');
  required(api, '"status": "PENDING"', 'raw Trip stop status');
  required(api, '"actualArrivalTime": null', 'raw Trip stop actual-arrival anchor');
  forbidden(api, /\/stops\/\{stopId\}\/(?:leave|departure|departed)/i, 'alternate depart route');
  forbidden(technical, /TripStop\.estimatedArrivalTime\s*\+\s*15|Trip\.departureDateTime\s*\+\s*15/i, 'stale no-show anchor');
  forbidden(technical, /stale snapshot (?:is )?allowed|planned ETA is authoritative/i, 'stale snapshot seam');

  for (const event of [
    'trip.stop.disabled',
    'booking.stop_disabled.affected',
    'booking.booking.stop_disabled_auto_fallback_applied',
    'booking.booking.passenger_no_show_marked',
    'trip.stop.departed_with_pending',
  ]) requiredEverywhere(documents, event, `event registry ${event}`, ['BACKEND_SOURCE_OF_TRUTH.md', 'VietRide_API_Contract_v1.md']);
  const eventFields = {
    'trip.stop.disabled': ['eventId', 'occurredAt', 'eventType', 'stopId', 'operatorId', 'replacedByStopId'],
    'booking.stop_disabled.affected': ['eventId', 'occurredAt', 'eventType', 'stopId', 'replacedByStopId', 'recipientUserIds', 'affectedBookingCount'],
    'booking.booking.stop_disabled_auto_fallback_applied': ['eventId', 'occurredAt', 'eventType', 'bookingId', 'tripId', 'userId', 'pendingActionId', 'disabledStopId', 'affectedField', 'fallbackStationId', 'resolvedAction'],
    'booking.booking.passenger_no_show_marked': ['eventId', 'occurredAt', 'eventType', 'bookingId', 'tripId', 'userId', 'bookingStatus', 'newlyNoShowPassengerIds', 'triggerType', 'pickupStopId'],
    'trip.stop.departed_with_pending': ['eventId', 'occurredAt', 'eventType', 'tripId', 'stopId', 'stopName', 'pendingPassengerCount', 'driverUserId', 'assistantUserId', 'departedAt'],
  };
  for (const [event, fields] of Object.entries(eventFields)) {
    assertRegistryEventFields(bsot, event, fields);
  }
  assertInlineEventFields(
    sectionBetween(api, '### `trip.stop.disabled`', 'API trip.stop.disabled'),
    eventFields['trip.stop.disabled'],
    'API trip.stop.disabled fields',
  );
  assertInlineEventFields(
    sectionBetween(api, '### `booking.stop_disabled.affected`', 'API booking.stop_disabled.affected'),
    eventFields['booking.stop_disabled.affected'],
    'API booking.stop_disabled.affected fields',
  );
  assertJsonKeys(
    sectionBetween(api, '### `booking.booking.stop_disabled_auto_fallback_applied`', 'API fallback event'),
    eventFields['booking.booking.stop_disabled_auto_fallback_applied'],
    'API fallback event fields',
  );
  assertJsonKeys(
    sectionBetween(api, '### `booking.booking.passenger_no_show_marked`', 'API no-show event'),
    eventFields['booking.booking.passenger_no_show_marked'],
    'API no-show event fields',
  );
  assertJsonKeys(
    sectionBetween(api, '### `trip.stop.departed_with_pending`', 'API departed event'),
    eventFields['trip.stop.departed_with_pending'],
    'API departed event fields',
  );
  required(bsot, 'eventId == OutboxEvent.Id == RabbitMQ MessageId', 'Outbox identity');
  required(api, 'duplicate EventIds are ignored', 'consumer dedupe');
  required(bsot, '`MARK_NO_SHOW`', 'history source');
  required(technical, 'MARK_NO_SHOW', 'technical history source');
  required(technical, 'DRIVER_STOP_DEPARTED_WITH_PENDING', 'notification enum');
  required(bookingSchema, 'NoShowDetectionJob', 'booking job contract');

  const paths = options.changedPaths ?? changedPaths(root);
  const forbiddenPaths = paths.filter((file) => forbiddenChangedPath.test(file));
  if (forbiddenPaths.length > 0) fail(`unowned DDL/migration paths changed: ${forbiddenPaths.join(', ')}`);

  return { checkedFiles: CONTRACT_FILES, checkedChangedPaths: paths.length };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))) {
  try {
    const result = verifyDay24Sot(process.cwd());
    console.log(`[day24-sot] verified ${result.checkedFiles.length} contract files; changed paths checked: ${result.checkedChangedPaths}`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
