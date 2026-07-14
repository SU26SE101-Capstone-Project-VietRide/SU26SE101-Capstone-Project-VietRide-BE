import { execFileSync, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const gateway = 'http://localhost:3000';
const tag = `codex-e2e-${Date.now()}`;
const operatorId = '10000000-0000-0000-0000-000000000009';
const operatorAdminId = '10000000-0000-0000-0000-0000000000a9';
const otherOperatorId = '10000000-0000-0000-0000-000000000007';
const otherOperatorAdminId = '10000000-0000-0000-0000-0000000000a7';
const systemAdminId = '31000000-0000-4000-8000-000000000106';
const passengerId = crypto.randomUUID();
const outboundRouteId = crypto.randomUUID();
const returnRouteId = crypto.randomUUID();
const stopTripId = crypto.randomUUID();
const paymentTripId = crypto.randomUUID();
const roundOutboundTripId = crypto.randomUUID();
const roundReturnTripId = crypto.randomUUID();
const createdStationIds = [];
const createdStopIds = [];
const bookingIds = [];
const paymentReferenceIds = [];
let platformBalanceBefore = '0';
let operatorToken;
let otherOperatorToken;
let systemAdminToken;
let passengerToken;
let vnpaySecret;

function pass(label, evidence = '') {
  console.log(`PASS | ${label}${evidence ? ` | ${evidence}` : ''}`);
}

function fail(message) {
  throw new Error(message);
}

function runSql(database, sql, capture = false) {
  const result = spawnSync(
    'docker',
    ['exec', '-i', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, ...(capture ? ['-At'] : [])],
    { cwd: root, input: sql, encoding: 'utf8', stdio: capture ? ['pipe', 'pipe', 'inherit'] : ['pipe', 'inherit', 'inherit'] },
  );
  if (result.error || result.status !== 0) fail(`psql failed for ${database} (status=${result.status ?? 1})`);
  return capture ? result.stdout.trim() : undefined;
}

function scalar(database, sql) {
  return runSql(database, `${sql.replace(/;?\s*$/, '')};`, true).split(/\r?\n/).filter(Boolean).at(-1) ?? '';
}

async function waitFor(label, check, attempts = 40, delayMs = 500) {
  let last;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      last = await check();
      if (last) return last;
    } catch (error) {
      last = error.message;
    }
    if (attempt < attempts) await new Promise((resolve) => setTimeout(resolve, delayMs));
  }
  fail(`${label} did not converge (${String(last)})`);
}

async function api(method, url, token, body, expectedStatus, idempotency = false) {
  const response = await fetch(`${gateway}${url}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(idempotency ? { 'Idempotency-Key': crypto.randomUUID() } : {}),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  const text = await response.text();
  let json;
  try { json = text ? JSON.parse(text) : undefined; } catch { json = undefined; }
  if (response.status !== expectedStatus) {
    fail(`${method} ${url}: expected HTTP ${expectedStatus}, got ${response.status}: ${text.slice(0, 500)}`);
  }
  return json;
}

async function issueToken({ subject, role, email, operator }) {
  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const key = await importPKCS8(settings.IdentityJwt.PrivateKey, 'RS256');
  const payload = { role, email, hasPhone: 'true' };
  if (operator) payload.operatorId = operator;
  return new SignJWT(payload)
    .setProtectedHeader({ alg: 'RS256', kid: settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
}

function containerEnv(name) {
  const output = execFileSync('docker', ['inspect', '--format', '{{range .Config.Env}}{{println .}}{{end}}', name], { encoding: 'utf8' });
  return Object.fromEntries(output.split(/\r?\n/).filter(Boolean).map((line) => {
    const index = line.indexOf('=');
    return [line.slice(0, index), line.slice(index + 1)];
  }));
}

async function restartPayment(tmnCode = '', hashSecret = '') {
  const internalJwt = containerEnv('vietride_gateway').INTERNAL_JWT_SECRET;
  if (!internalJwt || internalJwt.length < 32) fail('Running Gateway does not expose a valid INTERNAL_JWT_SECRET.');
  const env = {
    ...process.env,
    INTERNAL_JWT_SECRET: internalJwt,
    VNPAY_TMN_CODE: tmnCode,
    VNPAY_HASH_SECRET: hashSecret,
    VNPAY_BASE_URL: 'https://sandbox.vnpayment.vn/paymentv2/vpcpay.html',
    VNPAY_RETURN_URL: 'https://app.vietride.online/payments/return',
    VNPAY_IPN_URL: 'https://api.vietride.online/v1/payments/vnpay-ipn',
    VNPAY_PAYMENT_TIMEOUT_MINUTES: '10',
  };
  const result = spawnSync(
    'docker',
    ['compose', '-f', 'infra/docker/docker-compose.yml', 'up', '-d', '--force-recreate', '--no-deps', 'payment'],
    { cwd: root, env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
  );
  if (result.error || result.status !== 0) fail(`Payment restart failed: ${result.stderr}`);
  await waitFor('Payment health', async () => {
    try { return (await fetch('http://localhost:5004/health')).ok; } catch { return false; }
  }, 60, 500);
}

function sortedVnPayQuery(values) {
  const sorted = Object.entries(values)
    .filter(([key, value]) => key.toLowerCase() !== 'vnp_securehash' && key.toLowerCase() !== 'vnp_securehashtype' && value !== '')
    .sort(([left], [right]) => left.localeCompare(right));
  return new URLSearchParams(sorted).toString();
}

function signVnPay(values, secret = vnpaySecret) {
  return crypto.createHmac('sha512', secret).update(sortedVnPayQuery(values)).digest('hex');
}

async function sendIpn(values, expectedRspCode) {
  const parameters = { ...values, vnp_SecureHash: signVnPay(values) };
  const response = await fetch(`${gateway}/v1/payments/vnpay-ipn?${new URLSearchParams(parameters)}`);
  const body = await response.json();
  if (response.status !== 200 || body.RspCode !== expectedRspCode) {
    fail(`VNPay IPN expected HTTP 200/RspCode ${expectedRspCode}, got ${response.status}/${JSON.stringify(body)}`);
  }
  return body;
}

function paymentRow(paymentId) {
  return scalar(
    'vietride_payment',
    `select vnpay_txn_ref||'|'||amount||'|'||status||'|'||reference_type||'|'||reference_id from vietride_payment.payments where id='${paymentId}'`,
  ).split('|');
}

async function pollBookingStatus(bookingId, expected) {
  return waitFor(`booking ${bookingId} -> ${expected}`, async () => {
    const result = await api('GET', `/v1/bookings/${bookingId}`, passengerToken, undefined, 200);
    return result?.data?.status === expected ? result.data : false;
  }, 50, 400);
}

async function createStation(name, latitude, longitude) {
  const result = await api('POST', '/v1/operator/stations', operatorToken, {
    name,
    city: 'Ho Chi Minh City',
    province: 'Ho Chi Minh',
    latitude,
    longitude,
    addressStreet: `${tag} address`,
    contactPhone: '+84909090009',
    contactEmail: `${tag}@example.test`,
    supportsShuttle: false,
    displayNameOverride: `${name} counter`,
    counterLocation: 'Counter A',
    instructions: 'E2E only',
  }, 201);
  const stationId = result?.data?.stationId;
  if (!stationId) fail(`Station creation did not return stationId: ${JSON.stringify(result)}`);
  createdStationIds.push(stationId);
  return stationId;
}

async function createStop(name, latitude, longitude) {
  const result = await api('POST', '/v1/operator/stops', operatorToken, {
    name,
    latitude,
    longitude,
    description: `${tag} description`,
    address: `${tag} stop address`,
    googlePlaceId: `${tag}-${createdStopIds.length}`,
  }, 201);
  const stopId = result?.data?.id;
  if (!stopId) fail(`Stop creation did not return id: ${JSON.stringify(result)}`);
  createdStopIds.push(stopId);
  return stopId;
}

function seedIdentity() {
  runSql('vietride_identity', `
    insert into vietride_identity.users (id,email,phone,password_hash,display_name,role,status)
    values ('${passengerId}','${tag}@example.test','+84908880001',null,'${tag} passenger','PASSENGER'::user_role,'ACTIVE'::user_status);
  `);
}

function seedTrips(originStationId, destinationStationId, stopId) {
  const vehicle = scalar('vietride_trip', `select vehicle_id||'|'||driver_user_id from vietride_trip.trips where operator_id='${operatorId}' limit 1`).split('|');
  if (vehicle.length !== 2) fail('No existing operator vehicle/driver fixture is available.');
  const [vehicleId, driverUserId] = vehicle;
  runSql('vietride_trip', `
    begin;
    insert into vietride_trip.routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,total_distance_km,estimated_duration_minutes,is_active)
    values
      ('${outboundRouteId}','${operatorId}','${tag} outbound','${originStationId}','${destinationStationId}',125000,320,360,true),
      ('${returnRouteId}','${operatorId}','${tag} return','${destinationStationId}','${originStationId}',125000,320,360,true);
    update vietride_trip.routes set return_route_id='${returnRouteId}' where id='${outboundRouteId}';
    update vietride_trip.routes set return_route_id='${outboundRouteId}' where id='${returnRouteId}';
    insert into vietride_trip.trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare)
    values
      ('${stopTripId}','${operatorId}','${outboundRouteId}','${vehicleId}','${driverUserId}',now()+interval '10 days',now()+interval '10 days 6 hours','SCHEDULED','MANUAL',125000),
      ('${paymentTripId}','${operatorId}','${outboundRouteId}','${vehicleId}','${driverUserId}',now()+interval '11 days',now()+interval '11 days 6 hours','SCHEDULED','MANUAL',125000),
      ('${roundOutboundTripId}','${operatorId}','${outboundRouteId}','${vehicleId}','${driverUserId}',now()+interval '12 days',now()+interval '12 days 6 hours','SCHEDULED','MANUAL',125000),
      ('${roundReturnTripId}','${operatorId}','${returnRouteId}','${vehicleId}','${driverUserId}',now()+interval '14 days',now()+interval '14 days 6 hours','SCHEDULED','MANUAL',125000);
    insert into vietride_trip.trip_seats (trip_id,seat_number)
    values
      ('${stopTripId}','DUP1'),('${stopTripId}','MISS1'),('${stopTripId}','WALLET1'),
      ('${paymentTripId}','ONE1'),('${paymentTripId}','AMOUNT1'),('${paymentTripId}','TIMEOUT1'),
      ('${roundOutboundTripId}','GROUP1'),('${roundOutboundTripId}','GROUPFAIL1'),
      ('${roundReturnTripId}','GROUP1'),('${roundReturnTripId}','GROUPFAIL1');
    insert into vietride_trip.trip_stops
      (trip_id,stop_id,order_index,estimated_arrival_time,status,allow_pickup,allow_dropoff,distance_from_origin_km)
    values ('${stopTripId}','${stopId}',1,now()+interval '10 days 2 hours','PENDING',true,true,42.50);
    insert into vietride_trip.trip_stop_fares (trip_id,stop_id,fare_from_this_stop)
    values ('${stopTripId}','${stopId}',99000);
    commit;
  `);
}

function seedWallet() {
  runSql('vietride_payment', `
    insert into vietride_payment.wallets (user_id,balance,currency)
    values ('${passengerId}',1000000,'VND');
  `);
}

async function testStationAndStopApis() {
  const origin = await createStation(`${tag} Origin`, 20.1001, 100.1001);
  const destination = await createStation(`${tag} Destination`, 20.3001, 100.3001);
  const disposable = await createStation(`${tag} Disposable`, 20.5001, 100.5001);

  const patched = await api('PATCH', `/v1/operator/stations/${origin}`, operatorToken, {
    displayNameOverride: `${tag} operator override`, counterLocation: 'Counter Z', contactPhone: '+84909999999', instructions: 'Updated E2E',
  }, 200, true);
  if (patched?.data?.displayNameOverride !== `${tag} operator override`) fail('Operator station PATCH projection mismatch.');
  await api('PATCH', `/v1/operator/stations/${origin}`, otherOperatorToken, { counterLocation: 'forbidden' }, 404, true);
  await api('DELETE', `/v1/operator/stations/${origin}`, operatorToken, undefined, 200, true);
  if (scalar('vietride_trip', `select is_active from vietride_trip.operator_stations where operator_id='${operatorId}' and station_id='${origin}'`) !== 'f') fail('Operator station DELETE did not deactivate mapping.');
  const relink = await api('POST', '/v1/operator/stations', operatorToken, { stationId: origin, counterLocation: 'Relinked counter', contactPhone: '+84907777777', instructions: 'Relinked' }, 200);
  if (relink?.data?.isActive !== true) fail('Operator station POST did not reactivate mapping.');
  const before = await api('GET', `/v1/admin/stations/${origin}`, systemAdminToken, undefined, 200);
  const adminPatch = await api('PATCH', `/v1/admin/stations/${origin}`, systemAdminToken, {
    name: `${tag} Origin Canonical`, city: 'Thu Duc', province: 'Ho Chi Minh', addressStreet: `${tag} canonical address`,
  }, 200, true);
  if (adminPatch?.data?.name !== `${tag} Origin Canonical` || adminPatch.data.slug === before.data.slug) fail('Admin Station PATCH did not update canonical name/slug.');
  const stationList = await api('GET', `/v1/admin/stations?search=${encodeURIComponent(tag)}&page=1&pageSize=20`, systemAdminToken, undefined, 200);
  if (!stationList?.data?.items?.some((item) => item.id === origin)) fail('Admin Station list did not return fixture.');
  const deleted = await api('DELETE', `/v1/admin/stations/${disposable}`, systemAdminToken, undefined, 200, true);
  if (deleted?.data?.isActive !== false) fail('Admin Station DELETE did not soft-delete fixture.');
  if (scalar('vietride_trip', `select count(*) from vietride_trip.operator_stations where station_id='${disposable}' and is_active`) !== '0') fail('Admin Station DELETE did not deactivate mappings.');
  pass('Station operator/admin CRUD + reactivation + tenant isolation', `origin=${origin}`);

  const affected = await createStop(`${tag} Affected Stop`, 21.1001, 101.1001);
  const replacement = await createStop(`${tag} Replacement Stop`, 21.2001, 101.2001);
  const adminStop = await createStop(`${tag} Admin Stop`, 21.3001, 101.3001);
  const stopPatch = await api('PATCH', `/v1/operator/stops/${affected}`, operatorToken, {
    name: `${tag} Affected Stop Updated`, latitude: 21.1002, longitude: 101.1002, description: 'updated', address: 'updated address', googlePlaceId: `${tag}-updated`,
  }, 200, true);
  if (stopPatch?.data?.name !== `${tag} Affected Stop Updated`) fail('Operator Stop PATCH projection mismatch.');
  await api('PATCH', `/v1/operator/stops/${affected}`, otherOperatorToken, { name: 'forbidden' }, 404, true);
  const adminList = await api('GET', `/v1/admin/stops?operatorId=${operatorId}&search=${encodeURIComponent(tag)}&page=1&pageSize=20`, systemAdminToken, undefined, 200);
  if (!adminList?.data?.items?.some((item) => item.id === affected)) fail('Admin Stop filtered list did not return fixture.');
  const adminPatched = await api('PATCH', `/v1/admin/stops/${adminStop}`, systemAdminToken, { name: `${tag} Admin Stop Updated`, isActive: true }, 200, true);
  if (adminPatched?.data?.name !== `${tag} Admin Stop Updated`) fail('Admin Stop PATCH projection mismatch.');
  await api('DELETE', `/v1/admin/stops/${adminStop}?replacedByStopId=${replacement}`, systemAdminToken, undefined, 200, true);
  pass('Stop operator/admin PATCH/list/DELETE + tenant isolation', `affected=${affected}`);
  return { origin, destination, affected, replacement };
}

async function testTripAndBookingContract(origin, destination, affected) {
  seedTrips(origin, destination, affected);
  const detail = await api('GET', `/v1/trips/${stopTripId}`, passengerToken, undefined, 200);
  const stop = detail?.data?.stops?.find((item) => item.stopId === affected);
  if (!stop) fail('Trip detail did not include Stop projection.');
  const expectedKeys = ['address','allowDropoff','allowPickup','distanceFromOriginKm','effectiveFare','estimatedArrivalTime','fareFromThisStop','isActive','latitude','longitude','name','orderIndex','stopId'];
  if (JSON.stringify(Object.keys(stop).sort()) !== JSON.stringify(expectedKeys)) fail(`Trip Stop keys mismatch: ${Object.keys(stop).sort()}`);
  if (stop.name !== `${tag} Affected Stop Updated` || stop.address !== 'updated address' || stop.fareFromThisStop !== 99000 || stop.effectiveFare !== 99000 || stop.distanceFromOriginKm !== 42.5) fail(`Trip Stop values mismatch: ${JSON.stringify(stop)}`);
  pass('GET trip detail enriched Stop projection', `effectiveFare=${stop.effectiveFare}`);

  const duplicate = await api('POST', '/v1/bookings', passengerToken, {
    tripId: stopTripId,
    pickup: { stationId: origin },
    dropoff: { stationId: destination },
    seats: [{ seatNumber: ' DUP1 ' }, { seatNumber: 'dup1' }],
    paymentMethod: 'WALLET',
  }, 422, true);
  if (duplicate?.error?.code !== 'VALIDATION_ERROR') fail(`Duplicate seats returned ${duplicate?.error?.code}.`);
  pass('Booking duplicate seat normalization/rejection', 'case-insensitive + trimmed');

  const missing = await api('POST', '/v1/bookings', passengerToken, {
    tripId: stopTripId,
    pickup: { stationId: origin },
    dropoff: { stationId: destination },
    seats: [{ seatNumber: 'MISS1' }],
    paymentMethod: 'VNPAY',
  }, 502, true);
  if (missing?.error?.code !== 'PAYMENT_VNPAY_ERROR' || !missing?.error?.message?.includes('not configured')) fail(`Missing VNPay config mapping mismatch: ${JSON.stringify(missing)}`);
  if (scalar('vietride_booking', `select count(*) from vietride_booking.bookings where passenger_user_id='${passengerId}'`) !== '0') fail('Missing VNPay config left a Booking row behind.');
  if (scalar('vietride_trip', `select status from vietride_trip.trip_seats where trip_id='${stopTripId}' and seat_number='MISS1'`) !== 'AVAILABLE') fail('Missing VNPay config did not release seat.');
  pass('VNPay development missing-config error seam', 'HTTP 502 PAYMENT_VNPAY_ERROR; no orphan booking/lock');

  seedWallet();
  const wallet = await api('POST', '/v1/bookings', passengerToken, {
    tripId: stopTripId,
    pickup: { stopId: affected },
    dropoff: { stationId: destination },
    seats: [{ seatNumber: 'WALLET1' }],
    paymentMethod: 'WALLET',
  }, 201, true);
  const walletBookingId = wallet?.data?.bookingId;
  if (!walletBookingId || wallet.data.status !== 'CONFIRMED' || wallet.data.tickets?.length !== 1) fail(`PII-free Wallet booking failed: ${JSON.stringify(wallet)}`);
  bookingIds.push(walletBookingId);
  pass('PII-free booking payload', `booking=${walletBookingId}; seats=[{seatNumber}] only`);
}

async function testStopDisable(affected, replacement) {
  const result = await api('DELETE', `/v1/operator/stops/${affected}?replacedByStopId=${replacement}`, operatorToken, undefined, 200, true);
  if (result?.data?.warning !== 'STOP_DISABLED_BOOKING_AFFECTED' || result.data.activeBookingCount !== 1 || result.data.stop.isActive !== false) fail(`Stop disable response mismatch: ${JSON.stringify(result)}`);
  const outboxCount = scalar('vietride_trip', `select count(*) from vietride_trip.outbox_events where event_type='trip.stop.disabled' and payload::text like '%${affected}%'`);
  if (outboxCount !== '1') fail(`Expected one trip.stop.disabled outbox row, got ${outboxCount}.`);
  await waitFor('STOP_DISABLED pending action', async () => scalar('vietride_booking', `select count(*) from vietride_booking.booking_pending_actions a join vietride_booking.bookings b on b.id=a.booking_id where b.passenger_user_id='${passengerId}' and a.reason='STOP_DISABLED' and a.resolved_at is null`) === '1');
  const deadlineValid = scalar('vietride_booking', `select (a.deadline <= least(now()+interval '24 hours 1 minute', b.trip_snapshot_departure-interval '2 hours'))::text from vietride_booking.booking_pending_actions a join vietride_booking.bookings b on b.id=a.booking_id where b.passenger_user_id='${passengerId}' and a.reason='STOP_DISABLED' limit 1`);
  if (deadlineValid !== 'true') fail('STOP_DISABLED deadline is outside min(now+24h, departure-2h).');
  await waitFor('STOP_DISABLED notification', async () => scalar('vietride_notification', `select count(*) from vietride_notification.notifications where user_id='${passengerId}' and type='STOP_DISABLED'`) === '1');
  await new Promise((resolve) => setTimeout(resolve, 1500));
  const pendingCount = scalar('vietride_booking', `select count(*) from vietride_booking.booking_pending_actions a join vietride_booking.bookings b on b.id=a.booking_id where b.passenger_user_id='${passengerId}' and a.reason='STOP_DISABLED'`);
  const notificationCount = scalar('vietride_notification', `select count(*) from vietride_notification.notifications where user_id='${passengerId}' and type='STOP_DISABLED'`);
  if (pendingCount !== '1' || notificationCount !== '1') fail(`Stop disable was not idempotent: pending=${pendingCount}, notifications=${notificationCount}.`);
  pass('Stop disable Outbox -> Booking pending action -> Notification', 'activeBookingCount=1; idempotent 1/1');
}

async function createVnPayBooking(tripId, origin, destination, seatNumber) {
  const result = await api('POST', '/v1/bookings', passengerToken, {
    tripId,
    pickup: { stationId: origin },
    dropoff: { stationId: destination },
    seats: [{ seatNumber }],
    paymentMethod: 'VNPAY',
  }, 201, true);
  if (result?.data?.status !== 'PENDING_PAYMENT' || !result.data.paymentId || !result.data.paymentRedirectUrl) fail(`VNPay booking redirect response mismatch: ${JSON.stringify(result)}`);
  bookingIds.push(result.data.bookingId);
  paymentReferenceIds.push(result.data.bookingId);
  return result.data;
}

function validateRedirect(data, expectedAmount) {
  const url = new URL(data.paymentRedirectUrl);
  const values = Object.fromEntries(url.searchParams.entries());
  const hash = values.vnp_SecureHash;
  delete values.vnp_SecureHash;
  if (url.origin + url.pathname !== 'https://sandbox.vnpayment.vn/paymentv2/vpcpay.html') fail(`Unexpected VNPay base URL: ${url}`);
  if (values.vnp_TmnCode !== 'E2ETMN' || values.vnp_Amount !== String(expectedAmount * 100) || values.vnp_ReturnUrl !== 'https://app.vietride.online/payments/return') fail(`VNPay redirect fields mismatch: ${JSON.stringify(values)}`);
  if (signVnPay(values) !== hash) fail('VNPay redirect secure hash is invalid.');
  const parse = (value) => Date.UTC(Number(value.slice(0, 4)), Number(value.slice(4, 6)) - 1, Number(value.slice(6, 8)), Number(value.slice(8, 10)), Number(value.slice(10, 12)), Number(value.slice(12, 14)));
  if ((parse(values.vnp_ExpireDate) - parse(values.vnp_CreateDate)) / 60000 !== 10) fail('VNPay redirect expiry is not 10 minutes.');
  return values;
}

async function testVnPay(origin, destination) {
  vnpaySecret = crypto.randomBytes(48).toString('hex');
  await restartPayment('E2ETMN', vnpaySecret);
  pass('VNPay sandbox runtime configuration', 'temporary non-production E2E key; timeout=10');

  const oneWay = await createVnPayBooking(paymentTripId, origin, destination, 'ONE1');
  const redirect = validateRedirect(oneWay, oneWay.totalAmount);
  const [txnRef, amount] = paymentRow(oneWay.paymentId);
  const bad = { vnp_TmnCode: 'E2ETMN', vnp_TxnRef: txnRef, vnp_Amount: String(Number(amount) * 100), vnp_ResponseCode: '00', vnp_TransactionStatus: '00' };
  const invalidResponse = await fetch(`${gateway}/v1/payments/vnpay-ipn?${new URLSearchParams({ ...bad, vnp_SecureHash: 'invalid' })}`);
  const invalid = await invalidResponse.json();
  if (invalidResponse.status !== 200 || invalid.RspCode !== '97') fail(`Invalid signature mapping mismatch: ${JSON.stringify(invalid)}`);
  if (paymentRow(oneWay.paymentId)[2] !== 'PENDING_REDIRECT') fail('Invalid signature mutated Payment state.');
  await sendIpn(bad, '00');
  await pollBookingStatus(oneWay.bookingId, 'CONFIRMED');
  await sendIpn(bad, '02');
  if (scalar('vietride_trip', `select status from vietride_trip.trip_seats where trip_id='${paymentTripId}' and seat_number='ONE1'`) !== 'BOOKED') fail('One-way signed IPN did not book seat.');
  pass('VNPay one-way redirect + signed GET IPN + replay', `txnRef=${redirect.vnp_TxnRef}; RspCode=00/02/97`);

  const amountMismatch = await createVnPayBooking(paymentTripId, origin, destination, 'AMOUNT1');
  const [amountTxnRef, correctAmount] = paymentRow(amountMismatch.paymentId);
  await sendIpn({ vnp_TmnCode: 'E2ETMN', vnp_TxnRef: amountTxnRef, vnp_Amount: String(Number(correctAmount) * 100 + 100), vnp_ResponseCode: '00', vnp_TransactionStatus: '00' }, '04');
  await pollBookingStatus(amountMismatch.bookingId, 'EXPIRED');
  if (scalar('vietride_trip', `select status from vietride_trip.trip_seats where trip_id='${paymentTripId}' and seat_number='AMOUNT1'`) !== 'AVAILABLE') fail('Amount mismatch did not release seat.');
  pass('VNPay amount mismatch', 'HTTP 200/RspCode 04; booking expired; seat released');

  const group = await api('POST', '/v1/bookings/round-trip', passengerToken, {
    outbound: { tripId: roundOutboundTripId, pickup: { stationId: origin }, dropoff: { stationId: destination }, seats: [{ seatNumber: 'GROUP1' }] },
    return: { tripId: roundReturnTripId, pickup: { stationId: destination }, dropoff: { stationId: origin }, seats: [{ seatNumber: 'GROUP1' }] },
    paymentMethod: 'VNPAY',
  }, 201, true);
  const groupData = group?.data;
  if (!groupData?.paymentId || !groupData.paymentRedirectUrl || groupData.status !== 'PENDING_PAYMENT') fail(`Round-trip redirect mismatch: ${JSON.stringify(group)}`);
  bookingIds.push(groupData.outbound.bookingId, groupData.return.bookingId);
  paymentReferenceIds.push(groupData.bookingGroupId);
  validateRedirect(groupData, groupData.grandTotal);
  const [groupTxnRef, groupAmount, , referenceType] = paymentRow(groupData.paymentId);
  if (referenceType !== 'BOOKING_GROUP') fail(`Round-trip payment reference is ${referenceType}.`);
  await sendIpn({ vnp_TmnCode: 'E2ETMN', vnp_TxnRef: groupTxnRef, vnp_Amount: String(Number(groupAmount) * 100), vnp_ResponseCode: '00', vnp_TransactionStatus: '00' }, '00');
  await Promise.all([pollBookingStatus(groupData.outbound.bookingId, 'CONFIRMED'), pollBookingStatus(groupData.return.bookingId, 'CONFIRMED')]);
  const bookedGroupSeats = scalar('vietride_trip', `select count(*) from vietride_trip.trip_seats where trip_id in ('${roundOutboundTripId}','${roundReturnTripId}') and seat_number='GROUP1' and status='BOOKED'`);
  if (bookedGroupSeats !== '2') fail(`Round-trip confirmation booked ${bookedGroupSeats}/2 seats.`);
  pass('VNPay BOOKING_GROUP success', 'two bookings + two seats confirmed');

  const failedGroup = await api('POST', '/v1/bookings/round-trip', passengerToken, {
    outbound: { tripId: roundOutboundTripId, pickup: { stationId: origin }, dropoff: { stationId: destination }, seats: [{ seatNumber: 'GROUPFAIL1' }] },
    return: { tripId: roundReturnTripId, pickup: { stationId: destination }, dropoff: { stationId: origin }, seats: [{ seatNumber: 'GROUPFAIL1' }] },
    paymentMethod: 'VNPAY',
  }, 201, true);
  bookingIds.push(failedGroup.data.outbound.bookingId, failedGroup.data.return.bookingId);
  paymentReferenceIds.push(failedGroup.data.bookingGroupId);
  const [failedTxnRef, failedAmount] = paymentRow(failedGroup.data.paymentId);
  await sendIpn({ vnp_TmnCode: 'E2ETMN', vnp_TxnRef: failedTxnRef, vnp_Amount: String(Number(failedAmount) * 100), vnp_ResponseCode: '24', vnp_TransactionStatus: '02' }, '00');
  await Promise.all([pollBookingStatus(failedGroup.data.outbound.bookingId, 'EXPIRED'), pollBookingStatus(failedGroup.data.return.bookingId, 'EXPIRED')]);
  const releasedGroupSeats = scalar('vietride_trip', `select count(*) from vietride_trip.trip_seats where trip_id in ('${roundOutboundTripId}','${roundReturnTripId}') and seat_number='GROUPFAIL1' and status='AVAILABLE'`);
  if (releasedGroupSeats !== '2') fail(`Round-trip failure released ${releasedGroupSeats}/2 seats.`);
  pass('VNPay BOOKING_GROUP failed', 'two bookings expired + two locks released');

  const timeout = await createVnPayBooking(paymentTripId, origin, destination, 'TIMEOUT1');
  runSql('vietride_payment', `update vietride_payment.payments set created_at=now()-interval '10 minutes 2 seconds' where id='${timeout.paymentId}';`);
  await waitFor('VNPay timeout payment', async () => paymentRow(timeout.paymentId)[2] === 'EXPIRED', 180, 500);
  await pollBookingStatus(timeout.bookingId, 'EXPIRED');
  if (scalar('vietride_trip', `select status from vietride_trip.trip_seats where trip_id='${paymentTripId}' and seat_number='TIMEOUT1'`) !== 'AVAILABLE') fail('Timed-out payment did not release seat.');
  pass('VNPay 10-minute timeout job', 'payment + booking expired; seat released');
}

function cleanup() {
  const ids = bookingIds.length ? bookingIds.map((id) => `'${id}'`).join(',') : "'00000000-0000-0000-0000-000000000000'";
  const refs = paymentReferenceIds.length ? paymentReferenceIds.map((id) => `'${id}'`).join(',') : "'00000000-0000-0000-0000-000000000000'";
  runSql('vietride_notification', `
    delete from vietride_notification.notification_deliveries where notification_id in (select id from vietride_notification.notifications where user_id='${passengerId}');
    delete from vietride_notification.email_deliveries where notification_id in (select id from vietride_notification.notifications where user_id='${passengerId}');
    delete from vietride_notification.notifications where user_id='${passengerId}';
  `);
  runSql('vietride_booking', `
    delete from vietride_booking.tickets where booking_id in (${ids});
    delete from vietride_booking.passengers where booking_id in (${ids});
    delete from vietride_booking.booking_pending_actions where booking_id in (${ids});
    delete from vietride_booking.booking_status_history where booking_id in (${ids});
    delete from vietride_booking.voucher_usages where booking_id in (${ids});
    delete from vietride_booking.bookings where id in (${ids}) or passenger_user_id='${passengerId}';
    delete from vietride_booking.outbox_events where payload::text like '%${passengerId}%' or payload::text like '%${stopTripId}%';
  `);
  runSql('vietride_payment', `
    delete from vietride_payment.platform_wallet_transactions where reference_id in (${refs},${ids});
    update vietride_payment.platform_wallets set balance=${platformBalanceBefore}, row_version=row_version+1;
    delete from vietride_payment.payments where user_id='${passengerId}' or reference_id in (${refs},${ids});
    delete from vietride_payment.wallet_transactions where user_id='${passengerId}';
    delete from vietride_payment.wallets where user_id='${passengerId}';
    delete from vietride_payment.outbox_events where payload::text like '%${passengerId}%';
  `);
  runSql('vietride_trip', `
    delete from vietride_trip.trips where id in ('${stopTripId}','${paymentTripId}','${roundOutboundTripId}','${roundReturnTripId}');
    delete from vietride_trip.routes where id in ('${outboundRouteId}','${returnRouteId}');
    update vietride_trip.stops set replaced_by_stop_id=null where id in (${createdStopIds.map((id) => `'${id}'`).join(',') || "'00000000-0000-0000-0000-000000000000'"});
    delete from vietride_trip.stops where id in (${createdStopIds.map((id) => `'${id}'`).join(',') || "'00000000-0000-0000-0000-000000000000'"});
    delete from vietride_trip.operator_stations where station_id in (${createdStationIds.map((id) => `'${id}'`).join(',') || "'00000000-0000-0000-0000-000000000000'"});
    delete from vietride_trip.stations where id in (${createdStationIds.map((id) => `'${id}'`).join(',') || "'00000000-0000-0000-0000-000000000000'"});
    delete from vietride_trip.outbox_events where payload::text like '%${tag}%' or payload::text like '%${stopTripId}%';
  `);
  runSql('vietride_identity', `delete from vietride_identity.users where id='${passengerId}';`);
}

let runError;
try {
  platformBalanceBefore = scalar('vietride_payment', 'select coalesce((select balance from vietride_payment.platform_wallets limit 1),0)');
  seedIdentity();
  [operatorToken, otherOperatorToken, systemAdminToken, passengerToken] = await Promise.all([
    issueToken({ subject: operatorAdminId, role: 'OPERATOR_ADMIN', email: 'day9-approved-admin@example.test', operator: operatorId }),
    issueToken({ subject: otherOperatorAdminId, role: 'OPERATOR_ADMIN', email: 'day7-approved-admin@example.test', operator: otherOperatorId }),
    issueToken({ subject: systemAdminId, role: 'SYSTEM_ADMIN', email: 'e2e-full-system-admin@vietride.local' }),
    issueToken({ subject: passengerId, role: 'PASSENGER', email: `${tag}@example.test` }),
  ]);
  const fixture = await testStationAndStopApis();
  await testTripAndBookingContract(fixture.origin, fixture.destination, fixture.affected);
  await testStopDisable(fixture.affected, fixture.replacement);
  await testVnPay(fixture.origin, fixture.destination);
  pass('FULL E2E', tag);
} catch (error) {
  runError = error;
  console.error(`FAIL | FULL E2E | ${error.stack ?? error.message}`);
} finally {
  try {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    cleanup();
    pass('Fixture cleanup', `passenger=${passengerId}; tag=${tag}`);
  } catch (error) {
    console.error(`FAIL | Fixture cleanup | ${error.stack ?? error.message}`);
    runError ??= error;
  }
  try {
    await restartPayment('', '');
    pass('Payment config restore', 'ready-to-fill (no fake credentials)');
  } catch (error) {
    console.error(`FAIL | Payment config restore | ${error.stack ?? error.message}`);
    runError ??= error;
  }
}

if (runError) throw runError;
