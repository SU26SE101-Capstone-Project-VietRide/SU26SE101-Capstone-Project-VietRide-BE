// Day-23 focused runtime journey. HTTP traffic is Gateway-only. Direct infrastructure access is
// limited to isolated fixture setup, bounded evidence reads, and failure-safe cleanup.
import { execFileSync, spawn } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
export const gatewayBaseUrl = (process.env.GATEWAY_BASE_URL || 'http://localhost:3000').replace(/\/$/, '');
const rabbitBaseUrl = (process.env.RABBITMQ_MGMT_BASE_URL || 'http://localhost:15672').replace(/\/$/, '');
const manifestPath = 'TestResults/day23/task-23-9-evidence/manifest.json';

export const day23ErrorMatrix = Object.freeze([
  ['AUTH_TOKEN_INVALID', 401], ['FORBIDDEN', 403], ['BOOKING_NOT_FOUND', 404],
  ['BOOKING_PENDING_ACTION_NOT_FOUND', 404], ['BOOKING_PENDING_ACTION_NOT_RESOLVABLE', 409],
  ['BOOKING_PENDING_ACTION_SUPERSEDED', 409], ['BOOKING_PENDING_ACTION_ALREADY_RESOLVED', 409],
  ['BOOKING_PENDING_ACTION_EXPIRED', 409], ['IDEMPOTENCY_REQUEST_PENDING', 409],
  ['IDEMPOTENCY_KEY_REQUIRED', 422], ['IDEMPOTENCY_KEY_MISMATCH', 422], ['VALIDATION_ERROR', 422],
]);

export function assert(condition, message) {
  if (!condition) throw new Error(message);
}

export function isUuidV4(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

export function ictDate(instant) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(instant));
}

export function classifyScheduleChange(oldDeparture, newDeparture) {
  const deltaHours = Math.abs(new Date(newDeparture) - new Date(oldDeparture)) / 3_600_000;
  if (ictDate(oldDeparture) !== ictDate(newDeparture) || deltaHours >= 6) return 'MAJOR';
  if (deltaHours > 2) return 'MEDIUM';
  return 'MINOR';
}

export function assertGatewayOnlyUrl(url) {
  const rendered = url.replace('{{baseUrl}}', gatewayBaseUrl);
  const parsed = new URL(rendered);
  const gateway = new URL(gatewayBaseUrl);
  assert(parsed.origin === gateway.origin, `Non-Gateway URL is forbidden: ${url}`);
  assert(parsed.pathname.startsWith('/v1/') || parsed.pathname === '/health', `Public URL must use /v1/: ${url}`);
  assert(!/\/internal\/|\/clock|\/jobs?\//i.test(parsed.pathname), `Backdoor URL: ${url}`);
  assert(!/\/operator\/trips\/[^/]+\/schedule(?:\/|$)/i.test(parsed.pathname), `Trip schedule alias: ${url}`);
  assert(!/\/pending-actions\/[^/]+\/(?:accept|reject)(?:\/|$)/i.test(parsed.pathname), `Resolve alias: ${url}`);
  return true;
}

function capture(command, args) {
  return execFileSync(command, args, { cwd: root, encoding: 'utf8' }).trim();
}

function psql(database, sql) {
  return capture('docker', ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, '-Atc', sql]);
}

function readJson(relativePath) {
  return JSON.parse(fs.readFileSync(path.join(root, relativePath), 'utf8'));
}

function flattenRequests(items, result = []) {
  for (const item of items ?? []) {
    if (item.request) result.push(item);
    flattenRequests(item.item, result);
  }
  return result;
}

function requestUrl(request) {
  return typeof request.url === 'string' ? request.url : request.url?.raw ?? '';
}

export function assertPostmanArtifacts() {
  const collection = readJson('docs/api/postman/vietride.postman_collection.json');
  const environment = readJson('docs/api/postman/vietride.local.postman_environment.json');
  const allRequests = flattenRequests(collection.item);
  for (const item of allRequests) {
    const url = requestUrl(item.request);
    if (/day 23/i.test(item.name) || url.includes('day23')) assertGatewayOnlyUrl(url);
  }
  const folder = collection.item.find((item) => item.name === 'Day 23 - Schedule change journey');
  assert(folder, 'Postman Day-23 folder is missing');
  const requests = flattenRequests(folder.item);
  assert(requests.length >= 16, 'Day-23 Postman matrix is incomplete');
  for (const item of requests) assertGatewayOnlyUrl(requestUrl(item.request));
  const serialized = JSON.stringify(folder);
  for (const [code] of day23ErrorMatrix) assert(serialized.includes(code), `Postman error assertion missing ${code}`);
  for (const marker of ['MINOR', 'MEDIUM', 'MAJOR', 'currentDepartureAt', 'refundAmount']) assert(serialized.includes(marker), `Postman marker missing ${marker}`);
  const keys = new Set(environment.values.map((item) => item.key));
  for (const key of ['day23OperatorAdminAccessToken', 'day23PassengerAccessToken', 'day23DriverScheduleId', 'day23BookingId', 'day23PendingActionId', 'day23ResolveKey']) assert(keys.has(key), `Postman environment key missing ${key}`);
  console.log(`PASS | Postman Day-23 Gateway-only matrix (${requests.length} requests)`);
}

export function validateFocusedEvidenceManifest() {
  const manifest = readJson(manifestPath);
  assert(manifest.version === 1 && Array.isArray(manifest.results), 'Focused evidence manifest is invalid');
  const required = ['current departure', 'explicit outbox', 'restart', 'rabbitmq identity'];
  const text = JSON.stringify(manifest).toLowerCase();
  for (const marker of required) assert(text.includes(marker), `Evidence manifest missing ${marker}`);
  for (const result of manifest.results) {
    assert(/^23\.[3-8]$/.test(result.task), `Invalid evidence task ${result.task}`);
    assert(typeof result.command === 'string' && result.command.includes(result.filter), `Evidence command/filter mismatch for ${result.name}`);
    const locator = path.join(root, result.locator);
    assert(fs.existsSync(locator), `Evidence locator missing: ${result.locator}`);
    if (result.kind === 'trx') {
      const xml = fs.readFileSync(locator, 'utf8');
      const counters = xml.match(/<Counters\b([^>]*)\/>/);
      assert(counters, `TRX counters missing: ${result.locator}`);
      const attrs = Object.fromEntries([...counters[1].matchAll(/(\w+)="([^"]*)"/g)].map((m) => [m[1], m[2]]));
      assert(Number(attrs.executed) >= 1 && Number(attrs.failed) === 0, `TRX failed/empty: ${result.locator}`);
      assert(xml.includes(result.filter), `TRX suite/filter marker missing: ${result.locator}`);
    } else if (result.kind === 'jest') {
      const json = JSON.parse(fs.readFileSync(locator, 'utf8'));
      assert(json.success && json.numPassedTests >= 1 && json.numFailedTests === 0, `Jest failed/empty: ${result.locator}`);
      assert(JSON.stringify(json).includes(result.filter), `Jest filter marker missing: ${result.locator}`);
    } else {
      throw new Error(`Unknown evidence kind: ${result.kind}`);
    }
  }
  console.log(`PASS | validated ${manifest.results.length} retained Task 23.3-23.8 result locators`);
}

export async function poll(label, probe, predicate, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = await probe();
    if (predicate(value)) { console.log(`PASS | ${label}`); return value; }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`${label} timed out; last=${JSON.stringify(value)}`);
}

function redisKeys(key) {
  const hash = crypto.createHash('sha256').update(key).digest('hex').toUpperCase();
  return [`booking:idem:${key}`, `idempotency:${key}`, `booking:idem:v2:response:${hash}`, `booking:idem:v2:processing:${hash}`, `trip:idem:${key}`, `trip:idem:v2:response:${hash}`, `trip:idem:v2:processing:${hash}`];
}

function createOwnedState() {
  const state = {
    runId: crypto.randomUUID(),
    keys: new Set(),
    ids: {},
    generated: Object.fromEntries([
      'tripAudits', 'scheduleAudits', 'tripOutbox', 'pendingActions', 'statusHistory',
      'bookingOutbox', 'notifications', 'notificationDeliveries', 'emailDeliveries', 'redisKeys',
    ].map((name) => [name, new Set()])),
  };
  const id = (name) => (state.ids[name] = crypto.randomUUID());
  for (const name of ['operator', 'admin', 'passenger', 'otherPassenger', 'origin', 'destination', 'vehicleType', 'route']) id(name);
  for (const flow of ['minor', 'mediumAccept', 'mediumReject', 'majorReject', 'pending', 'longRange']) {
    id(`${flow}Vehicle`); id(`${flow}Driver`); id(`${flow}Schedule`); id(`${flow}Trip`); id(`${flow}Booking`);
  }
  for (const name of ['notResolvableAction', 'supersededAction', 'expiredAction']) id(name);
  state.key = () => { const value = crypto.randomUUID(); state.keys.add(value); return value; };
  return state;
}

function sqlList(values) {
  return [...values].map((value) => `'${value}'`).join(',') || 'NULL';
}

function rememberIds(target, database, sql) {
  const output = psql(database, sql);
  for (const value of output.split(/\r?\n/).filter(Boolean)) target.add(value);
}

function recordOwnedEffects(state) {
  const i = state.ids;
  const scheduleIds = Object.entries(i).filter(([name]) => name.endsWith('Schedule')).map(([, value]) => value);
  const tripIds = Object.entries(i).filter(([name]) => name.endsWith('Trip')).map(([, value]) => value);
  const bookingIds = Object.entries(i).filter(([name]) => name.endsWith('Booking')).map(([, value]) => value);
  rememberIds(state.generated.tripAudits, 'vietride_trip', `SELECT id FROM vietride_trip.trip_audit_logs WHERE trip_id IN (${sqlList(tripIds)})`);
  rememberIds(state.generated.scheduleAudits, 'vietride_trip', `SELECT id FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id IN (${sqlList(scheduleIds)})`);
  rememberIds(state.generated.tripOutbox, 'vietride_trip', `SELECT id FROM vietride_trip.outbox_events WHERE payload->>'tripId' IN (${sqlList(tripIds)}) OR payload->>'driverScheduleId' IN (${sqlList(scheduleIds)})`);
  rememberIds(state.generated.pendingActions, 'vietride_booking', `SELECT id FROM vietride_booking.booking_pending_actions WHERE booking_id IN (${sqlList(bookingIds)})`);
  rememberIds(state.generated.statusHistory, 'vietride_booking', `SELECT id FROM vietride_booking.booking_status_history WHERE booking_id IN (${sqlList(bookingIds)})`);
  rememberIds(state.generated.bookingOutbox, 'vietride_booking', `SELECT id FROM vietride_booking.outbox_events WHERE payload->>'bookingId' IN (${sqlList(bookingIds)}) OR payload->>'tripId' IN (${sqlList(tripIds)}) OR payload->>'userId' IN ('${i.passenger}','${i.otherPassenger}')`);
  const notificationKeys = psql('vietride_booking', `SELECT 'notification:idem:processed:'||event_type||':'||id FROM vietride_booking.outbox_events WHERE payload->>'bookingId' IN (${sqlList(bookingIds)}) OR payload->>'tripId' IN (${sqlList(tripIds)}) OR payload->>'userId' IN ('${i.passenger}','${i.otherPassenger}')`);
  for (const value of notificationKeys.split(/\r?\n/).filter(Boolean)) state.generated.redisKeys.add(value);
  rememberIds(state.generated.notifications, 'vietride_notification', `SELECT id FROM vietride_notification.notifications WHERE user_id IN ('${i.passenger}','${i.otherPassenger}')`);
  rememberIds(state.generated.notificationDeliveries, 'vietride_notification', `SELECT id FROM vietride_notification.notification_deliveries WHERE notification_id IN (${sqlList(state.generated.notifications)})`);
  rememberIds(state.generated.emailDeliveries, 'vietride_notification', `SELECT id FROM vietride_notification.email_deliveries WHERE notification_id IN (${sqlList(state.generated.notifications)})`);
}

function cleanupOwned(state) {
  const i = state.ids;
  const scheduleIds = Object.entries(i).filter(([name]) => name.endsWith('Schedule')).map(([, value]) => value);
  const tripIds = Object.entries(i).filter(([name]) => name.endsWith('Trip')).map(([, value]) => value);
  const bookingIds = Object.entries(i).filter(([name]) => name.endsWith('Booking')).map(([, value]) => value);
  const operations = [
    () => recordOwnedEffects(state),
    () => psql('vietride_notification', `DELETE FROM vietride_notification.notification_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE user_id IN ('${i.passenger}','${i.otherPassenger}')); DELETE FROM vietride_notification.email_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE user_id IN ('${i.passenger}','${i.otherPassenger}')); DELETE FROM vietride_notification.notifications WHERE user_id IN ('${i.passenger}','${i.otherPassenger}');`),
    () => psql('vietride_booking', `DELETE FROM vietride_booking.outbox_events WHERE payload->>'bookingId' IN (${sqlList(bookingIds)}) OR payload->>'tripId' IN (${sqlList(tripIds)}) OR payload->>'userId' IN ('${i.passenger}','${i.otherPassenger}'); DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (${sqlList(bookingIds)}); DELETE FROM vietride_booking.booking_pending_actions WHERE booking_id IN (${sqlList(bookingIds)}); DELETE FROM vietride_booking.bookings WHERE id IN (${sqlList(bookingIds)});`),
    () => psql('vietride_trip', `DELETE FROM vietride_trip.outbox_events WHERE payload->>'tripId' IN (${sqlList(tripIds)}) OR payload->>'driverScheduleId' IN (${sqlList(scheduleIds)}); DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id IN (${sqlList(tripIds)}); DELETE FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id IN (${sqlList(scheduleIds)}); DELETE FROM vietride_trip.trip_generation_skip_logs WHERE driver_schedule_id IN (${sqlList(scheduleIds)}); DELETE FROM vietride_trip.trips WHERE driver_schedule_id IN (${sqlList(scheduleIds)}) OR id IN (${sqlList(tripIds)}); DELETE FROM vietride_trip.driver_schedules WHERE id IN (${sqlList(scheduleIds)}); DELETE FROM vietride_trip.vehicles WHERE id IN (${sqlList(Object.entries(i).filter(([name]) => name.endsWith('Vehicle')).map(([, value]) => value))}); DELETE FROM vietride_trip.routes WHERE id='${i.route}'; DELETE FROM vietride_trip.stations WHERE id IN ('${i.origin}','${i.destination}'); DELETE FROM vietride_trip.vehicle_types WHERE id='${i.vehicleType}';`),
    () => psql('vietride_identity', `DELETE FROM vietride_identity.users WHERE id IN ('${i.admin}','${i.passenger}','${i.otherPassenger}'); DELETE FROM vietride_identity.operators WHERE id='${i.operator}';`),
    () => {
      const keys = [...state.keys].flatMap(redisKeys).concat([...state.generated.redisKeys]);
      if (keys.length) execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...keys], { encoding: 'utf8' });
    },
  ];
  const errors = [];
  for (const operation of operations) { try { operation(); } catch (error) { errors.push(error); } }
  if (errors.length) throw new AggregateError(errors, 'Day-23 cleanup failed');
}

function assertOwnedClean(state) {
  const i = state.ids;
  const scheduleIds = Object.entries(i).filter(([name]) => name.endsWith('Schedule')).map(([, value]) => value);
  const tripIds = Object.entries(i).filter(([name]) => name.endsWith('Trip')).map(([, value]) => value);
  const bookingIds = Object.entries(i).filter(([name]) => name.endsWith('Booking')).map(([, value]) => value);
  const vehicleIds = Object.entries(i).filter(([name]) => name.endsWith('Vehicle')).map(([, value]) => value);
  const identityCounts = psql('vietride_identity', `SELECT (SELECT count(*) FROM vietride_identity.users WHERE id IN ('${i.admin}','${i.passenger}','${i.otherPassenger}'))||'|'||(SELECT count(*) FROM vietride_identity.operators WHERE id='${i.operator}')`);
  assert(identityCounts === '0|0', `Identity cleanup incomplete: ${identityCounts}`);
  const tripCounts = psql('vietride_trip', `SELECT (SELECT count(*) FROM vietride_trip.trips WHERE id IN (${sqlList(tripIds)}))||'|'||(SELECT count(*) FROM vietride_trip.driver_schedules WHERE id IN (${sqlList(scheduleIds)}))||'|'||(SELECT count(*) FROM vietride_trip.vehicles WHERE id IN (${sqlList(vehicleIds)}))||'|'||(SELECT count(*) FROM vietride_trip.routes WHERE id='${i.route}')||'|'||(SELECT count(*) FROM vietride_trip.stations WHERE id IN ('${i.origin}','${i.destination}'))||'|'||(SELECT count(*) FROM vietride_trip.vehicle_types WHERE id='${i.vehicleType}')||'|'||(SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE id IN (${sqlList(state.generated.tripAudits)}))||'|'||(SELECT count(*) FROM vietride_trip.driver_schedule_audit_logs WHERE id IN (${sqlList(state.generated.scheduleAudits)}))||'|'||(SELECT count(*) FROM vietride_trip.trip_generation_skip_logs WHERE driver_schedule_id IN (${sqlList(scheduleIds)}))||'|'||(SELECT count(*) FROM vietride_trip.outbox_events WHERE id IN (${sqlList(state.generated.tripOutbox)}))`);
  assert(tripCounts.split('|').every((value) => value === '0'), `Trip ownership cleanup incomplete: ${tripCounts}`);
  const bookingCounts = psql('vietride_booking', `SELECT (SELECT count(*) FROM vietride_booking.bookings WHERE id IN (${sqlList(bookingIds)}))||'|'||(SELECT count(*) FROM vietride_booking.booking_pending_actions WHERE id IN (${sqlList(state.generated.pendingActions)}))||'|'||(SELECT count(*) FROM vietride_booking.booking_status_history WHERE id IN (${sqlList(state.generated.statusHistory)}))||'|'||(SELECT count(*) FROM vietride_booking.outbox_events WHERE id IN (${sqlList(state.generated.bookingOutbox)}))`);
  assert(bookingCounts.split('|').every((value) => value === '0'), `Booking ownership cleanup incomplete: ${bookingCounts}`);
  const notificationCounts = psql('vietride_notification', `SELECT (SELECT count(*) FROM vietride_notification.notifications WHERE id IN (${sqlList(state.generated.notifications)}))||'|'||(SELECT count(*) FROM vietride_notification.notification_deliveries WHERE id IN (${sqlList(state.generated.notificationDeliveries)}))||'|'||(SELECT count(*) FROM vietride_notification.email_deliveries WHERE id IN (${sqlList(state.generated.emailDeliveries)}))`);
  assert(notificationCounts === '0|0|0', `Notification ownership cleanup incomplete: ${notificationCounts}`);
  const ownedRedisKeys = [...state.keys].flatMap(redisKeys).concat([...state.generated.redisKeys]);
  if (ownedRedisKeys.length) {
    const remaining = Number(capture('docker', ['exec', 'vietride_redis', 'redis-cli', 'EXISTS', ...ownedRedisKeys]));
    assert(remaining === 0, `Redis cleanup left ${remaining} keys`);
  }
}

function runtimeDepartureWindow() {
  const ict = new Date(Date.now() + 7 * 3_600_000);
  const hour = ict.getUTCHours();
  let baseHour;
  let dayOffset = 0;
  if (hour < 5) baseHour = 8;
  else if (hour < 13) baseHour = 16;
  else if (hour < 21) { baseHour = 8; dayOffset = 1; }
  else { baseHour = 16; dayOffset = 1; }
  ict.setUTCDate(ict.getUTCDate() + dayOffset);
  const time = (delta) => `${String(baseHour + delta).padStart(2, '0')}:00:00`;
  return {
    serviceDate: ict.toISOString().slice(0, 10), oldTime: time(0), minorTime: time(2),
    mediumAcceptTime: time(3), mediumRejectTime: time(4), majorRejectTime: time(6), pendingTime: time(3),
  };
}

function setupOwned(state) {
  const i = state.ids;
  const tag = state.runId.replaceAll('-', '').slice(0, 10);
  const window = runtimeDepartureWindow();
  const { serviceDate, oldTime } = window;
  const longDateValue = new Date(`${serviceDate}T00:00:00Z`);
  longDateValue.setUTCDate(longDateValue.getUTCDate() + 10);
  const longRangeDate = longDateValue.toISOString().slice(0, 10);
  state.serviceDate = serviceDate;
  psql('vietride_identity', `INSERT INTO vietride_identity.operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,cancellation_policy,is_active) VALUES ('${i.operator}','Day23 ${tag}','D23BR${tag}','D23TX${tag}','op-${tag}@day23.local','0900000023','APPROVED',now(),'[]',true); INSERT INTO vietride_identity.users (id,email,display_name,role,status,operator_id) VALUES ('${i.admin}','admin-${tag}@day23.local','Day23 Admin','OPERATOR_ADMIN','ACTIVE','${i.operator}'),('${i.passenger}','passenger-${tag}@day23.local','Day23 Passenger','PASSENGER','ACTIVE',NULL),('${i.otherPassenger}','other-${tag}@day23.local','Day23 Other','PASSENGER','ACTIVE',NULL);`);
  const flows = [
    ['minor', window.minorTime, 'CONFIRMED', serviceDate],
    ['mediumAccept', window.mediumAcceptTime, 'CONFIRMED', serviceDate],
    ['mediumReject', window.mediumRejectTime, 'CONFIRMED', serviceDate],
    ['majorReject', window.majorRejectTime, 'CONFIRMED', serviceDate],
    ['pending', window.pendingTime, 'PENDING_PAYMENT', serviceDate],
    ['longRange', window.mediumAcceptTime, 'CONFIRMED', longRangeDate],
  ];
  const tripFixtureSql = flows.map(([flow, , , flowDate], index) => {
    const contractDay = new Date(`${flowDate}T00:00:00Z`).getUTCDay();
    const arrivalTime = `${String(Number(oldTime.slice(0, 2)) + 1).padStart(2, '0')}:00:00`;
    return `INSERT INTO vietride_trip.vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status) VALUES ('${i[`${flow}Vehicle`]}','${i.operator}','${i.vehicleType}','D23${tag.slice(0, 8)}${index}','{"version":1,"vehicleTypeCode":"DAY23","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"seatType":"STANDARD","isEnabled":true}]}',1,'ACTIVE'); INSERT INTO vietride_trip.driver_schedules (id,operator_id,route_id,vehicle_id,driver_user_id,day_of_week,departure_time,valid_from,valid_until,is_active) VALUES ('${i[`${flow}Schedule`]}','${i.operator}','${i.route}','${i[`${flow}Vehicle`]}','${i[`${flow}Driver`]}','[${contractDay}]','${oldTime}',current_date,current_date+30,false); INSERT INTO vietride_trip.trips (id,operator_id,route_id,vehicle_id,driver_user_id,driver_schedule_id,departure_date_time,estimated_arrival_time,status,source,base_fare) VALUES ('${i[`${flow}Trip`]}','${i.operator}','${i.route}','${i[`${flow}Vehicle`]}','${i[`${flow}Driver`]}','${i[`${flow}Schedule`]}','${flowDate} ${oldTime}+07','${flowDate} ${arrivalTime}+07','SCHEDULED','AUTO_FROM_SCHEDULE',100001);`;
  }).join(' ');
  psql('vietride_trip', `INSERT INTO vietride_trip.stations (id,name,slug,city,province) VALUES ('${i.origin}','Day23 Origin','day23-${tag}-origin','HCMC','HCMC'),('${i.destination}','Day23 Destination','day23-${tag}-destination','Da Lat','Lam Dong'); INSERT INTO vietride_trip.vehicle_types (id,code,display_name,default_seat_count,is_system_defined) VALUES ('${i.vehicleType}','D23_${tag}','Day23 Type',1,false); INSERT INTO vietride_trip.routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,estimated_duration_minutes) VALUES ('${i.route}','${i.operator}','Day23 Route','${i.origin}','${i.destination}',100001,240); ${tripFixtureSql}`);
  psql('vietride_booking', flows.map(([flow, , status, flowDate], index) => `INSERT INTO vietride_booking.bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,base_fare,total_amount,status,trip_snapshot_departure,trip_current_departure,confirmed_at) VALUES ('${i[`${flow}Booking`]}','VR-${flowDate.replaceAll('-', '')}-${tag.slice(0, 7).toUpperCase()}${index}','${i.passenger}','${i[`${flow}Trip`]}','${i.operator}','${i.origin}',100001,100001,'${status}','${flowDate} ${oldTime}+07','${flowDate} ${oldTime}+07',${status === 'CONFIRMED' ? 'now()' : 'NULL'});`).join(' '));
  console.log(`PASS | isolated Day-23 fixture graph seeded (${state.runId})`);
  state.flows = flows;
}

async function issueTokens(state) {
  const settings = readJson('apps/identity/src/VietRide.Identity.Api/appsettings.Development.json');
  const privateKey = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  const token = (sub, role, operatorId) => new SignJWT({ role, operatorId, email: `${role.toLowerCase()}@day23.local`, hasPhone: 'true' }).setProtectedHeader({ alg: 'RS256', kid }).setIssuer('vietride-identity').setAudience('vietride-api').setSubject(sub).setIssuedAt().setExpirationTime('15m').sign(privateKey);
  const i = state.ids;
  const [admin, passenger, otherPassenger] = await Promise.all([token(i.admin, 'OPERATOR_ADMIN', i.operator), token(i.passenger, 'PASSENGER'), token(i.otherPassenger, 'PASSENGER')]);
  console.log('PASS | runtime JWTs issued (redacted)');
  return { admin, passenger, otherPassenger };
}

async function request(method, pathname, { token, key, body, signal } = {}) {
  assertGatewayOnlyUrl(`${gatewayBaseUrl}${pathname}`);
  const headers = { 'X-Request-Id': crypto.randomUUID(), Accept: 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (key !== undefined) headers['Idempotency-Key'] = key;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${gatewayBaseUrl}${pathname}`, { method, headers, signal, body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body) });
  const raw = await response.text();
  let json; try { json = raw ? JSON.parse(raw) : null; } catch { throw new Error(`${pathname} returned non-JSON`); }
  return { status: response.status, raw, json };
}

function expect(result, status, code, label) {
  assert(result.status === status, `${label}: expected ${status}, got ${result.status}`);
  if (code) assert(result.json?.error?.code === code, `${label}: expected ${code}, got ${result.json?.error?.code}`);
  console.log(`PASS | ${label} | HTTP ${status}${code ? ` ${code}` : ''}`);
}

async function applySchedule(state, tokens, flow, newTime, severity) {
  const i = state.ids;
  const key = state.key();
  expect(await request('PATCH', `/v1/operator/driver-schedules/${i[`${flow}Schedule`]}?applyTo=ALL_PENDING`, { token: tokens.admin, key, body: { departureTime: newTime } }), 200, null, `${severity} ALL_PENDING producer`);
  const expectedCounts = flow === 'pending' ? '0|0|0' : severity === 'MINOR' ? '0|1|0' : '1|0|1';
  await poll(`${flow} exact projection/action/event cardinality`, () => psql('vietride_booking', `SELECT to_char(trip_current_departure AT TIME ZONE 'Asia/Ho_Chi_Minh','HH24:MI')||'|'||(SELECT count(*) FROM vietride_booking.booking_pending_actions WHERE booking_id='${i[`${flow}Booking`]}' AND resolved_at IS NULL)||'|'||(SELECT count(*) FROM vietride_booking.outbox_events WHERE payload->>'bookingId'='${i[`${flow}Booking`]}' AND event_type='booking.booking.schedule_change_informational')||'|'||(SELECT count(*) FROM vietride_booking.outbox_events WHERE payload->>'bookingId'='${i[`${flow}Booking`]}' AND event_type='booking.booking.schedule_change_required') FROM vietride_booking.bookings WHERE id='${i[`${flow}Booking`]}'`), (value) => value === `${newTime.slice(0, 5)}|${expectedCounts}`);
  if (flow === 'pending' || severity === 'MINOR') return null;
  const expectedPercent = severity === 'MEDIUM' ? 50 : 100;
  const expectedRefund = severity === 'MEDIUM' ? 50001 : 100001;
  const action = await poll(`${flow} frozen action metadata`, () => psql('vietride_booking', `SELECT id::text||'|'||severity||'|'||(metadata->>'severity')||'|'||(metadata->>'refundBasisAmount')||'|'||(metadata->>'refundPercent')||'|'||(metadata->>'refundAmount') FROM vietride_booking.booking_pending_actions WHERE booking_id='${i[`${flow}Booking`]}' AND resolved_at IS NULL`), (value) => value.split('|').slice(1).join('|') === `${severity}|${severity}|100001|${expectedPercent}|${expectedRefund}`);
  const actionId = action.split('|')[0];
  state.generated.pendingActions.add(actionId);
  console.log(`PASS | ${flow} required action freezes ${severity}/100001/${expectedPercent}/${expectedRefund}`);
  return actionId;
}

function scheduleMetadata(sourceEventId, oldDeparture, newDeparture, severity, initialDeadline, terminalDeadline = null) {
  return JSON.stringify({ sourceEventId, oldDeparture, newDeparture, severity, initialDeadline, terminalDeadline, refundBasisAmount: 100001, refundPercent: severity === 'MEDIUM' ? 50 : 100, refundAmount: severity === 'MEDIUM' ? 50001 : 100001 }).replaceAll("'", "''");
}

function seedErrorActions(state) {
  const i = state.ids;
  const booking = i.mediumAcceptBooking;
  const now = new Date();
  const oldDeparture = new Date(now.getTime() + 7 * 86400000).toISOString();
  const newDeparture = new Date(now.getTime() + 7 * 86400000 + 3 * 3600000).toISOString();
  const future = new Date(now.getTime() + 3600000).toISOString();
  const past = new Date(now.getTime() - 3600000).toISOString();
  psql('vietride_booking', `INSERT INTO vietride_booking.booking_pending_actions (id,booking_id,reason,severity,deadline,metadata) VALUES ('${i.notResolvableAction}','${i.pendingBooking}','SCHEDULE_CHANGE','MEDIUM','${future}','${scheduleMetadata(crypto.randomUUID(), oldDeparture, newDeparture, 'MEDIUM', future)}'); INSERT INTO vietride_booking.booking_pending_actions (id,booking_id,reason,severity,deadline,resolved_at,resolved_action,metadata) VALUES ('${i.supersededAction}','${booking}','SCHEDULE_CHANGE','MEDIUM','${future}',now(),'SUPERSEDED','${scheduleMetadata(crypto.randomUUID(), oldDeparture, newDeparture, 'MEDIUM', future)}'); INSERT INTO vietride_booking.booking_pending_actions (id,booking_id,reason,severity,deadline,metadata) VALUES ('${i.expiredAction}','${booking}','SCHEDULE_CHANGE','MEDIUM','${past}','${scheduleMetadata(crypto.randomUUID(), oldDeparture, newDeparture, 'MEDIUM', past)}');`);
}

async function boundedWait(promise, label, timeoutMs = 10_000) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`${label} timed out`)), timeoutMs); }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

export async function settleInFlightBeforeCleanup(requestPromise, abort, timeoutMs = 10_000) {
  if (!requestPromise) return;
  const settled = requestPromise.then(() => undefined, () => undefined);
  try {
    await boundedWait(settled, 'pending probe in-flight request settlement', timeoutMs);
  } catch {
    abort();
    await settled;
  }
}

async function proveInFlightPending(state, tokens) {
  const i = state.ids;
  const key = state.key();
  const pathname = `/v1/bookings/${i.pendingBooking}/pending-actions/${i.notResolvableAction}/resolve`;
  const locker = spawn('docker', ['exec', '-i', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', 'vietride_booking'], { cwd: root, stdio: ['pipe', 'pipe', 'pipe'] });
  let stdout = '';
  let stderr = '';
  let released = false;
  locker.stdout.on('data', (chunk) => { stdout += chunk.toString(); });
  locker.stderr.on('data', (chunk) => { stderr += chunk.toString(); });
  const lockerExit = new Promise((resolve, reject) => {
    locker.once('error', reject);
    locker.once('exit', (code) => code === 0 ? resolve() : reject(new Error(`pending probe locker exited ${code}: ${stderr}`)));
  });
  const waitForMarker = (marker) => poll(`pending probe ${marker}`, () => stdout, (value) => value.includes(marker), 10_000);
  const abortController = new AbortController();
  let first;
  try {
    locker.stdin.write(`BEGIN;\nSELECT id FROM vietride_booking.booking_pending_actions WHERE id='${i.notResolvableAction}' FOR UPDATE;\n\\echo LOCK_ACQUIRED\n`);
    await waitForMarker('LOCK_ACQUIRED');
    first = request('POST', pathname, { token: tokens.passenger, key, body: { action: 'ACCEPTED' }, signal: abortController.signal });
    const processingKey = redisKeys(key).find((value) => value.includes('booking:idem:v2:processing:'));
    await poll('pending probe processing owner acquired', () => capture('docker', ['exec', 'vietride_redis', 'redis-cli', 'EXISTS', processingKey]), (value) => value === '1', 10_000);
    expect(await request('POST', pathname, { token: tokens.passenger, key, body: { action: 'ACCEPTED' } }), 409, 'IDEMPOTENCY_REQUEST_PENDING', 'in-flight duplicate is pending');
    locker.stdin.write('COMMIT;\n\\echo LOCK_RELEASED\n\\q\n');
    released = true;
    await waitForMarker('LOCK_RELEASED');
    await boundedWait(lockerExit, 'pending probe locker exit');
    expect(await first, 409, 'BOOKING_PENDING_ACTION_NOT_RESOLVABLE', 'active action/Booking state not resolvable');
  } finally {
    try {
      if (!released && locker.exitCode === null) {
        try { locker.stdin.write('ROLLBACK;\n\\q\n'); }
        catch { locker.kill(); }
      }
      try { await boundedWait(lockerExit, 'pending probe locker exit'); }
      catch {
        if (locker.exitCode === null) locker.kill();
        await lockerExit.catch(() => undefined);
      }
    } finally {
      await settleInFlightBeforeCleanup(first, () => abortController.abort());
    }
  }
}

async function rabbitSnapshot() {
  const env = JSON.parse(capture('docker', ['inspect', '--format', '{{json .Config.Env}}', 'vietride_rabbitmq']));
  const map = Object.fromEntries(env.map((entry) => entry.split(/=(.*)/s).slice(0, 2)));
  const authorization = `Basic ${Buffer.from(`${map.RABBITMQ_DEFAULT_USER}:${map.RABBITMQ_DEFAULT_PASS}`).toString('base64')}`;
  const response = await fetch(`${rabbitBaseUrl}/api/queues/%2F`, { headers: { Authorization: authorization } });
  assert(response.ok, `RabbitMQ management returned ${response.status}`);
  const queues = await response.json();
  const names = ['booking.trip-schedule-changed', 'notification:booking-schedule-change-informational', 'notification:booking-schedule-change-required'];
  const selected = queues.filter((queue) => names.includes(queue.name));
  assert(selected.length === names.length, 'Required Day-23 RabbitMQ queues are missing');
  return Object.fromEntries(selected.map((queue) => [queue.name, { messages: queue.messages, ready: queue.messages_ready, unacked: queue.messages_unacknowledged }]));
}

async function runRuntimeJourney(state) {
  const health = await fetch(`${gatewayBaseUrl}/health`);
  assert(health.ok, `Gateway health returned ${health.status}`);
  const rabbitBefore = await rabbitSnapshot();
  const tokens = await issueTokens(state);
  const i = state.ids;
  const actions = {};
  const timeFor = (flow) => state.flows.find(([name]) => name === flow)[1];
  actions.mediumAccept = await applySchedule(state, tokens, 'mediumAccept', timeFor('mediumAccept'), 'MEDIUM');
  actions.mediumReject = await applySchedule(state, tokens, 'mediumReject', timeFor('mediumReject'), 'MEDIUM');
  actions.majorReject = await applySchedule(state, tokens, 'majorReject', timeFor('majorReject'), 'MAJOR');
  await applySchedule(state, tokens, 'minor', timeFor('minor'), 'MINOR');
  await applySchedule(state, tokens, 'pending', timeFor('pending'), 'MEDIUM');
  actions.longRange = await applySchedule(state, tokens, 'longRange', timeFor('longRange'), 'MEDIUM');

  expect(await request('POST', `/v1/bookings/${i.longRangeBooking}/pending-actions/${actions.longRange}/resolve`, { token: tokens.passenger, key: state.key(), body: { action: 'ACCEPTED' } }), 200, null, 'passenger accepts >24h MEDIUM precision branch');

  const acceptKey = state.key();
  const acceptPath = `/v1/bookings/${i.mediumAcceptBooking}/pending-actions/${actions.mediumAccept}/resolve`;
  const accepted = await request('POST', acceptPath, { token: tokens.passenger, key: acceptKey, body: { action: 'ACCEPTED' } });
  expect(accepted, 200, null, 'passenger accepts MEDIUM');
  const replay = await request('POST', acceptPath, { token: tokens.passenger, key: acceptKey, body: { action: 'ACCEPTED' } });
  assert(replay.status === 200 && replay.raw === accepted.raw, 'Resolver replay is not byte-identical');
  expect(await request('POST', acceptPath, { token: tokens.passenger, key: state.key(), body: { action: 'ACCEPTED' } }), 409, 'BOOKING_PENDING_ACTION_ALREADY_RESOLVED', 'new key sees terminal action');
  expect(await request('POST', acceptPath, { token: tokens.passenger, key: acceptKey, body: { action: 'REJECTED' } }), 422, 'IDEMPOTENCY_KEY_MISMATCH', 'changed replay body mismatches');

  for (const [flow, actionId, expectedRefund] of [['mediumReject', actions.mediumReject, 50001], ['majorReject', actions.majorReject, 100001]]) {
    expect(await request('POST', `/v1/bookings/${i[`${flow}Booking`]}/pending-actions/${actionId}/resolve`, { token: tokens.passenger, key: state.key(), body: { action: 'REJECTED' } }), 200, null, `${flow} rejection`);
    assert(psql('vietride_booking', `SELECT status::text||'|'||refund_override::text||'|'||(SELECT payload->>'refundAmount' FROM vietride_booking.outbox_events WHERE event_type='booking.booking.cancelled' AND payload->>'bookingId'='${i[`${flow}Booking`]}' LIMIT 1) FROM vietride_booking.bookings WHERE id='${i[`${flow}Booking`]}'`) === `CANCELLED|true|${expectedRefund}`, `${flow} refund state mismatch`);
  }

  seedErrorActions(state);
  const basePath = `/v1/bookings/${i.mediumAcceptBooking}/pending-actions/${i.expiredAction}/resolve`;
  expect(await request('POST', basePath, { body: { action: 'ACCEPTED' } }), 401, 'AUTH_TOKEN_INVALID', 'missing JWT');
  expect(await request('POST', basePath, { token: tokens.admin, key: state.key(), body: { action: 'ACCEPTED' } }), 403, 'FORBIDDEN', 'role gate before lookup');
  expect(await request('POST', basePath, { token: tokens.otherPassenger, key: state.key(), body: { action: 'ACCEPTED' } }), 404, 'BOOKING_NOT_FOUND', 'owner masking');
  expect(await request('POST', `/v1/bookings/${i.mediumAcceptBooking}/pending-actions/${crypto.randomUUID()}/resolve`, { token: tokens.passenger, key: state.key(), body: { action: 'ACCEPTED' } }), 404, 'BOOKING_PENDING_ACTION_NOT_FOUND', 'missing action under owned Booking');
  await proveInFlightPending(state, tokens);
  expect(await request('POST', `/v1/bookings/${i.mediumAcceptBooking}/pending-actions/${i.supersededAction}/resolve`, { token: tokens.passenger, key: state.key(), body: { action: 'ACCEPTED' } }), 409, 'BOOKING_PENDING_ACTION_SUPERSEDED', 'superseded action');
  expect(await request('POST', basePath, { token: tokens.passenger, key: state.key(), body: { action: 'ACCEPTED' } }), 409, 'BOOKING_PENDING_ACTION_EXPIRED', 'strictly-after cutoff');
  expect(await request('POST', basePath, { token: tokens.passenger, body: { action: 'ACCEPTED' } }), 422, 'IDEMPOTENCY_KEY_REQUIRED', 'missing idempotency key');
  expect(await request('POST', basePath, { token: tokens.passenger, key: 'not-v4', body: { action: 'ACCEPTED', selectedStopId: crypto.randomUUID() } }), 422, 'VALIDATION_ERROR', 'invalid key and request shape');

  const detail = await request('GET', `/v1/operator/bookings/${i.mediumAcceptBooking}`, { token: tokens.admin });
  expect(detail, 200, null, 'operator reads current departure projection');
  assert(detail.json?.data?.trip?.currentDepartureAt && detail.json?.data?.trip?.departureAt, 'Nested current/snapshot departure missing');

  const eventState = psql('vietride_booking', `SELECT count(*) FILTER (WHERE (payload->>'eventId')::uuid=id)||'|'||count(*) FROM vietride_booking.outbox_events WHERE payload->>'bookingId' IN (${sqlList(Object.entries(i).filter(([name]) => name.endsWith('Booking')).map(([, value]) => value))})`);
  const [identityCount, totalCount] = eventState.split('|').map(Number);
  assert(totalCount > 0 && identityCount === totalCount, `Booking Outbox identity mismatch: ${eventState}`);
  await poll('Notification rows persisted for schedule facts', () => Number(psql('vietride_notification', `SELECT count(*) FROM vietride_notification.notifications WHERE user_id='${i.passenger}' AND type='TRIP_SCHEDULE_CHANGED'`)), (count) => count >= 4);
  const rabbitAfter = await rabbitSnapshot();
  for (const [name, stateAfter] of Object.entries(rabbitAfter)) {
    assert(stateAfter.messages === 0 && stateAfter.ready === 0 && stateAfter.unacked === 0, `${name} is not drained: ${JSON.stringify(stateAfter)}`);
    assert(rabbitBefore[name].messages === 0, `${name} was not drained before journey`);
  }
  console.log('PASS | bounded DB/Outbox/Notification/RabbitMQ runtime evidence');
  console.log('PASS | timeout equality/phases remain proven by retained frozen-clock PostgreSQL evidence; no wall-clock wait');
}

export async function runIsolatedGatewayJourney({ setup = setupOwned, execute = runRuntimeJourney, cleanup = cleanupOwned, assertClean = assertOwnedClean, state = createOwnedState() } = {}) {
  let journeyError;
  let cleanupError;
  try {
    await setup(state);
    await execute(state);
  } catch (error) {
    journeyError = error;
  } finally {
    try { await cleanup(state); await assertClean(state); console.log('PASS | isolated Day-23 fixture cleanup verified'); }
    catch (error) { cleanupError = error; }
  }
  if (journeyError && cleanupError) throw new AggregateError([journeyError, cleanupError], 'Journey and cleanup both failed');
  if (journeyError) throw journeyError;
  if (cleanupError) throw cleanupError;
}

export async function runFocused() {
  assertPostmanArtifacts();
  validateFocusedEvidenceManifest();
  await runIsolatedGatewayJourney();
  console.log('PASS | Day-23 focused runtime journey completed');
}

async function main() {
  const args = process.argv.slice(2);
  for (const arg of args) assert(['--focused', '--help', '-h'].includes(arg), `Unknown argument: ${arg}`);
  if (args.includes('--help') || args.includes('-h')) {
    console.log('Usage: node scripts/run-day23-schedule-change-local.mjs --focused\n\n--focused runs isolated Gateway/DB/Outbox/Notification/RabbitMQ evidence and cleanup.');
    return;
  }
  assert(args.includes('--focused'), 'Use --focused; full regression belongs to /audit-day 23');
  await runFocused();
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) await main();
