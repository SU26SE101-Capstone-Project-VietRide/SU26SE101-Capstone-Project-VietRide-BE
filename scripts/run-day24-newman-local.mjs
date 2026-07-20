import { execFileSync, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const collectionPath = 'docs/api/postman/vietride.postman_collection.json';
const environmentPath = 'docs/api/postman/vietride.local.postman_environment.json';
const folder = 'Day 24 - Stop disable and no-show evidence';
const environment = JSON.parse(fs.readFileSync(path.join(root, environmentPath), 'utf8'));
const variables = Object.fromEntries(environment.values.map(({ key, value }) => [key, value]));

const ids = {
  operator: variables.day24OperatorId,
  operatorAdmin: '24000000-0000-4000-8000-000000000011',
  passenger: '24000000-0000-4000-8000-000000000012',
  driver: '24000000-0000-4000-8000-000000000013',
  stop: variables.day24StopId,
  replacement: variables.day24ReplacementStopId,
  alternateReplacement: variables.day24AlternateReplacementStopId,
  trip: variables.day24TripId,
  tripStop: variables.day24TripStopId,
  alternateTripStop: variables.day24AlternateTripStopId,
  pendingTrip: variables.day24PendingTripId,
  pendingTripStop: variables.day24PendingTripStopId,
  booking: variables.day24BookingId,
  refusalBooking: variables.day24RefusalBookingId,
  countBooking: '24000000-0000-4000-8000-000000000303',
  countPassenger: '24000000-0000-4000-8000-000000000304',
  action: variables.day24PendingActionId,
  refusalAction: '24000000-0000-4000-8000-000000000042',
  fallbackStation: variables.day24FallbackStationId,
  destinationStation: '24000000-0000-4000-8000-000000000052',
  vehicleType: '24000000-0000-4000-8000-000000000061',
  vehicle: '24000000-0000-4000-8000-000000000062',
  route: '24000000-0000-4000-8000-000000000063',
  cancellationTrip: '24000000-0000-4000-8000-000000000064',
};

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function capture(command, args) {
  return execFileSync(command, args, { cwd: root, encoding: 'utf8' }).trim();
}

function psql(database, sql) {
  return capture('docker', [
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1',
    '-U', 'vietride', '-d', database, '-Atc', sql,
  ]);
}

function containerEnv(name) {
  const values = JSON.parse(capture('docker', ['inspect', '--format', '{{json .Config.Env}}', name]));
  return Object.fromEntries(values.map((entry) => entry.split(/=(.*)/s).slice(0, 2)));
}

function redisKeys(key) {
  const hash = crypto.createHash('sha256').update(key).digest('hex').toUpperCase();
  return [
    `booking:idem:${key}`, `booking:idem:v2:response:${hash}`, `booking:idem:v2:processing:${hash}`,
    `trip:idem:${key}`, `trip:idem:v2:response:${hash}`, `trip:idem:v2:processing:${hash}`,
    `idempotency:${key}`,
  ];
}

function ownedIdempotencyKeys() {
  return environment.values
    .filter(({ key }) => /^day24.*Key$/.test(key))
    .flatMap(({ value }) => redisKeys(value));
}

function cleanup() {
  const passengerIds = `'${ids.passenger}','${ids.driver}'`;
  const bookingIds = `'${ids.booking}','${ids.refusalBooking}','${ids.countBooking}'`;
  const tripIds = `'${ids.trip}','${ids.pendingTrip}','${ids.cancellationTrip}'`;
  const stopIds = `'${ids.stop}','${ids.replacement}','${ids.alternateReplacement}','${ids.tripStop}','${ids.alternateTripStop}','${ids.pendingTripStop}'`;
  const stationIds = `'${ids.fallbackStation}','${ids.destinationStation}'`;
  const operations = [
    () => psql('vietride_notification', `DELETE FROM vietride_notification.notification_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE user_id IN (${passengerIds})); DELETE FROM vietride_notification.email_deliveries WHERE notification_id IN (SELECT id FROM vietride_notification.notifications WHERE user_id IN (${passengerIds})); DELETE FROM vietride_notification.notifications WHERE user_id IN (${passengerIds});`),
    () => psql('vietride_payment', `DELETE FROM vietride_payment.wallet_transactions WHERE user_id IN (${passengerIds}); DELETE FROM vietride_payment.wallets WHERE user_id IN (${passengerIds}); DELETE FROM vietride_payment.outbox_events WHERE payload::text LIKE '%${ids.passenger}%' OR payload::text LIKE '%${ids.booking}%' OR payload::text LIKE '%${ids.refusalBooking}%';`),
    () => psql('vietride_booking', `DELETE FROM vietride_booking.tickets WHERE booking_id IN (${bookingIds}); DELETE FROM vietride_booking.passengers WHERE booking_id IN (${bookingIds}); DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (${bookingIds}); DELETE FROM vietride_booking.booking_pending_actions WHERE booking_id IN (${bookingIds}); DELETE FROM vietride_booking.outbox_events WHERE payload::text LIKE '%${ids.passenger}%' OR payload::text LIKE '%${ids.booking}%' OR payload::text LIKE '%${ids.refusalBooking}%' OR payload::text LIKE '%${ids.trip}%'; DELETE FROM vietride_booking.bookings WHERE id IN (${bookingIds});`),
    () => psql('vietride_trip', `UPDATE vietride_trip.stops SET replaced_by_stop_id=NULL WHERE id IN (${stopIds}); DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id IN (${tripIds}); DELETE FROM vietride_trip.outbox_events WHERE payload::text LIKE '%${ids.operator}%' OR payload::text LIKE '%${ids.trip}%' OR payload::text LIKE '%${ids.stop}%'; DELETE FROM vietride_trip.trips WHERE id IN (${tripIds}); DELETE FROM vietride_trip.routes WHERE id='${ids.route}'; DELETE FROM vietride_trip.vehicles WHERE id='${ids.vehicle}'; DELETE FROM vietride_trip.vehicle_types WHERE id='${ids.vehicleType}'; DELETE FROM vietride_trip.stops WHERE id IN (${stopIds}); DELETE FROM vietride_trip.stations WHERE id IN (${stationIds});`),
    () => psql('vietride_identity', `DELETE FROM vietride_identity.users WHERE id IN ('${ids.operatorAdmin}','${ids.passenger}','${ids.driver}'); DELETE FROM vietride_identity.operators WHERE id='${ids.operator}';`),
    () => {
      const keys = ownedIdempotencyKeys();
      if (keys.length) capture('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...keys]);
    },
  ];
  const failures = [];
  for (const operation of operations) {
    try { operation(); } catch (error) { failures.push(error); }
  }
  if (failures.length) throw new AggregateError(failures, 'Day-24 fixture cleanup failed');
}

function seed() {
  psql('vietride_identity', `INSERT INTO vietride_identity.operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,cancellation_policy,is_active) VALUES ('${ids.operator}','Day24 Audit Operator','D24-AUDIT-BRN','D24-AUDIT-TAX','operator@day24.local','0900000024','APPROVED',now(),'[]',true); INSERT INTO vietride_identity.users (id,email,display_name,role,status,operator_id) VALUES ('${ids.operatorAdmin}','admin@day24.local','Day24 Operator Admin','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),('${ids.passenger}','passenger@day24.local','Day24 Passenger','PASSENGER','ACTIVE',NULL),('${ids.driver}','driver@day24.local','Day24 Driver','DRIVER','ACTIVE','${ids.operator}');`);

  psql('vietride_trip', `INSERT INTO vietride_trip.stations (id,name,slug,city,province) VALUES ('${ids.fallbackStation}','Day24 Origin','day24-audit-origin','Ho Chi Minh','Ho Chi Minh'),('${ids.destinationStation}','Day24 Destination','day24-audit-destination','Da Lat','Lam Dong'); INSERT INTO vietride_trip.stops (id,operator_id,name,latitude,longitude) VALUES ('${ids.stop}','Day24 Disabled Stop',10.1,106.1),('${ids.replacement}','${ids.operator}','Day24 Replacement Stop',10.2,106.2),('${ids.alternateReplacement}','${ids.operator}','Day24 Alternate Replacement',10.3,106.3),('${ids.tripStop}','${ids.operator}','Day24 Arrived Stop',10.4,106.4),('${ids.alternateTripStop}','${ids.operator}','Day24 Alternate Trip Stop',10.5,106.5),('${ids.pendingTripStop}','${ids.operator}','Day24 Pending Stop',10.6,106.6);`.replace(`('${ids.stop}','Day24 Disabled Stop'`, `('${ids.stop}','${ids.operator}','Day24 Disabled Stop'`));

  psql('vietride_trip', `INSERT INTO vietride_trip.vehicle_types (id,code,display_name,default_seat_count) VALUES ('${ids.vehicleType}','DAY24_AUDIT','Day24 Audit Type',1); INSERT INTO vietride_trip.vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status) VALUES ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','D24AUDIT','{"version":1,"vehicleTypeCode":"DAY24_AUDIT","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"seatType":"STANDARD","isEnabled":true}]}',1,'ACTIVE'); INSERT INTO vietride_trip.routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,estimated_duration_minutes) VALUES ('${ids.route}','${ids.operator}','Day24 Audit Route','${ids.fallbackStation}','${ids.destinationStation}',100000,240); INSERT INTO vietride_trip.trips (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare) VALUES ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}',now()+interval '10 days',now()+interval '10 days 4 hours','IN_PROGRESS','MANUAL',100000),('${ids.pendingTrip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}',now()+interval '11 days',now()+interval '11 days 4 hours','IN_PROGRESS','MANUAL',100000),('${ids.cancellationTrip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}',now()+interval '12 days',now()+interval '12 days 4 hours','SCHEDULED','MANUAL',100000); INSERT INTO vietride_trip.trip_stops (trip_id,stop_id,order_index,estimated_arrival_time,actual_arrival_time,status,allow_pickup,allow_dropoff,distance_from_origin_km) VALUES ('${ids.trip}','${ids.tripStop}',1,now(),now(),'ARRIVED',true,true,10),('${ids.trip}','${ids.alternateTripStop}',2,now()+interval '1 hour',NULL,'PENDING',true,true,20),('${ids.pendingTrip}','${ids.pendingTripStop}',1,now()+interval '1 hour',NULL,'PENDING',true,true,10);`);

  psql('vietride_booking', `INSERT INTO vietride_booking.bookings (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_stop_id,dropoff_station_id,base_fare,total_amount,status,trip_snapshot_departure,trip_current_departure,confirmed_at) VALUES ('${ids.booking}','VR-20260720-D24A0001','${ids.passenger}','${ids.trip}','${ids.operator}','${ids.stop}','${ids.destinationStation}',100000,100000,'CONFIRMED',now()+interval '10 days',now()+interval '10 days',now()),('${ids.refusalBooking}','VR-20260720-D24A0002','${ids.passenger}','${ids.cancellationTrip}','${ids.operator}','${ids.stop}','${ids.destinationStation}',100000,100000,'CONFIRMED',now()+interval '12 days',now()+interval '12 days',now()),('${ids.countBooking}','VR-20260720-D24A0003','${ids.passenger}','${ids.trip}','${ids.operator}','${ids.tripStop}','${ids.destinationStation}',100000,100000,'CONFIRMED',now()+interval '10 days',now()+interval '10 days',now()); INSERT INTO vietride_booking.passengers (id,booking_id,seat_number,boarding_status) VALUES ('${ids.countPassenger}','${ids.countBooking}','A01','PENDING'); INSERT INTO vietride_booking.booking_pending_actions (id,booking_id,reason,deadline,metadata) VALUES ('${ids.action}','${ids.booking}','STOP_DISABLED',now()+interval '1 day','{"fallbackStationId":"${ids.fallbackStation}"}'),('${ids.refusalAction}','${ids.refusalBooking}','STOP_DISABLED',now()+interval '1 day','{"fallbackStationId":"${ids.fallbackStation}"}');`);
  console.log('PASS | isolated Day-24 Postman fixture seeded');
}

async function poll(label, probe, predicate, attempts = 20) {
  let last;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    last = probe();
    if (predicate(last)) {
      console.log(`PASS | ${label} | ${last}`);
      return last;
    }
    if (attempt + 1 < attempts) await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error(`${label} did not converge within 2 seconds; last=${last}`);
}

async function assertSideEffects() {
  const bookingState = psql('vietride_booking', `SELECT (SELECT pickup_station_id::text||'|'||resolved_action::text FROM vietride_booking.bookings b JOIN vietride_booking.booking_pending_actions a ON a.booking_id=b.id WHERE b.id='${ids.booking}')||'|'||(SELECT status::text||'|'||refund_override::text||'|'||resolved_action::text FROM vietride_booking.bookings b JOIN vietride_booking.booking_pending_actions a ON a.booking_id=b.id WHERE b.id='${ids.refusalBooking}')`);
  assert(bookingState === `${ids.fallbackStation}|ACCEPTED|CANCELLED|true|REJECTED`, `Booking/action side effects mismatch: ${bookingState}`);
  console.log(`PASS | Booking replacement/refusal state | ${bookingState}`);
  await poll('Trip Outbox published with stable event identity',
    () => psql('vietride_trip', `SELECT count(*) FILTER (WHERE event_type='trip.stop.disabled' AND id::text=payload->>'eventId' AND status='PUBLISHED')||'|'||count(*) FILTER (WHERE event_type='trip.stop.departed_with_pending' AND id::text=payload->>'eventId' AND status='PUBLISHED') FROM vietride_trip.outbox_events WHERE payload::text LIKE '%${ids.stop}%' OR payload::text LIKE '%${ids.tripStop}%'`),
    (value) => value === '1|1');
  await poll('RabbitMQ consumer produced assigned-driver notification',
    () => psql('vietride_notification', `SELECT count(*) FROM vietride_notification.notifications WHERE user_id='${ids.driver}' AND type='DRIVER_STOP_DEPARTED_WITH_PENDING'`),
    (value) => value === '1');
}

async function issueTokens() {
  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const privateKey = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  const userToken = (subject, role, operatorId) => new SignJWT({ role, ...(operatorId ? { operatorId } : {}), email: `${role.toLowerCase()}@day24.local`, hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid: settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(subject)
    .setIssuedAt().setExpirationTime('15m').sign(privateKey);
  const secret = containerEnv('vietride_gateway').INTERNAL_JWT_SECRET;
  assert(secret?.length >= 32, 'Running Gateway does not expose a valid INTERNAL_JWT_SECRET.');
  const internalJwt = await new SignJWT({ callerService: 'gateway', reqId: crypto.randomUUID() })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway').setAudience('vietride-internal').setSubject('day24-audit')
    .setIssuedAt().setExpirationTime('10m').sign(new TextEncoder().encode(secret));
  const [operatorAdmin, passenger, driver] = await Promise.all([
    userToken(ids.operatorAdmin, 'OPERATOR_ADMIN', ids.operator),
    userToken(ids.passenger, 'PASSENGER'),
    userToken(ids.driver, 'DRIVER', ids.operator),
  ]);
  return { operatorAdmin, passenger, driver, internalJwt };
}

function runNewman(tokens) {
  const temporaryDirectory = fs.mkdtempSync(path.join(root, 'TestResults/day24-newman-'));
  const runtimeEnvironmentPath = path.join(temporaryDirectory, 'environment.json');
  const runtimeCollectionPath = path.join(temporaryDirectory, 'collection.json');
  const runtimeEnvironment = structuredClone(environment);
  const runtimeCollection = JSON.parse(fs.readFileSync(path.join(root, collectionPath), 'utf8'));
  runtimeCollection.item = runtimeCollection.item.filter((item) => item.name === folder);
  assert(runtimeCollection.item.length === 1, `Postman folder is missing: ${folder}`);
  const credentials = {
    day24OperatorAdminAccessToken: tokens.operatorAdmin,
    day24PassengerAccessToken: tokens.passenger,
    day24DriverAccessToken: tokens.driver,
    day24InternalJwt: tokens.internalJwt,
  };
  for (const variable of runtimeEnvironment.values) {
    if (Object.hasOwn(credentials, variable.key)) variable.value = credentials[variable.key];
  }
  fs.writeFileSync(runtimeEnvironmentPath, JSON.stringify(runtimeEnvironment), 'utf8');
  fs.writeFileSync(runtimeCollectionPath, JSON.stringify(runtimeCollection), 'utf8');
  try {
    const runtimePath = path.relative(root, runtimeEnvironmentPath);
    const runtimeCollection = path.relative(root, runtimeCollectionPath);
    const command = process.platform === 'win32' ? 'cmd.exe' : 'npx';
    const args = process.platform === 'win32'
      ? ['/d', '/c', `npx --yes newman run ${runtimeCollection} -e ${runtimePath} --reporters cli`]
      : ['--yes', 'newman', 'run', runtimeCollection, '-e', runtimePath, '--reporters', 'cli'];
    const result = spawnSync(command, args, { cwd: root, encoding: 'utf8', stdio: 'inherit' });
    if (result.error || result.status !== 0) throw result.error ?? new Error(`Newman exited ${result.status}`);
  } finally {
    fs.rmSync(temporaryDirectory, { recursive: true, force: true });
  }
}

let failure;
try {
  cleanup();
  seed();
  const tokens = await issueTokens();
  runNewman(tokens);
  await assertSideEffects();
  console.log('PASS | authenticated Day-24 Newman boundary matrix');
} catch (error) {
  failure = error;
} finally {
  try {
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    cleanup();
    console.log('PASS | Day-24 fixture cleanup');
  } catch (cleanupError) {
    failure = failure ? new AggregateError([failure, cleanupError]) : cleanupError;
  }
}

if (failure) throw failure;
