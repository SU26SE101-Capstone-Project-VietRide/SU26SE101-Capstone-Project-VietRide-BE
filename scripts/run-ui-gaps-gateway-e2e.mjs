import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

export const UI25_FOLDER = 'UI Gaps - Gateway Real Stack';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const collectionPath = path.join(
  repoRoot,
  'docs/api/postman/vietride.postman_collection.json',
);
const environmentPath = path.join(
  repoRoot,
  'docs/api/postman/vietride.local.postman_environment.json',
);
const testResultsDirectory = path.join(repoRoot, 'TestResults');
const gatewayBaseUrl = process.env.UI25_GATEWAY_BASE_URL || 'http://localhost:3000';

const runtimeOnlyKeys = [
  'systemAdminAccessToken',
  'operatorAdminAccessToken',
  'operatorUserAccessToken',
  'operatorId',
  'ui25RunId',
  'ui25RouteId',
  'ui25TripId',
  'ui25BookingId',
  'ui25ParcelId',
  'ui25FromDate',
  'ui25ToDate',
  'ui25Month',
  'ui25ReportFromUtc',
  'ui25ReportToUtc',
  'ui25EffectiveFrom',
  'ui25AdminPolicyId',
  'ui25AdminPolicyVersion',
  'ui25OperatorPolicyId',
  'ui25OperatorPolicyVersion',
  'ui25AdminPolicyCreateKey',
  'ui25AdminPolicyUpdateKey',
  'ui25AdminPolicyDeleteKey',
  'ui25OperatorPolicyCreateKey',
  'ui25OperatorPolicyUpdateKey',
  'ui25OperatorPolicyDeleteKey',
  'ui25FareBatchKey',
  'ui25FareWrongRoleKey',
  'ui25PolicyValidationKey',
];

const requiredRouteMarkers = [
  '/v1/admin/reports/platform',
  '/v1/operator/trips',
  '/v1/operator/bookings',
  '/v1/admin/trip-settlements',
  '/v1/admin/platform-wallet/transactions',
  '/v1/admin/policies',
  '/v1/operator/policies',
  '/v1/operator/parcel-route-fares/{{ui25RouteId}}/batch',
  '/v1/operator/parcels',
  '/v1/operator/parcel-stats',
  '/v1/operator/booking-stats',
  '/v1/admin/booking-stats/aggregate',
  '/v1/admin/dashboard/summary',
  '/v1/admin/revenue/analytics',
  '/v1/operator/revenue/analytics',
  '/api-specs/booking',
  '/api-specs/parcel',
  '/api-specs/payment',
  '/internal/v1/reports/platform/bookings',
];

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function requestUrl(request) {
  if (typeof request?.url === 'string') return request.url;
  return request?.url?.raw || '';
}

function flattenRequests(items, result = []) {
  for (const item of items || []) {
    if (item.request) result.push(item);
    flattenRequests(item.item, result);
  }
  return result;
}

export function assertPostmanArtifacts() {
  const collection = readJson(collectionPath);
  const environment = readJson(environmentPath);
  const folder = collection.item?.find((item) => item.name === UI25_FOLDER);

  assert(folder, `Postman folder is missing: ${UI25_FOLDER}`);
  const requests = flattenRequests(folder.item);
  assert(requests.length >= 41, `UI-25 Postman matrix has only ${requests.length} requests`);

  const urls = requests.map((item) => requestUrl(item.request));
  const nonGatewayUrls = urls.filter((url) => !url.startsWith('{{baseUrl}}/'));
  const internalUrls = urls.filter((url) => url.includes('/internal/'));
  const serialized = JSON.stringify(folder);

  for (const marker of requiredRouteMarkers) {
    assert(serialized.includes(marker), `UI-25 route marker is missing: ${marker}`);
  }
  assert(
    !serialized.includes('{{$guid}}'),
    'UI-25 mutations must use runner-owned Idempotency-Key values for deterministic cleanup',
  );
  for (const item of requests) {
    if (!['POST', 'PUT', 'PATCH', 'DELETE'].includes(item.request.method)) continue;
    const headers = item.request.header || [];
    assert(
      headers.some((header) => header.key?.toLowerCase() === 'idempotency-key'),
      `Mutation is missing Idempotency-Key: ${item.name}`,
    );
  }

  const environmentKeys = (environment.values || []).map((item) => item.key);
  for (const key of runtimeOnlyKeys) {
    assert(environmentKeys.includes(key), `Postman environment key is missing: ${key}`);
  }

  const sensitiveKeys = new Set(
    runtimeOnlyKeys.filter(
      (key) =>
        /token|key|policyid|routeid|tripid|bookingid|parcelid|operatorid|runid/i.test(key) &&
        key !== 'operatorId',
    ),
  );
  const nonEmptySensitiveValues = (environment.values || [])
    .filter((item) => sensitiveKeys.has(item.key) && String(item.value || '').trim() !== '')
    .map((item) => item.key);

  return {
    requestCount: requests.length,
    environmentKeys,
    nonEmptySensitiveValues,
    nonGatewayUrls,
    internalUrls,
  };
}

export function buildRuntimeArtifacts(collection, environment, runtimeValues) {
  const runtimeCollection = structuredClone(collection);
  const runtimeEnvironment = structuredClone(environment);
  const folder = runtimeCollection.item?.find((item) => item.name === UI25_FOLDER);
  assert(folder, `Postman folder is missing: ${UI25_FOLDER}`);
  runtimeCollection.item = [folder];

  for (const [key, value] of Object.entries(runtimeValues)) {
    const current = runtimeEnvironment.values?.find((item) => item.key === key);
    if (current) {
      current.value = value;
      current.enabled = true;
    } else {
      runtimeEnvironment.values ??= [];
      runtimeEnvironment.values.push({ key, value, enabled: true });
    }
  }

  return { collection: runtimeCollection, environment: runtimeEnvironment };
}

function parseDotEnv() {
  const result = {};
  for (const line of fs.readFileSync(path.join(repoRoot, '.env'), 'utf8').split(/\r?\n/)) {
    const match = line.match(/^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/);
    if (!match) continue;
    result[match[1]] = match[2].trim().replace(/^(['"])(.*)\1$/, '$2');
  }
  return result;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    encoding: 'utf8',
    input: options.input,
    stdio: options.inherit ? 'inherit' : ['pipe', 'pipe', 'pipe'],
    env: options.env || process.env,
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    const detail = options.inherit ? '' : `: ${(result.stderr || '').trim().slice(0, 500)}`;
    throw new Error(`${command} exited with ${result.status}${detail}`);
  }
  return options.inherit ? '' : (result.stdout || '').trim();
}

function sqlLiteral(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

function psql(database, sql, capture = false) {
  const env = parseDotEnv();
  const output = run(
    'docker',
    [
      'exec',
      '-i',
      process.env.POSTGRES_CONTAINER || 'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      env.POSTGRES_USER || 'vietride',
      '-d',
      database,
      ...(capture ? ['-q', '-At'] : []),
    ],
    { input: sql },
  );
  return output;
}

async function api(method, pathname, { token, body, idempotencyKey } = {}) {
  const headers = { accept: 'application/json' };
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  if (idempotencyKey) headers['idempotency-key'] = idempotencyKey;
  const response = await fetch(`${gatewayBaseUrl}${pathname}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(30_000),
  });
  const json = await response.json().catch(() => null);
  return { response, json };
}

async function requireHealth(url) {
  const response = await fetch(url, { signal: AbortSignal.timeout(20_000) });
  assert.equal(response.status, 200, `Health check failed: ${url}`);
}

function ictDateParts(date = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date);
  const get = (type) => parts.find((part) => part.type === type).value;
  return { year: get('year'), month: get('month'), day: get('day') };
}

export function ui25TimeWindow(now = new Date()) {
  const vietnamTime = ictDateParts(now);
  const currentDate = `${vietnamTime.year}-${vietnamTime.month}-${vietnamTime.day}`;
  const dayStart = new Date(`${currentDate}T00:00:00+07:00`);
  const nextDayStart = new Date(dayStart.getTime() + 24 * 60 * 60 * 1000);
  const tomorrow = ictDateParts(nextDayStart);
  const tomorrowDate = `${tomorrow.year}-${tomorrow.month}-${tomorrow.day}`;

  return {
    currentDate,
    tomorrowDate,
    month: `${vietnamTime.year}-${vietnamTime.month}`,
    fixtureInstantUtc: new Date(dayStart.getTime() + 12 * 60 * 60 * 1000).toISOString(),
    reportFromUtc: dayStart.toISOString(),
    reportToUtc: nextDayStart.toISOString(),
    effectiveFrom: `${tomorrowDate}T00:00:00+07:00`,
  };
}

export function ui25RunTimeWindow(now, runId) {
  const time = ui25TimeWindow(now);
  const offsetSeconds = (Number.parseInt(runId.replaceAll('-', '').slice(0, 8), 16) % 3_600) + 1;
  return {
    ...time,
    reportFromUtc: new Date(
      new Date(time.reportFromUtc).getTime() + offsetSeconds * 1_000,
    ).toISOString(),
    reportToUtc: new Date(
      new Date(time.reportToUtc).getTime() - offsetSeconds * 1_000,
    ).toISOString(),
  };
}

function makeFixture(now = new Date()) {
  const runId = randomUUID();
  const time = ui25RunTimeWindow(now, runId);
  const suffix = runId.replaceAll('-', '').slice(0, 10);
  const phoneTail = String(Date.now()).slice(-7);
  const password = `Ui25-${suffix}-Aa1!`;

  return {
    ...time,
    runId,
    suffix,
    phoneTail,
    password,
    operatorId: randomUUID(),
    operatorSubscriptionId: randomUUID(),
    systemAdminId: null,
    operatorAdminId: null,
    operatorStaffId: null,
    passengerId: null,
    routeId: randomUUID(),
    originStationId: randomUUID(),
    destinationStationId: randomUUID(),
    vehicleTypeId: randomUUID(),
    vehicleId: randomUUID(),
    tripId: randomUUID(),
    bookingId: randomUUID(),
    bookingPassengerId: randomUUID(),
    bookingStatusHistoryId: randomUUID(),
    passengerBookingId: randomUUID(),
    passengerRecordId: randomUUID(),
    passengerTicketId: randomUUID(),
    passengerTicketCode: `VT-${time.currentDate.replaceAll('-', '')}-${suffix.slice(0, 8).toUpperCase()}`,
    passengerBookingCode: `VR-${time.currentDate.replaceAll('-', '')}-${suffix.slice(0, 8).toUpperCase()}`,
    bookingStatsId: randomUUID(),
    parcelId: randomUUID(),
    parcelStatusHistoryId: randomUUID(),
    subscriptionPaymentId: randomUUID(),
    subscriptionReferenceId: randomUUID(),
    settlementId: randomUUID(),
    walletTransactionId: randomUUID(),
    bookingLedgerId: randomUUID(),
    parcelLedgerId: randomUUID(),
    bookingLedgerEventId: randomUUID(),
    parcelLedgerEventId: randomUUID(),
    systemEmail: `ui25-system-${suffix}@example.test`,
    operatorEmail: `ui25-operator-${suffix}@example.test`,
    staffEmail: `ui25-staff-${suffix}@example.test`,
    passengerEmail: `ui25-passenger-${suffix}@example.test`,
    vehicleTypeCode: `UI25${suffix.slice(0, 4).toUpperCase()}`,
    vehicleTypeDisplayName: 'UI-25 Custom Coach',
    vehicleLicensePlate: '51B-25025',
    registrationKeys: [randomUUID(), randomUUID(), randomUUID(), randomUUID()],
    adminPolicyCreateKey: randomUUID(),
    adminPolicyUpdateKey: randomUUID(),
    adminPolicyDeleteKey: randomUUID(),
    operatorPolicyCreateKey: randomUUID(),
    operatorPolicyUpdateKey: randomUUID(),
    operatorPolicyDeleteKey: randomUUID(),
    fareBatchKey: randomUUID(),
    fareWrongRoleKey: randomUUID(),
    policyValidationKey: randomUUID(),
    adminPolicyId: null,
    operatorPolicyId: null,
  };
}

async function registerFixtureUser(email, password, displayName, phone, idempotencyKey) {
  const result = await api('POST', '/v1/auth/register', {
    body: { email, password, displayName, phone },
    idempotencyKey,
  });
  assert(
    [200, 201].includes(result.response.status),
    `Identity registration failed with ${result.response.status}`,
  );
}

function identityUserId(email) {
  const output = psql(
    'vietride_identity',
    `SET search_path TO vietride_identity, public;
SELECT id FROM users WHERE lower(email)=lower(${sqlLiteral(email)}) LIMIT 1;`,
    true,
  );
  assert.match(output, /^[0-9a-f-]{36}$/i, 'Registered Identity user was not persisted');
  return output;
}

async function login(email, password, expectedRole) {
  const result = await api('POST', '/v1/auth/login', { body: { email, password } });
  assert.equal(result.response.status, 200, `Identity login failed for ${expectedRole}`);
  assert.equal(result.json?.data?.user?.role, expectedRole, `Identity issued the wrong role`);
  assert(result.json?.data?.accessToken, `Identity issued no access token for ${expectedRole}`);
  return result.json.data.accessToken;
}

async function seedIdentity(fixture) {
  await registerFixtureUser(
    fixture.systemEmail,
    fixture.password,
    'UI-25 System Admin',
    `+8498${fixture.phoneTail}`,
    fixture.registrationKeys[0],
  );
  await registerFixtureUser(
    fixture.operatorEmail,
    fixture.password,
    'UI-25 Operator Admin',
    `+8497${fixture.phoneTail}`,
    fixture.registrationKeys[1],
  );
  await registerFixtureUser(
    fixture.staffEmail,
    fixture.password,
    'UI-25 Operator Staff',
    `+8496${fixture.phoneTail}`,
    fixture.registrationKeys[2],
  );
  await registerFixtureUser(
    fixture.passengerEmail,
    fixture.password,
    'UI-25 Passenger',
    `+8494${fixture.phoneTail}`,
    fixture.registrationKeys[3],
  );

  fixture.systemAdminId = identityUserId(fixture.systemEmail);
  fixture.operatorAdminId = identityUserId(fixture.operatorEmail);
  fixture.operatorStaffId = identityUserId(fixture.staffEmail);
  fixture.passengerId = identityUserId(fixture.passengerEmail);

  psql(
    'vietride_identity',
    `SET search_path TO vietride_identity, public;
BEGIN;
INSERT INTO operators (
  id,name,business_registration_number,tax_code,contact_email,contact_phone,
  registration_status,approved_at,is_active,logo_url,representative_name,representative_phone
) VALUES (
  '${fixture.operatorId}','UI-25 Real Stack Operator','UI25-${fixture.suffix}-BRN',
  'UI25-${fixture.suffix}-TAX','${fixture.operatorEmail}','+84950000025',
  'APPROVED',now(),true,'https://example.test/ui25-operator.png','UI-25 Representative','+84950000026'
);
INSERT INTO operator_subscriptions (
  id,operator_id,active_plan_id,status,started_at,expires_at,payment_method,billing_period
) VALUES (
  '${fixture.operatorSubscriptionId}','${fixture.operatorId}',
  '00000000-0000-0000-0000-000000000001','ACTIVE',now(),now() + interval '30 days',
  'WALLET','MONTHLY'
);
UPDATE users SET role='SYSTEM_ADMIN',status='ACTIVE',operator_id=NULL,deleted_at=NULL,
  failed_login_attempts=0,last_failed_login_at=NULL,locked_from_status=NULL
WHERE id='${fixture.systemAdminId}';
UPDATE users SET role='OPERATOR_ADMIN',status='ACTIVE',operator_id='${fixture.operatorId}',deleted_at=NULL,
  failed_login_attempts=0,last_failed_login_at=NULL,locked_from_status=NULL
WHERE id='${fixture.operatorAdminId}';
UPDATE users SET role='OPERATOR_STAFF',status='ACTIVE',operator_id='${fixture.operatorId}',deleted_at=NULL,
  failed_login_attempts=0,last_failed_login_at=NULL,locked_from_status=NULL
WHERE id='${fixture.operatorStaffId}';
UPDATE users SET role='PASSENGER',status='ACTIVE',operator_id=NULL,deleted_at=NULL,
  failed_login_attempts=0,last_failed_login_at=NULL,locked_from_status=NULL
WHERE id='${fixture.passengerId}';
COMMIT;`,
  );

  return {
    systemAdmin: await login(fixture.systemEmail, fixture.password, 'SYSTEM_ADMIN'),
    operatorAdmin: await login(fixture.operatorEmail, fixture.password, 'OPERATOR_ADMIN'),
    operatorStaff: await login(fixture.staffEmail, fixture.password, 'OPERATOR_STAFF'),
    passenger: await login(fixture.passengerEmail, fixture.password, 'PASSENGER'),
  };
}

function seedTrip(fixture) {
  psql(
    'vietride_trip',
    `SET search_path TO vietride_trip, public;
BEGIN;
INSERT INTO stations (id,name,slug,address_street,city,latitude,longitude,supports_shuttle,is_active)
VALUES
 ('${fixture.originStationId}','UI-25 Origin','ui25-origin-${fixture.suffix}','1 Origin','Hồ Chí Minh',10.77,106.70,false,true),
 ('${fixture.destinationStationId}','UI-25 Destination','ui25-destination-${fixture.suffix}','2 Destination','Đà Lạt',11.94,108.44,false,true);
INSERT INTO routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,is_active)
VALUES ('${fixture.routeId}','${fixture.operatorId}','UI-25 HCM - Đà Lạt','${fixture.originStationId}','${fixture.destinationStationId}',100000,true);
INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_system_defined,is_active)
VALUES ('${fixture.vehicleTypeId}','${fixture.vehicleTypeCode}','${fixture.vehicleTypeDisplayName}',40,false,true);
INSERT INTO vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
VALUES ('${fixture.vehicleId}','${fixture.operatorId}','${fixture.vehicleTypeId}','${fixture.vehicleLicensePlate}','{}'::jsonb,40,1000,20,'ACTIVE',true);
INSERT INTO trips (
 id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,
 completed_at,status,source,base_fare,max_cargo_weight_kg,seat_layout_snapshot_json
 ) VALUES (
  '${fixture.tripId}','${fixture.operatorId}','${fixture.routeId}','${fixture.vehicleId}','${fixture.operatorStaffId}',
  '${fixture.currentDate}T08:00:00+07:00','${fixture.currentDate}T11:00:00+07:00',
  '${fixture.currentDate}T11:00:00+07:00','COMPLETED','MANUAL',100000,1000,'{}'::jsonb
);
COMMIT;`,
  );
}

function seedBooking(fixture) {
  psql(
    'vietride_booking',
    `SET search_path TO vietride_booking, public;
BEGIN;
INSERT INTO bookings (
 id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,
 base_fare,discount_amount,total_amount,status,trip_snapshot_origin_name,trip_snapshot_dest_name,
 trip_snapshot_departure,trip_snapshot_route_name,trip_current_departure,confirmed_at,completed_at,
 buyer_display_name,buyer_email,buyer_phone,buyer_avatar_url,created_at,updated_at
) VALUES (
 '${fixture.bookingId}','UI25-${fixture.suffix.toUpperCase()}','${fixture.systemAdminId}','${fixture.tripId}',
 '${fixture.operatorId}','${fixture.originStationId}','${fixture.destinationStationId}',100000,0,100000,
 'COMPLETED','UI-25 Origin','UI-25 Destination','${fixture.currentDate}T08:00:00+07:00','UI-25 HCM - Đà Lạt',
 '${fixture.currentDate}T08:00:00+07:00','${fixture.currentDate}T09:00:00+07:00',
 '${fixture.currentDate}T11:00:00+07:00','UI-25 Buyer',
 '${fixture.systemEmail}','+84950000027','https://example.test/ui25-buyer.png',
 '${fixture.currentDate}T07:00:00+07:00','${fixture.fixtureInstantUtc}'
);
INSERT INTO passengers (id,booking_id,seat_number,boarding_status)
VALUES ('${fixture.bookingPassengerId}','${fixture.bookingId}','A01','BOARDED');
INSERT INTO booking_status_history (id,booking_id,status,occurred_at,source)
VALUES ('${fixture.bookingStatusHistoryId}','${fixture.bookingId}','COMPLETED',
        '${fixture.currentDate}T11:00:00+07:00','UI25_E2E');
INSERT INTO bookings (
 id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,dropoff_station_id,
 base_fare,discount_amount,total_amount,status,trip_snapshot_origin_name,trip_snapshot_dest_name,
 trip_snapshot_departure,trip_snapshot_route_name,trip_current_departure,confirmed_at,
 buyer_display_name,buyer_email,buyer_phone,created_at,updated_at
) VALUES (
 '${fixture.passengerBookingId}','${fixture.passengerBookingCode}','${fixture.passengerId}','${fixture.tripId}',
 '${fixture.operatorId}','${fixture.originStationId}','${fixture.destinationStationId}',100000,0,100000,
 'CONFIRMED','UI-25 Origin','UI-25 Destination','${fixture.currentDate}T08:00:00+07:00','UI-25 HCM - Đà Lạt',
 '${fixture.currentDate}T08:00:00+07:00','${fixture.currentDate}T07:30:00+07:00',
 'UI-25 Passenger','${fixture.passengerEmail}','+8494${fixture.phoneTail}',
 '${fixture.currentDate}T07:30:00+07:00','${fixture.fixtureInstantUtc}'
);
INSERT INTO passengers (id,booking_id,seat_number,boarding_status)
VALUES ('${fixture.passengerRecordId}','${fixture.passengerBookingId}','B02','PENDING');
INSERT INTO tickets (
 id,booking_id,passenger_id,ticket_code,seat_number,status,fare_amount,discount_amount,paid_amount,
 issued_at,created_at,updated_at
) VALUES (
 '${fixture.passengerTicketId}','${fixture.passengerBookingId}','${fixture.passengerRecordId}',
 '${fixture.passengerTicketCode}','B02','ISSUED',100000,0,100000,
 '${fixture.currentDate}T07:30:00+07:00','${fixture.currentDate}T07:30:00+07:00','${fixture.fixtureInstantUtc}'
);
INSERT INTO booking_stats (
 id,operator_id,operator_name,stat_date,trip_id,total_bookings,total_confirmed,total_cancelled,
 total_no_show,total_completed,total_revenue,total_refunded,total_seats_booked,updated_at
) VALUES (
 '${fixture.bookingStatsId}','${fixture.operatorId}','UI-25 Real Stack Operator','${fixture.currentDate}',
 '${fixture.tripId}',1,1,0,0,1,100000,0,1,'${fixture.fixtureInstantUtc}'
);
COMMIT;`,
  );
}

function assertTicketVehicle(item, fixture, vehicleSelector) {
  const vehicle = vehicleSelector(item);
  assert(vehicle, `Ticket history omitted vehicle for booking ${fixture.passengerBookingId}`);
  assert.equal(vehicle.licensePlate, fixture.vehicleLicensePlate);
  assert.equal(vehicle.vehicleType?.code, fixture.vehicleTypeCode);
  assert.equal(vehicle.vehicleType?.displayName, fixture.vehicleTypeDisplayName);
}

async function verifyPassengerTicketHistory(fixture, passengerToken) {
  const bookingResult = await api('GET', '/v1/bookings/history?page=1&pageSize=100', {
    token: passengerToken,
  });
  assert.equal(bookingResult.response.status, 200, 'Passenger Booking history request failed');
  assert.equal(bookingResult.json?.success, true, 'Passenger Booking history envelope failed');
  const bookingItems = bookingResult.json?.data?.items;
  assert(Array.isArray(bookingItems), 'Passenger Booking history returned no item array');
  const ownedBooking = bookingItems.find((item) => item.bookingId === fixture.passengerBookingId);
  assert(ownedBooking, 'Passenger Booking history omitted the owned fixture booking');
  assert(
    !bookingItems.some((item) => item.bookingId === fixture.bookingId),
    'Passenger Booking history leaked another user booking',
  );
  assert.equal(ownedBooking.bookingCode, fixture.passengerBookingCode);
  assert.equal(ownedBooking.tickets?.[0]?.ticketCode, fixture.passengerTicketCode);
  assertTicketVehicle(ownedBooking, fixture, (item) => item.vehicle);

  const facadeResult = await api(
    'GET',
    '/v1/passenger/history?type=TICKET&page=1&pageSize=100',
    { token: passengerToken },
  );
  assert.equal(facadeResult.response.status, 200, 'Passenger History TICKET request failed');
  assert.equal(facadeResult.json?.success, true, 'Passenger History TICKET envelope failed');
  const facadeItems = facadeResult.json?.data?.items;
  assert(Array.isArray(facadeItems), 'Passenger History TICKET returned no item array');
  const ownedFacadeItem = facadeItems.find((item) => item.id === fixture.passengerBookingId);
  assert(ownedFacadeItem, 'Passenger History TICKET omitted the owned fixture booking');
  assert(
    !facadeItems.some((item) => item.id === fixture.bookingId),
    'Passenger History TICKET leaked another user booking',
  );
  assert.equal(ownedFacadeItem.code, fixture.passengerBookingCode);
  assert.equal(ownedFacadeItem.ticket?.tickets?.[0]?.ticketCode, fixture.passengerTicketCode);
  assertTicketVehicle(ownedFacadeItem, fixture, (item) => item.ticket?.vehicle);
}

function seedParcel(fixture) {
  psql(
    'vietride_parcel',
    `SET search_path TO vietride_parcel, public;
BEGIN;
INSERT INTO parcels (
 id,parcel_code,sender_user_id,recipient_name,recipient_phone,recipient_email,operator_id,trip_id,
 description,photo_url,size_category,estimated_weight_kg,actual_weight_kg,delivery_method,
 deposit_amount,original_deposit_amount,deposit_percent,discount_amount,additional_amount,refund_amount,
 total_price_vnd,estimated_size_category,estimated_chargeable_weight_kg,estimated_dim_weight_kg,
 estimated_height_cm,estimated_length_cm,estimated_width_cm,estimated_volume_m3,
 estimated_gross_price_vnd,estimated_total_price_vnd,final_gross_price_vnd,final_total_price_vnd,
 deposit_required_vnd,deposit_paid_vnd,balance_required_vnd,balance_paid_vnd,
 status,loaded_at,unloaded_at,confirmed_at,created_at,updated_at,
 trip_snapshot_route_id,trip_snapshot_route_name,trip_snapshot_origin_station_name,
 trip_snapshot_destination_station_name,trip_snapshot_vehicle_id,trip_snapshot_vehicle_license_plate
) VALUES (
 '${fixture.parcelId}','UI25-P-${fixture.suffix.toUpperCase()}','${fixture.operatorAdminId}',
 'UI-25 Recipient','+84950000028','recipient-ui25@example.test','${fixture.operatorId}','${fixture.tripId}',
 'UI-25 parcel projection','https://example.test/ui25-parcel.png','MEDIUM',5.5,5.5,'TERMINAL_PICKUP',
 60000,60000,100,0,0,0,60000,'MEDIUM',5.5,0.1,20,30,25,0.015,
 60000,60000,60000,60000,60000,60000,0,0,'DELIVERY_CONFIRMED',
 '${fixture.currentDate}T09:00:00+07:00','${fixture.currentDate}T10:00:00+07:00',
 '${fixture.currentDate}T11:00:00+07:00','${fixture.currentDate}T07:00:00+07:00','${fixture.fixtureInstantUtc}',
 '${fixture.routeId}','UI-25 HCM - Đà Lạt','UI-25 Origin','UI-25 Destination',
 '${fixture.vehicleId}','51B-25025'
);
INSERT INTO parcel_status_history (id,parcel_id,status,occurred_at,actor_type,source,reason)
VALUES ('${fixture.parcelStatusHistoryId}','${fixture.parcelId}','DELIVERY_CONFIRMED',
        '${fixture.currentDate}T11:00:00+07:00','SYSTEM','STATUS_TRIGGER','UI-25 deterministic baseline');
COMMIT;`,
  );
}

function seedPayment(fixture) {
  psql(
    'vietride_payment',
    `SET search_path TO vietride_payment, public;
BEGIN;
INSERT INTO payments (
 id,reference_type,reference_id,operator_id,amount,method,status,idempotency_key,succeeded_at,context,created_at,updated_at
) VALUES (
 '${fixture.subscriptionPaymentId}','SUBSCRIPTION','${fixture.subscriptionReferenceId}','${fixture.operatorId}',
 300000,'WALLET','SUCCEEDED','ui25-${fixture.runId}','${fixture.fixtureInstantUtc}',
  '{}'::jsonb,'${fixture.fixtureInstantUtc}','${fixture.fixtureInstantUtc}'
);
INSERT INTO operator_trip_settlements (
 id,operator_id,trip_id,net_amount,trip_terminal_at,eligible_at,status,settlement_method,settled_at,
 settled_by_user_id,operator_contact_phone,operator_logo_url,operator_name,operator_snapshot_resolved,
 settled_by_display_name,settled_by_email,settled_by_role,settled_by_snapshot_resolved
) VALUES (
 '${fixture.settlementId}','${fixture.operatorId}','${fixture.tripId}',160000,
 '${fixture.currentDate}T11:00:00+07:00','${fixture.currentDate}T11:30:00+07:00',
 'SETTLED','ADMIN_MANUAL','${fixture.fixtureInstantUtc}','${fixture.systemAdminId}',
 '+84950000025','https://example.test/ui25-operator.png','UI-25 Real Stack Operator',true,
 'UI-25 System Admin','${fixture.systemEmail}','SYSTEM_ADMIN',true
);
INSERT INTO platform_wallet_transactions (
 id,type,amount,balance_before,balance_after,reference_type,reference_id,note,actor_type,
 actor_user_id,actor_display_name,actor_email,actor_role,actor_snapshot_resolved,created_at
) VALUES (
 '${fixture.walletTransactionId}','CREDIT',300000,0,300000,'SUBSCRIPTION_PAYMENT','${fixture.subscriptionReferenceId}',
 'UI-25 projection','USER','${fixture.systemAdminId}','UI-25 System Admin','${fixture.systemEmail}',
 'SYSTEM_ADMIN',true,'${fixture.fixtureInstantUtc}'
);
INSERT INTO operator_ledger_entries (
 id,operator_id,trip_id,entry_type,amount,reference_type,reference_id,source_event_id,note,created_at
) VALUES
 ('${fixture.bookingLedgerId}','${fixture.operatorId}','${fixture.tripId}','BOOKING_REVENUE',100000,
  'BOOKING','${fixture.bookingId}','${fixture.bookingLedgerEventId}','UI-25 booking revenue','${fixture.fixtureInstantUtc}'),
 ('${fixture.parcelLedgerId}','${fixture.operatorId}','${fixture.tripId}','PARCEL_REVENUE',60000,
  'PARCEL','${fixture.parcelId}','${fixture.parcelLedgerEventId}','UI-25 parcel revenue','${fixture.fixtureInstantUtc}');
COMMIT;`,
  );
}

function runtimeValues(fixture, tokens) {
  return {
    baseUrl: gatewayBaseUrl,
    systemAdminAccessToken: tokens.systemAdmin,
    operatorAdminAccessToken: tokens.operatorAdmin,
    operatorUserAccessToken: tokens.operatorStaff,
    operatorId: fixture.operatorId,
    ui25RunId: fixture.runId,
    ui25RouteId: fixture.routeId,
    ui25TripId: fixture.tripId,
    ui25BookingId: fixture.bookingId,
    ui25ParcelId: fixture.parcelId,
    ui25FromDate: fixture.currentDate,
    ui25ToDate: fixture.currentDate,
    ui25Month: fixture.month,
    ui25ReportFromUtc: fixture.reportFromUtc,
    ui25ReportToUtc: fixture.reportToUtc,
    ui25EffectiveFrom: fixture.effectiveFrom,
    ui25AdminPolicyId: '',
    ui25AdminPolicyVersion: '',
    ui25OperatorPolicyId: '',
    ui25OperatorPolicyVersion: '',
    ui25AdminPolicyCreateKey: fixture.adminPolicyCreateKey,
    ui25AdminPolicyUpdateKey: fixture.adminPolicyUpdateKey,
    ui25AdminPolicyDeleteKey: fixture.adminPolicyDeleteKey,
    ui25OperatorPolicyCreateKey: fixture.operatorPolicyCreateKey,
    ui25OperatorPolicyUpdateKey: fixture.operatorPolicyUpdateKey,
    ui25OperatorPolicyDeleteKey: fixture.operatorPolicyDeleteKey,
    ui25FareBatchKey: fixture.fareBatchKey,
    ui25FareWrongRoleKey: fixture.fareWrongRoleKey,
    ui25PolicyValidationKey: fixture.policyValidationKey,
  };
}

function forceNewmanFailure(runtimeCollection) {
  const folder = runtimeCollection.item[0];
  folder.event ??= [];
  folder.event.push({
    listen: 'test',
    script: {
      type: 'text/javascript',
      exec: ["pm.test('UI25 forced cleanup-path failure', () => pm.expect(false).to.equal(true));"],
    },
  });
}

function runNewman(fixture, tokens) {
  const sourceCollection = readJson(collectionPath);
  const sourceEnvironment = readJson(environmentPath);
  const runtime = buildRuntimeArtifacts(
    sourceCollection,
    sourceEnvironment,
    runtimeValues(fixture, tokens),
  );
  if (process.env.UI25_FORCE_NEWMAN_FAILURE === 'true') forceNewmanFailure(runtime.collection);

  fs.mkdirSync(testResultsDirectory, { recursive: true });
  const runtimeCollectionPath = path.join(
    testResultsDirectory,
    `ui25-${fixture.suffix}.postman_collection.json`,
  );
  const runtimeEnvironmentPath = path.join(
    testResultsDirectory,
    `ui25-${fixture.suffix}.postman_environment.json`,
  );
  fs.writeFileSync(runtimeCollectionPath, `${JSON.stringify(runtime.collection, null, 2)}\n`, 'utf8');
  fs.writeFileSync(runtimeEnvironmentPath, `${JSON.stringify(runtime.environment, null, 2)}\n`, 'utf8');

  try {
    const newmanArgs = [
      '--yes',
      'newman',
      'run',
      runtimeCollectionPath,
      '-e',
      runtimeEnvironmentPath,
      '--export-environment',
      runtimeEnvironmentPath,
      '--reporters',
      'cli',
    ];
    if (process.platform === 'win32') {
      const npxCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js');
      run(process.execPath, [npxCli, ...newmanArgs], { inherit: true });
    } else {
      run('npx', newmanArgs, { inherit: true });
    }

    const finalEnvironment = readJson(runtimeEnvironmentPath);
    const values = Object.fromEntries(finalEnvironment.values.map((item) => [item.key, item.value]));
    fixture.adminPolicyId = values.ui25AdminPolicyId;
    fixture.operatorPolicyId = values.ui25OperatorPolicyId;
    assert.match(fixture.adminPolicyId, /^[0-9a-f-]{36}$/i);
    assert.match(fixture.operatorPolicyId, /^[0-9a-f-]{36}$/i);
  } finally {
    fs.rmSync(runtimeCollectionPath, { force: true });
    fs.rmSync(runtimeEnvironmentPath, { force: true });
  }
}

function verifyPersistence(fixture) {
  const policyIds = [fixture.adminPolicyId, fixture.operatorPolicyId].filter(Boolean);
  assert.equal(policyIds.length, 2, 'Newman did not return both Policy IDs');
  const policyList = policyIds.map(sqlLiteral).join(',');
  const auditCount = Number(
    psql(
      'vietride_rag',
      `SET search_path TO vietride_rag, public;
SELECT count(*) FROM policy_audit_logs WHERE policy_id IN (${policyList});`,
      true,
    ),
  );
  assert.equal(auditCount, 6, 'Policy audit cardinality is not create/update/delete per tenant');

  const fareCount = Number(
    psql(
      'vietride_parcel',
      `SET search_path TO vietride_parcel, public;
SELECT count(*) FROM parcel_route_fares
WHERE route_id='${fixture.routeId}' AND operator_id='${fixture.operatorId}'
  AND size_category IN ('SMALL','MEDIUM','LARGE');`,
      true,
    ),
  );
  assert.equal(fareCount, 3, 'Parcel fare batch did not persist all requested sizes');
}

function ownedIdempotencyOperations(fixture) {
  return {
    identity: fixture.registrationKeys,
    parcel: [fixture.fareBatchKey, fixture.fareWrongRoleKey],
    rag: [
      fixture.adminPolicyCreateKey,
      fixture.adminPolicyUpdateKey,
      fixture.adminPolicyDeleteKey,
      fixture.operatorPolicyCreateKey,
      fixture.operatorPolicyUpdateKey,
      fixture.operatorPolicyDeleteKey,
      fixture.policyValidationKey,
    ],
  };
}

function redisBaseArgs() {
  const env = parseDotEnv();
  const redisAuth = env.REDIS_PASSWORD || '';
  const baseArgs = ['exec'];
  if (redisAuth) baseArgs.push('-e', `REDISCLI_AUTH=${redisAuth}`);
  baseArgs.push(process.env.REDIS_CONTAINER || 'vietride_redis', 'redis-cli', '--raw');
  return baseArgs;
}

function idempotencyRedisKeys(service, operationId) {
  const normalized = operationId.toLowerCase();
  const hash = createHash('sha256').update(normalized).digest('hex').toUpperCase();
  return [
    `${service}:idem:${normalized}`,
    `${service}:idem:v2:processing:${hash}`,
    `${service}:idem:v2:response:${hash}`,
  ];
}

function redisOwnedKeys(ownedOperations) {
  return Object.entries(ownedOperations).flatMap(([service, operationIds]) =>
    operationIds.filter(Boolean).flatMap((operationId) => idempotencyRedisKeys(service, operationId)),
  );
}

function redisDeleteOwnedKeys(ownedOperations) {
  const keys = redisOwnedKeys(ownedOperations);
  if (keys.length) run('docker', [...redisBaseArgs(), 'DEL', ...keys]);
}

function redisOwnedResidue(ownedOperations) {
  const baseArgs = redisBaseArgs();
  const residue = new Set();
  for (const key of redisOwnedKeys(ownedOperations)) {
    for (const match of run('docker', [...baseArgs, '--scan', '--pattern', key])
      .split(/\r?\n/)
      .filter(Boolean)) {
      residue.add(match);
    }
  }
  return [...residue];
}

function resolveFixtureUserIds(fixture) {
  const rows = psql(
    'vietride_identity',
    `SET search_path TO vietride_identity, public;
SELECT lower(email) || '|' || id
FROM users
WHERE lower(email) IN (lower(${sqlLiteral(fixture.systemEmail)}),lower(${sqlLiteral(
      fixture.operatorEmail,
    )}),lower(${sqlLiteral(fixture.staffEmail)}),lower(${sqlLiteral(fixture.passengerEmail)}));`,
    true,
  )
    .split(/\r?\n/)
    .filter(Boolean);
  const idsByEmail = new Map(rows.map((row) => row.split('|')));
  fixture.systemAdminId ??= idsByEmail.get(fixture.systemEmail.toLowerCase()) || null;
  fixture.operatorAdminId ??= idsByEmail.get(fixture.operatorEmail.toLowerCase()) || null;
  fixture.operatorStaffId ??= idsByEmail.get(fixture.staffEmail.toLowerCase()) || null;
  fixture.passengerId ??= idsByEmail.get(fixture.passengerEmail.toLowerCase()) || null;
}

function runCleanupStages(stages) {
  const errors = [];
  for (const [name, action] of stages) {
    try {
      action();
    } catch (error) {
      errors.push(new Error(`${name}: ${error instanceof Error ? error.message : String(error)}`));
    }
  }
  if (errors.length) throw new AggregateError(errors, 'One or more UI-25 cleanup stages failed');
}

function cleanup(fixture) {
  const policyIds = [fixture.adminPolicyId, fixture.operatorPolicyId].filter(Boolean);
  const policyIdFilter = policyIds.length
    ? `id IN (${policyIds.map(sqlLiteral).join(',')}) OR `
    : '';
  const ragOperationIds = ownedIdempotencyOperations(fixture).rag;
  runCleanupStages([
    ['Identity fixture ID resolution', () => resolveFixtureUserIds(fixture)],
    ['RAG policies and idempotency rows', () => psql(
      'vietride_rag',
      `SET search_path TO vietride_rag, public;
BEGIN;
SET LOCAL session_replication_role = replica;
CREATE TEMP TABLE ui25_owned_policies ON COMMIT DROP AS
  SELECT id FROM policies
  WHERE ${policyIdFilter}title LIKE ${sqlLiteral(`UI25-${fixture.runId}-%`)};
DELETE FROM policy_audit_logs WHERE policy_id IN (SELECT id FROM ui25_owned_policies);
DELETE FROM policies WHERE id IN (SELECT id FROM ui25_owned_policies);
DELETE FROM idempotency_operations WHERE operation_id IN (${ragOperationIds
        .map(sqlLiteral)
        .join(',')});
COMMIT;`,
    )],
    ['Payment fixtures', () => psql(
      'vietride_payment',
      `SET search_path TO vietride_payment, public;
BEGIN;
DELETE FROM operator_ledger_entries WHERE id IN ('${fixture.bookingLedgerId}','${fixture.parcelLedgerId}');
DELETE FROM operator_trip_settlements WHERE id='${fixture.settlementId}';
DELETE FROM platform_wallet_transactions WHERE id='${fixture.walletTransactionId}';
DELETE FROM payments WHERE id='${fixture.subscriptionPaymentId}';
COMMIT;`,
    )],
    ['Parcel fixtures', () => psql(
      'vietride_parcel',
      `SET search_path TO vietride_parcel, public;
BEGIN;
SET LOCAL session_replication_role = replica;
DELETE FROM parcel_status_history WHERE parcel_id='${fixture.parcelId}';
DELETE FROM platform_parcel_stats WHERE parcel_id='${fixture.parcelId}';
DELETE FROM parcel_route_fares WHERE route_id='${fixture.routeId}' AND operator_id='${fixture.operatorId}';
DELETE FROM parcels WHERE id='${fixture.parcelId}';
COMMIT;`,
    )],
    ['Booking fixtures', () => psql(
      'vietride_booking',
      `SET search_path TO vietride_booking, public;
BEGIN;
DELETE FROM tickets WHERE id='${fixture.passengerTicketId}'
   OR booking_id IN ('${fixture.bookingId}','${fixture.passengerBookingId}');
DELETE FROM booking_status_history WHERE booking_id IN ('${fixture.bookingId}','${fixture.passengerBookingId}');
DELETE FROM passengers WHERE booking_id IN ('${fixture.bookingId}','${fixture.passengerBookingId}');
DELETE FROM platform_booking_stats WHERE booking_id='${fixture.bookingId}';
DELETE FROM booking_stats WHERE id='${fixture.bookingStatsId}';
DELETE FROM bookings WHERE id IN ('${fixture.bookingId}','${fixture.passengerBookingId}');
COMMIT;`,
    )],
    ['Trip fixtures', () => psql(
      'vietride_trip',
      `SET search_path TO vietride_trip, public;
BEGIN;
DELETE FROM vietride_trip.platform_trip_stats WHERE trip_id='${fixture.tripId}';
DELETE FROM trip_seats WHERE trip_id='${fixture.tripId}';
DELETE FROM trips WHERE id='${fixture.tripId}';
DELETE FROM vehicles WHERE id='${fixture.vehicleId}';
DELETE FROM vehicle_types WHERE id='${fixture.vehicleTypeId}';
DELETE FROM routes WHERE id='${fixture.routeId}';
DELETE FROM stations WHERE id IN ('${fixture.originStationId}','${fixture.destinationStationId}');
COMMIT;`,
    )],
    ['Identity fixtures', () => {
      const userIds = [
        fixture.systemAdminId,
        fixture.operatorAdminId,
        fixture.operatorStaffId,
        fixture.passengerId,
      ].filter(Boolean);
      const list = userIds.length
        ? userIds.map(sqlLiteral).join(',')
        : "'00000000-0000-0000-0000-000000000000'";
      psql(
      'vietride_identity',
      `SET search_path TO vietride_identity, public;
BEGIN;
CREATE TEMP TABLE ui25_owned_users ON COMMIT DROP AS
  SELECT id FROM users
  WHERE lower(email) IN (lower(${sqlLiteral(fixture.systemEmail)}),lower(${sqlLiteral(
        fixture.operatorEmail,
      )}),lower(${sqlLiteral(fixture.staffEmail)}),lower(${sqlLiteral(fixture.passengerEmail)}));
DELETE FROM refresh_tokens WHERE user_id IN (SELECT id FROM ui25_owned_users);
DELETE FROM email_verification_tokens WHERE user_id IN (SELECT id FROM ui25_owned_users);
DELETE FROM activity_logs WHERE user_id IN (SELECT id FROM ui25_owned_users);
DELETE FROM outbox_events WHERE payload::text LIKE '%${fixture.runId}%'
   OR ${userIds.length
    ? userIds.map((id) => `payload::text LIKE '%${id}%'`).join(' OR ')
    : 'false'};
DELETE FROM users WHERE id IN (SELECT id FROM ui25_owned_users) OR id IN (${list});
DELETE FROM operator_subscriptions WHERE id='${fixture.operatorSubscriptionId}'
   OR operator_id='${fixture.operatorId}';
DELETE FROM operators WHERE id='${fixture.operatorId}';
COMMIT;`,
      );
    }],
    ['Redis idempotency artifacts', () => redisDeleteOwnedKeys(ownedIdempotencyOperations(fixture))],
  ]);
}

function verifyCleanup(fixture) {
  const policyIds = [fixture.adminPolicyId, fixture.operatorPolicyId].filter(Boolean);
  const policyIdsSql = policyIds.length
    ? policyIds.map(sqlLiteral).join(',')
    : "'00000000-0000-0000-0000-000000000000'";
  const userIds = [
    fixture.systemAdminId,
    fixture.operatorAdminId,
    fixture.operatorStaffId,
    fixture.passengerId,
  ].filter(Boolean);
  const userIdsSql = userIds.length
    ? userIds.map(sqlLiteral).join(',')
    : "'00000000-0000-0000-0000-000000000000'";
  const residue = {
    identity: Number(
      psql(
        'vietride_identity',
        `SET search_path TO vietride_identity, public;
SELECT
 (SELECT count(*) FROM operators WHERE id='${fixture.operatorId}') +
 (SELECT count(*) FROM operator_subscriptions WHERE id='${fixture.operatorSubscriptionId}') +
 (SELECT count(*) FROM users WHERE email IN (${sqlLiteral(fixture.systemEmail)},${sqlLiteral(
          fixture.operatorEmail,
        )},${sqlLiteral(fixture.staffEmail)},${sqlLiteral(fixture.passengerEmail)}));`,
        true,
      ),
    ),
    trip: Number(
      psql(
        'vietride_trip',
        `SELECT
 (SELECT count(*) FROM vietride_trip.trips WHERE id='${fixture.tripId}') +
 (SELECT count(*) FROM vietride_trip.routes WHERE id='${fixture.routeId}') +
 (SELECT count(*) FROM vietride_trip.vehicles WHERE id='${fixture.vehicleId}');`,
        true,
      ),
    ),
    booking: Number(
      psql(
        'vietride_booking',
        `SELECT
 (SELECT count(*) FROM vietride_booking.bookings
    WHERE id IN ('${fixture.bookingId}','${fixture.passengerBookingId}')) +
 (SELECT count(*) FROM vietride_booking.passengers WHERE id='${fixture.passengerRecordId}') +
 (SELECT count(*) FROM vietride_booking.tickets WHERE id='${fixture.passengerTicketId}');`,
        true,
      ),
    ),
    payment: Number(
      psql(
        'vietride_payment',
        `SELECT
 (SELECT count(*) FROM vietride_payment.payments WHERE id='${fixture.subscriptionPaymentId}') +
 (SELECT count(*) FROM vietride_payment.operator_trip_settlements WHERE id='${fixture.settlementId}') +
 (SELECT count(*) FROM vietride_payment.operator_ledger_entries
    WHERE id IN ('${fixture.bookingLedgerId}','${fixture.parcelLedgerId}'));`,
        true,
      ),
    ),
    parcel: Number(
      psql(
        'vietride_parcel',
        `SELECT
 (SELECT count(*) FROM vietride_parcel.parcels WHERE id='${fixture.parcelId}') +
 (SELECT count(*) FROM vietride_parcel.parcel_route_fares WHERE route_id='${fixture.routeId}');`,
        true,
      ),
    ),
    rag: Number(
      psql(
        'vietride_rag',
        `SELECT
 (SELECT count(*) FROM vietride_rag.policies
    WHERE id IN (${policyIdsSql}) OR created_by_user_id IN (${userIdsSql})) +
 (SELECT count(*) FROM vietride_rag.policy_audit_logs WHERE policy_id IN (${policyIdsSql})) +
 (SELECT count(*) FROM vietride_rag.idempotency_operations
     WHERE operation_id IN (${ownedIdempotencyOperations(fixture).rag
      .map(sqlLiteral)
      .join(',')}));`,
        true,
      ),
    ),
    redis: redisOwnedResidue(ownedIdempotencyOperations(fixture)).length,
  };
  assert.equal(
    Object.values(residue).reduce((sum, count) => sum + count, 0),
    0,
    `UI-25 cleanup left fixture residue: ${JSON.stringify(residue)}`,
  );
}

async function main() {
  const staticResult = assertPostmanArtifacts();
  if (process.argv.includes('--static-only')) {
    console.log(`PASS | UI-25 Postman static matrix (${staticResult.requestCount} requests)`);
    return;
  }

  await Promise.all([
    requireHealth(`${gatewayBaseUrl}/health`),
    requireHealth(`${gatewayBaseUrl}/v1/identity/health`),
    requireHealth(`${gatewayBaseUrl}/v1/trip/health`),
    requireHealth(`${gatewayBaseUrl}/v1/booking/health`),
    requireHealth(`${gatewayBaseUrl}/v1/payment/health`),
    requireHealth(`${gatewayBaseUrl}/v1/parcel/health`),
    requireHealth('http://localhost:3003/health'),
  ]);

  const fixture = makeFixture();
  let tokens;
  let liveError;
  try {
    tokens = await seedIdentity(fixture);
    seedTrip(fixture);
    seedBooking(fixture);
    seedParcel(fixture);
    seedPayment(fixture);
    await verifyPassengerTicketHistory(fixture, tokens.passenger);
    runNewman(fixture, tokens);
    verifyPersistence(fixture);
  } catch (error) {
    liveError = error;
  } finally {
    try {
      cleanup(fixture);
      verifyCleanup(fixture);
      console.log('UI25_CLEANUP_SUMMARY={"status":"PASS","dbResidue":0,"redisResidue":0}');
    } catch (cleanupError) {
      if (liveError) liveError = new AggregateError([liveError, cleanupError], 'UI-25 run and cleanup failed');
      else liveError = cleanupError;
    }
  }

  if (liveError) throw liveError;
  console.log(
    `UI25_REDACTED_SUMMARY=${JSON.stringify({
      status: 'PASS',
      requests: staticResult.requestCount,
      auth: 'Identity login',
      gatewayOnly: true,
      cleanupResidue: 0,
    })}`,
  );
}

const invokedDirectly = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invokedDirectly) {
  main().catch((error) => {
    const messages =
      error instanceof AggregateError
        ? error.errors.map((item) => (item instanceof Error ? item.message : String(item)))
        : [error instanceof Error ? error.message : String(error)];
    console.error(`UI25_FAILED=${JSON.stringify(messages)}`);
    process.exitCode = 1;
  });
}
