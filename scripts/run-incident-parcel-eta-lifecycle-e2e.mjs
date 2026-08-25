// Feature-plan Docker E2E. Fixture setup/cleanup uses PostgreSQL directly; every
// business transition is exercised through the running Gateway/Tracking APIs.
import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { importPKCS8, SignJWT } from 'jose';
import { io } from 'socket.io-client';

const root = process.cwd();
const gateway = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const tracking = process.env.TRACKING_BASE_URL || 'http://localhost:3001';
const postgres = process.env.POSTGRES_CONTAINER || 'vietride_postgres';
const redisContainer = process.env.REDIS_CONTAINER || 'vietride_redis';
const postgresUser = process.env.POSTGRES_USER || 'vietride';
const ids = Object.freeze({
  operator: crypto.randomUUID(),
  foreignOperator: crypto.randomUUID(),
  plan: crypto.randomUUID(),
  subscription: crypto.randomUUID(),
  admin: crypto.randomUUID(),
  staff: crypto.randomUUID(),
  foreignAdmin: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  sender: crypto.randomUUID(),
  recipient: crypto.randomUUID(),
  rootLocation: crypto.randomUUID(),
  originLocation: crypto.randomUUID(),
  destinationLocation: crypto.randomUUID(),
  legacyRootStation: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  stop: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  route: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  tripSeat: crypto.randomUUID(),
  depositPolicy: crypto.randomUUID(),
});
const runTag = ids.trip.replaceAll('-', '').slice(0, 10).toUpperCase();
const recipientEmail = `recipient-${runTag}@feature-plan-e2e.test`;
const idempotencyKeys = [];
const createdParcelIds = [];
const generatedTripIds = [];
const sockets = new Set();
const auditFailures = [];
const evidence = {};
let parcelId;
let driverScheduleId;
let paymentPlatformSnapshot;
let assertions = 0;

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  }
  return result.stdout.trim();
}

function sql(database, schema, statement) {
  return run('docker', [
    'exec',
    postgres,
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    postgresUser,
    '-d',
    database,
    '-qAtc',
    `SET search_path TO ${schema},public; ${statement}`,
  ]);
}

const identitySql = (statement) => sql('vietride_identity', 'vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', 'vietride_trip', statement);
const parcelSql = (statement) => sql('vietride_parcel', 'vietride_parcel', statement);
const paymentSql = (statement) => sql('vietride_payment', 'vietride_payment', statement);
const notificationSql = (statement) =>
  sql('vietride_notification', 'vietride_notification', statement);

function scalar(value) {
  return String(value).split(/\r?\n/u).filter(Boolean).at(-1)?.trim() || '';
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
  assertions += 1;
}

function projectEtaEvidence(eta, recordedAt) {
  if (!eta) return null;
  const estimatedArrivalMs = Date.parse(eta.estimatedArrivalTime);
  const recordedAtMs = Date.parse(recordedAt);
  return {
    targetKind: eta.targetKind,
    targetId: eta.stopId ?? eta.stationId ?? null,
    estimatedArrivalTime: eta.estimatedArrivalTime,
    etaMinutesFromGps:
      Number.isFinite(estimatedArrivalMs) && Number.isFinite(recordedAtMs)
        ? Math.round(((estimatedArrivalMs - recordedAtMs) / 60_000) * 100) / 100
        : null,
    distanceMeters: eta.distanceMeters,
    delayMinutes: eta.delayMinutes,
    estimateQuality: eta.estimateQuality,
  };
}

function pass(label) {
  console.log(`PASS | ${label}`);
}

function audit(condition, label, evidence) {
  assertions += 1;
  if (condition) {
    pass(label);
    return;
  }
  auditFailures.push(`${label}: ${evidence}`);
  console.error(`FAIL | ${label} | ${evidence}`);
}

function nextKey() {
  const key = crypto.randomUUID();
  idempotencyKeys.push(key);
  return key;
}

async function poll(label, probe, predicate, timeoutMs = 90_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await probe();
      if (predicate(last)) {
        pass(label);
        return last;
      }
    } catch (error) {
      last = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 400));
  }
  throw new Error(
    `${label} timed out; last=${last instanceof Error ? last.message : JSON.stringify(last)}`,
  );
}

async function api(method, pathname, { token, body, key } = {}) {
  const traceId = `feature-e2e-${crypto.randomUUID()}`;
  const response = await fetch(`${gateway}${pathname}`, {
    method,
    headers: {
      'X-Request-Id': traceId,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(key ? { 'Idempotency-Key': key } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(20_000),
  });
  const text = await response.text();
  return {
    status: response.status,
    json: text ? JSON.parse(text) : null,
    traceId,
    responseTraceId: response.headers.get('x-request-id'),
  };
}

async function trackingApi(pathname, token, headers = {}) {
  const response = await fetch(`${tracking}${pathname}`, {
    headers: { Authorization: `Bearer ${token}`, ...headers },
    signal: AbortSignal.timeout(20_000),
  });
  const text = await response.text();
  return {
    status: response.status,
    json: text ? JSON.parse(text) : null,
    etag: response.headers.get('etag'),
  };
}

function data(response, expectedStatus, errorCode = null) {
  assert(
    response.status === expectedStatus,
    `Expected HTTP ${expectedStatus}, got ${response.status}: ${JSON.stringify(response.json)}`,
  );
  assert(response.json?.statusCode === expectedStatus, 'ApiResponse statusCode mismatch');
  if (errorCode) {
    assert(response.json?.success === false, 'Expected error envelope');
    assert(
      response.json?.error?.code === errorCode,
      `Expected ${errorCode}, got ${response.json?.error?.code}`,
    );
  } else {
    assert(response.json?.success === true, 'Expected success envelope');
  }
  return response.json?.data;
}

async function mintToken(subject, role, operatorId = null, email = null) {
  const settings = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  const privateKey = await importPKCS8(settings.IdentityJwt.PrivateKey, 'RS256');
  const claims = {
    role,
    email: email || `${role.toLowerCase()}-${runTag}@feature-plan-e2e.test`,
    hasPhone: 'true',
  };
  if (operatorId) {
    claims.operatorId = operatorId;
    claims.operator_id = operatorId;
    claims.operatorStatus = 'APPROVED';
    claims.operator_status = 'APPROVED';
  }
  return new SignJWT(claims)
    .setProtectedHeader({ alg: 'RS256', kid: settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('45m')
    .sign(privateKey);
}

function pickRootCode() {
  for (let code = 99; code >= 10; code -= 1) {
    const candidate = String(code);
    if (tripSql(`SELECT count(*) FROM locations WHERE code='${candidate}'`) === '0') {
      return candidate;
    }
  }
  throw new Error('No free two-digit location code is available');
}

function encodePolyline(points) {
  let previousLatitude = 0;
  let previousLongitude = 0;
  let encoded = '';
  const encodeValue = (value) => {
    let current = value < 0 ? ~(value << 1) : value << 1;
    let result = '';
    while (current >= 0x20) {
      result += String.fromCharCode((0x20 | (current & 0x1f)) + 63);
      current >>= 5;
    }
    return result + String.fromCharCode(current + 63);
  };
  for (const point of points) {
    const latitude = Math.round(point.latitude * 1e5);
    const longitude = Math.round(point.longitude * 1e5);
    encoded += encodeValue(latitude - previousLatitude);
    encoded += encodeValue(longitude - previousLongitude);
    previousLatitude = latitude;
    previousLongitude = longitude;
  }
  return encoded.replaceAll("'", "''");
}

function seedFixtures() {
  const rootCode = pickRootCode();
  const originCode = `${rootCode}001`;
  const destinationCode = `${rootCode}002`;
  const departure = new Date(Date.now() + 2 * 60 * 60 * 1000);
  const stopEta = new Date(Date.now() + 3 * 60 * 60 * 1000);
  const arrival = new Date(Date.now() + 5 * 60 * 60 * 1000);
  const departureDate = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(departure);
  const polyline = encodePolyline([
    { latitude: 10.76, longitude: 106.66 },
    { latitude: 10.77, longitude: 106.67 },
    { latitude: 10.78, longitude: 106.68 },
  ]);

  identitySql(`
    INSERT INTO subscription_plans
      (id,name,description,price_per_month,price_per_year,max_vehicles,max_drivers,
       max_assistants,max_operator_users,max_routes,max_trips_per_month,
       enable_parcel,enable_shuttle,enable_rag,is_active)
    VALUES
      ('${ids.plan}','Feature E2E ${runTag}','Incident Parcel ETA lifecycle E2E',0,0,
       10,10,10,10,10,100,true,false,false,true);
    INSERT INTO operators
      (id,name,business_registration_number,tax_code,contact_email,contact_phone,
       registration_status,approved_at,is_active)
    VALUES
      ('${ids.operator}','Feature Operator ${runTag}','FE-${runTag}','FE-TAX-${runTag}',
       'operator-${runTag}@feature-plan-e2e.test','+84810${runTag.slice(0, 6)}','APPROVED',now(),true),
      ('${ids.foreignOperator}','Foreign Operator ${runTag}','FX-${runTag}','FX-TAX-${runTag}',
       'foreign-${runTag}@feature-plan-e2e.test','+84811${runTag.slice(0, 6)}','APPROVED',now(),true);
    INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
    VALUES
      ('${ids.admin}','admin-${runTag}@feature-plan-e2e.test','+84910000011','Admin ${runTag}','OPERATOR_ADMIN','ACTIVE','${ids.operator}'),
      ('${ids.staff}','staff-${runTag}@feature-plan-e2e.test','+84910000012','Staff ${runTag}','OPERATOR_STAFF','ACTIVE','${ids.operator}'),
      ('${ids.foreignAdmin}','foreign-admin-${runTag}@feature-plan-e2e.test','+84910000013','Foreign Admin ${runTag}','OPERATOR_ADMIN','ACTIVE','${ids.foreignOperator}'),
      ('${ids.driver}','driver-${runTag}@feature-plan-e2e.test','+84910000014','Driver ${runTag}','DRIVER','ACTIVE','${ids.operator}'),
      ('${ids.assistant}','assistant-${runTag}@feature-plan-e2e.test','+84910000015','Assistant ${runTag}','ASSISTANT','ACTIVE','${ids.operator}'),
      ('${ids.sender}','sender-${runTag}@feature-plan-e2e.test','+84910000016','Sender ${runTag}','PASSENGER','ACTIVE',NULL),
      ('${ids.recipient}','${recipientEmail}','+84910000017','Recipient ${runTag}','PASSENGER','ACTIVE',NULL);
    INSERT INTO operator_subscriptions
      (id,operator_id,active_plan_id,status,started_at,expires_at,current_vehicles,
       current_routes,current_trips_this_month,last_reset_at)
    VALUES
      ('${ids.subscription}','${ids.operator}','${ids.plan}','ACTIVE',
       now()-interval '1 day',now()+interval '30 days',0,0,0,now());
  `);

  tripSql(`
    INSERT INTO locations (id,code,name,type,is_active,sort_order,parent_location_id)
    VALUES
      ('${ids.rootLocation}','${rootCode}','Root ${runTag}','PROVINCE',true,1,NULL),
      ('${ids.originLocation}','${originCode}','Origin Leaf ${runTag}','WARD',true,1,'${ids.rootLocation}'),
      ('${ids.destinationLocation}','${destinationCode}','Destination Leaf ${runTag}','WARD',true,2,'${ids.rootLocation}');
    INSERT INTO stations
      (id,name,slug,city,ward,latitude,longitude,location_id,is_active)
    VALUES
      ('${ids.legacyRootStation}','Legacy Root ${runTag}','feature-root-${runTag.toLowerCase()}',
       'Feature City','Root Ward',10.755,106.655,'${ids.rootLocation}',true),
      ('${ids.originStation}','Origin ${runTag}','feature-origin-${runTag.toLowerCase()}',
       'Feature City','Origin Ward',10.760,106.660,'${ids.originLocation}',true),
      ('${ids.destinationStation}','Destination ${runTag}','feature-destination-${runTag.toLowerCase()}',
       'Feature City','Destination Ward',10.780,106.680,'${ids.destinationLocation}',true);
    INSERT INTO stops (id,operator_id,name,latitude,longitude,location_id,is_active)
    VALUES ('${ids.stop}','${ids.operator}','Middle Stop ${runTag}',10.770,106.670,'${ids.originLocation}',true);
  `);

  tripSql(`
    INSERT INTO vehicle_types
      (id,code,display_name,default_seat_count,is_system_defined,is_active)
    VALUES ('${ids.vehicleType}','FPE_${runTag}','Feature E2E Vehicle ${runTag}',2,false,true);
    INSERT INTO vehicles
      (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,
       max_cargo_weight_kg,max_cargo_volume_m3,status,is_active)
    VALUES
      ('${ids.vehicle}','${ids.operator}','${ids.vehicleType}','FE${runTag.slice(0, 8)}',
       '{"version":1,"vehicleTypeCode":"FEATURE_E2E","totalSeats":2,"rows":1,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"type":"STANDARD","isWindow":true,"isAisle":false,"disabled":false},{"seatNumber":"A02","row":1,"col":2,"deck":1,"type":"STANDARD","isWindow":true,"isAisle":false,"disabled":false}]}',
       2,100,10,'ACTIVE',true);
    INSERT INTO routes
      (id,operator_id,name,origin_station_id,destination_station_id,base_fare,
       total_distance_km,estimated_duration_minutes,path_polyline,is_active)
    VALUES
      ('${ids.route}','${ids.operator}','Feature Route ${runTag}','${ids.originStation}',
       '${ids.destinationStation}',150000,12,180,'${polyline}',true);
    INSERT INTO route_stops
      (route_id,stop_id,order_index,estimated_duration_from_origin_minutes,
       distance_from_origin_km,allow_pickup,allow_dropoff)
    VALUES ('${ids.route}','${ids.stop}',1,90,6,true,true);
    INSERT INTO trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,
       departure_date_time,estimated_arrival_time,status,source,base_fare,
       max_cargo_weight_kg,max_cargo_volume_m3,estimated_passenger_luggage_kg,
       reserved_parcel_weight_kg,reserved_parcel_volume_m3,total_loaded_weight_kg,
       total_loaded_volume_m3,seat_layout_snapshot_json)
    VALUES
      ('${ids.trip}','${ids.operator}','${ids.route}','${ids.vehicle}','${ids.driver}',
       '${ids.assistant}','${departure.toISOString()}','${arrival.toISOString()}',
       'SCHEDULED','MANUAL',150000,100,10,0,0,0,0,0,
       '{"version":1,"vehicleTypeCode":"FEATURE_E2E","totalSeats":2,"rows":1,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"type":"STANDARD","isWindow":true,"isAisle":false,"disabled":false},{"seatNumber":"A02","row":1,"col":2,"deck":1,"type":"STANDARD","isWindow":true,"isAisle":false,"disabled":false}]}');
    INSERT INTO trip_stops
      (trip_id,stop_id,order_index,estimated_arrival_time,status,
       allow_pickup,allow_dropoff,distance_from_origin_km)
    VALUES
      ('${ids.trip}','${ids.stop}',1,'${stopEta.toISOString()}',
       'PENDING',true,true,6);
    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    VALUES ('${ids.tripSeat}','${ids.trip}','A01','STANDARD','AVAILABLE');
  `);

  parcelSql(`
    INSERT INTO parcel_route_fares
      (route_id,size_category,operator_id,price_vnd,price_per_chargeable_kg_vnd,
       minimum_price_vnd,effective_from)
    VALUES
      ('${ids.route}','SMALL','${ids.operator}',1000,1000,0,now()-interval '1 hour'),
      ('${ids.route}','MEDIUM','${ids.operator}',1000,1000,0,now()-interval '1 hour');
    INSERT INTO operator_deposit_policies
      (id,operator_id,route_id,deposit_percent,effective_from,is_active)
    VALUES ('${ids.depositPolicy}','${ids.operator}','${ids.route}',65,now()-interval '1 hour',true);
  `);

  paymentPlatformSnapshot = paymentSql(`
    SELECT id::text||'|'||balance::text||'|'||row_version::text
    FROM platform_wallets ORDER BY created_at LIMIT 1;
  `);
  paymentSql(`
    INSERT INTO wallets (user_id,balance,currency,row_version)
    VALUES ('${ids.sender}',100000,'VND',0);
  `);
  evidence.plannedManualTrip = {
    tripId: ids.trip,
    departureDateTime: departure.toISOString(),
    stopEstimatedArrivalTime: stopEta.toISOString(),
    destinationEstimatedArrivalTime: arrival.toISOString(),
    departureToStopMinutes: Math.round((stopEta.getTime() - departure.getTime()) / 60_000),
    stopToDestinationMinutes: Math.round((arrival.getTime() - stopEta.getTime()) / 60_000),
    totalMinutes: Math.round((arrival.getTime() - departure.getTime()) / 60_000),
  };
  pass('real PostgreSQL fixture: hierarchy, route snapshot, boarding trip, fare and wallet');
  return { rootCode, originCode, destinationCode, departureDate };
}

function isoDateInVietnam(date) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date);
}

function isoDayOfWeek(dateText) {
  const utcDay = new Date(`${dateText}T12:00:00Z`).getUTCDay();
  return utcDay === 0 ? 7 : utcDay;
}

async function exerciseImmediateDriverScheduleGeneration(tokens) {
  const serviceDate = isoDateInVietnam(new Date(Date.now() + 7 * 24 * 60 * 60 * 1000));
  const schedule = data(
    await api('POST', '/v1/operator/driver-schedules', {
      token: tokens.admin,
      body: {
        routeId: ids.route,
        vehicleId: ids.vehicle,
        driverUserId: ids.driver,
        assistantUserId: ids.assistant,
        dayOfWeek: [isoDayOfWeek(serviceDate)],
        departureTime: '06:15:00',
        validFrom: serviceDate,
        validUntil: serviceDate,
        baseFare: 150000,
        isActive: true,
      },
    }),
    201,
  );
  driverScheduleId = schedule.id;
  assert(driverScheduleId, 'DriverSchedule create response omitted id');

  const generated = await poll(
    'Active DriverSchedule enqueues Hangfire generation immediately',
    () => {
      const row = tripSql(`
        SELECT id::text||'|'||source::text||'|'||planned_eta_source::text
        FROM trips WHERE driver_schedule_id='${driverScheduleId}'
        ORDER BY created_at LIMIT 1;
      `);
      if (!row) return null;
      const [tripId, source, plannedEtaSource] = row.split('|');
      return { tripId, source, plannedEtaSource };
    },
    (value) => Boolean(value?.tripId),
    30_000,
  );
  generatedTripIds.push(generated.tripId);
  assert(
    generated.source === 'AUTO_FROM_SCHEDULE',
    `Generated source drifted: ${generated.source}`,
  );
  if (process.env.EXPECT_GOONG === 'true') {
    audit(
      generated.plannedEtaSource === 'GOONG',
      'Hangfire-generated Trip planned ETA uses real Goong Directions',
      `plannedEtaSource=${generated.plannedEtaSource}`,
    );
  }
  const etaSnapshot = tripSql(`
    SELECT
      (ts.estimated_arrival_time > t.departure_date_time)::text||'|'||
      (t.estimated_arrival_time > ts.estimated_arrival_time)::text||'|'||
      count(seat.id)::text
    FROM trips t
    JOIN trip_stops ts ON ts.trip_id=t.id
    LEFT JOIN trip_seats seat ON seat.trip_id=t.id
    WHERE t.id='${generated.tripId}'
    GROUP BY t.departure_date_time,t.estimated_arrival_time,ts.estimated_arrival_time;
  `);
  assert(
    etaSnapshot === 'true|true|2',
    `Generated Trip snapshot is incomplete: ${JSON.stringify(etaSnapshot)}`,
  );
  assert(
    tripSql(`SELECT count(*) FROM resource_reservations WHERE trip_id='${generated.tripId}'`) ===
      '3',
    'Generated Trip did not reserve driver, assistant and vehicle',
  );
  const generatedTiming = tripSql(`
    SELECT t.departure_date_time::text||'|'||ts.estimated_arrival_time::text||'|'||
      t.estimated_arrival_time::text||'|'||
      round(extract(epoch FROM (ts.estimated_arrival_time-t.departure_date_time))/60)::text||'|'||
      round(extract(epoch FROM (t.estimated_arrival_time-ts.estimated_arrival_time))/60)::text
    FROM trips t
    JOIN trip_stops ts ON ts.trip_id=t.id
    WHERE t.id='${generated.tripId}'
    ORDER BY ts.order_index LIMIT 1;
  `).split('|');
  evidence.hangfireGeneratedTrip = {
    tripId: generated.tripId,
    source: generated.source,
    plannedEtaSource: generated.plannedEtaSource,
    departureDateTime: generatedTiming[0],
    stopEstimatedArrivalTime: generatedTiming[1],
    destinationEstimatedArrivalTime: generatedTiming[2],
    departureToStopMinutes: Number(generatedTiming[3]),
    stopToDestinationMinutes: Number(generatedTiming[4]),
  };
  pass('DriverSchedule single-day window -> immediate Hangfire -> Trip/stops/seats/reservations');
}

function parcelBody(quote, overrides = {}) {
  return {
    tripId: ids.trip,
    dropoffStopId: null,
    bookingId: null,
    itemName: `Feature parcel ${runTag}`,
    description: 'Real Docker lifecycle parcel',
    sizeCategory: quote.estimatedSizeCategory,
    lengthCm: 20,
    widthCm: 20,
    heightCm: 20,
    estimatedWeightKg: 3.2,
    photoUrl: null,
    recipient: {
      fullName: `Recipient ${runTag}`,
      phoneNumber: '+84910000017',
      email: recipientEmail,
    },
    deliveryMethod: 'TERMINAL_PICKUP',
    paymentMethod: 'WALLET',
    voucherCode: null,
    quoteToken: quote.quoteToken,
    ...overrides,
  };
}

function resignQuoteToken(token, mutatePayload) {
  const secret = process.env.PARCEL_QUOTE_TOKEN_SECRET;
  assert(
    secret?.length >= 32,
    'PARCEL_QUOTE_TOKEN_SECRET is required to exercise signed quote edge cases',
  );
  const [encodedPayload] = token.split('.');
  const payload = JSON.parse(Buffer.from(encodedPayload, 'base64url').toString('utf8'));
  mutatePayload(payload);
  const resignedPayload = Buffer.from(JSON.stringify(payload), 'utf8').toString('base64url');
  const signature = crypto
    .createHmac('sha256', secret)
    .update(resignedPayload, 'ascii')
    .digest('base64url');
  return `${resignedPayload}.${signature}`;
}

async function getParcel(token) {
  return data(await api('GET', `/v1/parcels/${parcelId}`, { token }), 200);
}

async function connectSocket(token) {
  const socket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token },
    transports: ['websocket'],
    forceNew: true,
    reconnection: false,
    timeout: 8_000,
  });
  sockets.add(socket);
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Tracking socket connect timeout')), 8_000);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve();
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      reject(error);
    });
  });
  return socket;
}

function emitAck(socket, event, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} ack timeout`)), 8_000);
    socket.emit(event, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

function onceEvent(socket, event, timeoutMs = 15_000) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} event timeout`)), timeoutMs);
    socket.once(event, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

function optionalEvent(socket, event, timeoutMs = 5_000) {
  return new Promise((resolve) => {
    const handler = (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    };
    const timeout = setTimeout(() => {
      socket.off(event, handler);
      resolve(null);
    }, timeoutMs);
    socket.once(event, handler);
  });
}

async function exerciseLocation(fixture) {
  const query = encodeURIComponent(runTag);
  const root = data(
    await api('GET', `/v1/stations/search?locationScopeCode=${fixture.rootCode}&q=${query}`),
    200,
  );
  assert(root.length === 3, `Root hierarchy expected 3 stations, got ${root.length}`);
  assert(
    root.some((station) => station.id === ids.legacyRootStation),
    'Root station missing',
  );
  assert(
    root.some((station) => station.id === ids.originStation),
    'Origin leaf station missing',
  );
  assert(
    root.some((station) => station.id === ids.destinationStation),
    'Destination leaf station missing',
  );
  const leaf = data(
    await api('GET', `/v1/stations/search?locationScopeCode=${fixture.originCode}&q=${query}`),
    200,
  );
  assert(
    leaf.length === 1 && leaf[0].id === ids.originStation,
    `Leaf hierarchy leaked stations: ${JSON.stringify(leaf)}`,
  );
  data(
    await api(
      'GET',
      `/v1/stations/search?locationScopeCode=${fixture.rootCode}&locationId=${ids.originLocation}`,
    ),
    422,
    'VALIDATION_ERROR',
  );
  const tripSearch = data(
    await api(
      'GET',
      `/v1/trips/search?originProvinceCode=${fixture.rootCode}&destinationProvinceCode=${fixture.rootCode}&departureDate=${fixture.departureDate}&passengerCount=1`,
    ),
    200,
  );
  assert(
    JSON.stringify(tripSearch).includes(ids.trip),
    'Trip across two direct leaves of one root was not searchable',
  );
  pass('LOC-BE-001/PARCEL-BE-001 root+leaf station and trip search');
}

async function exerciseParcel(tokens, fixture) {
  const query = new URLSearchParams({
    originStationId: ids.originStation,
    destinationStationId: ids.destinationStation,
    departureDate: fixture.departureDate,
    lengthCm: '20',
    widthCm: '20',
    heightCm: '20',
    estimatedWeightKg: '3.2',
    page: '1',
    pageSize: '20',
  });
  const available = data(
    await api('GET', `/v1/parcels/available-trips?${query}`, { token: tokens.sender }),
    200,
  );
  const quote = available.items.find((item) => item.tripId === ids.trip);
  assert(quote, 'Boarding trip with active fare missing from Parcel availability');
  assert(quote.quoteToken && quote.quoteExpiresAt, 'Quote token/expiry missing');
  assert(quote.estimatedSizeCategory === 'SMALL', 'Canonical size should be SMALL');
  assert(quote.estimatedGrossPriceVnd === 3200, 'Canonical gross should be 3,200 VND');
  assert(quote.depositPercent === 20, 'Legacy 65% policy changed canonical 20% deposit');
  assert(quote.estimatedDepositVnd === 640, 'Canonical deposit should be 640 VND');

  const vouchers = await api(
    'GET',
    `/v1/parcels/vouchers/available?tripId=${ids.trip}&sizeCategory=SMALL&paymentMethod=WALLET&quoteToken=${encodeURIComponent(quote.quoteToken)}`,
    { token: tokens.sender },
  );
  data(vouchers, 200);

  const tokenParts = quote.quoteToken.split('.');
  assert(tokenParts.length === 2 && tokenParts[1], 'Quote token wire format is invalid');
  tokenParts[1] = `${tokenParts[1][0] === 'A' ? 'B' : 'A'}${tokenParts[1].slice(1)}`;
  const tampered = tokenParts.join('.');
  data(
    await api('POST', '/v1/parcels', {
      token: tokens.sender,
      key: nextKey(),
      body: parcelBody(quote, { quoteToken: tampered }),
    }),
    409,
    'PARCEL_QUOTE_INVALID',
  );
  const expired = resignQuoteToken(quote.quoteToken, (payload) => {
    payload.issuedAt = new Date(Date.now() - 60_000).toISOString();
    payload.expiresAt = new Date(Date.now() - 1_000).toISOString();
  });
  data(
    await api('POST', '/v1/parcels', {
      token: tokens.sender,
      key: nextKey(),
      body: parcelBody(quote, { quoteToken: expired }),
    }),
    409,
    'PARCEL_QUOTE_EXPIRED',
  );
  const stale = resignQuoteToken(quote.quoteToken, (payload) => {
    payload.settlementPolicyVersion += 1;
  });
  data(
    await api('POST', '/v1/parcels', {
      token: tokens.sender,
      key: nextKey(),
      body: parcelBody(quote, { quoteToken: stale }),
    }),
    409,
    'PARCEL_QUOTE_STALE',
  );
  data(
    await api(
      'GET',
      `/v1/parcels/vouchers/available?tripId=${ids.trip}&sizeCategory=MEDIUM&paymentMethod=WALLET&quoteToken=${encodeURIComponent(quote.quoteToken)}`,
      { token: tokens.sender },
    ),
    409,
    'PARCEL_QUOTE_MISMATCH',
  );
  const mismatchResponse = await api('POST', '/v1/parcels', {
    token: tokens.sender,
    key: nextKey(),
    body: parcelBody(quote, { sizeCategory: 'MEDIUM' }),
  });
  if (mismatchResponse.status === 201) {
    const unexpectedParcel = data(mismatchResponse, 201);
    createdParcelIds.push(unexpectedParcel.parcelId);
    audit(
      false,
      'Quote-token request field mismatch is rejected',
      `sizeCategory=MEDIUM with SMALL token created Parcel ${unexpectedParcel.parcelId}`,
    );
  } else {
    data(mismatchResponse, 409, 'PARCEL_QUOTE_MISMATCH');
    audit(true, 'Quote-token request field mismatch is rejected');
  }

  const nonCanonicalEmail = `  ${recipientEmail.toUpperCase()}  `;
  const recipientNormalizationProbe = data(
    await api('POST', '/v1/parcels', {
      token: tokens.sender,
      key: nextKey(),
      body: parcelBody(quote, {
        recipient: {
          fullName: `Recipient ${runTag}`,
          phoneNumber: '+84910000017',
          email: nonCanonicalEmail,
        },
      }),
    }),
    201,
  );
  createdParcelIds.push(recipientNormalizationProbe.parcelId);
  assert(
    parcelSql(
      `SELECT recipient_user_id::text FROM parcels WHERE id='${recipientNormalizationProbe.parcelId}'`,
    ) === ids.recipient,
    'Trimmed/lowercase recipient lookup did not link recipient_user_id',
  );
  const persistedRecipientEmail = parcelSql(
    `SELECT recipient_email FROM parcels WHERE id='${recipientNormalizationProbe.parcelId}'`,
  );
  audit(
    persistedRecipientEmail === recipientEmail.toLowerCase(),
    'Recipient email is canonicalized before downstream delivery email',
    `persisted=${JSON.stringify(persistedRecipientEmail)}`,
  );

  const created = data(
    await api('POST', '/v1/parcels', {
      token: tokens.sender,
      key: nextKey(),
      body: parcelBody(quote),
    }),
    201,
  );
  parcelId = created.parcelId;
  createdParcelIds.push(parcelId);
  assert(created.estimatedGrossPriceVnd === 3200, 'Create/search gross drifted');
  assert(created.depositRequiredVnd === 640, 'Create/search deposit drifted');
  assert(
    parcelSql(`SELECT recipient_user_id::text FROM parcels WHERE id='${parcelId}'`) ===
      ids.recipient,
    'Normalized recipient email was not linked to recipient_user_id',
  );
  const received = data(
    await api('GET', '/v1/parcels/received?page=1&pageSize=20', { token: tokens.recipient }),
    200,
  );
  assert(JSON.stringify(received).includes(parcelId), 'Recipient /received did not include Parcel');

  data(
    await api('POST', `/v1/parcels/${parcelId}/deposit-payment`, {
      token: tokens.sender,
      key: nextKey(),
      body: { paymentMethod: 'WALLET' },
    }),
    200,
  );
  await poll(
    'Wallet deposit event reserves Parcel and Trip cargo',
    () => getParcel(tokens.sender),
    (parcel) => parcel.status === 'RESERVED' && parcel.depositPaidVnd === 640,
  );
  const checkedIn = data(
    await api('POST', `/v1/assistant/parcels/${parcelId}/check-in`, {
      token: tokens.assistant,
      key: nextKey(),
      body: { tripId: ids.trip, parcelCode: created.parcelCode, photoUrls: [] },
    }),
    200,
  );
  assert(checkedIn.status === 'CHECKED_IN', `Expected CHECKED_IN, got ${checkedIn.status}`);
  const reweighed = data(
    await api('POST', `/v1/assistant/parcels/${parcelId}/reweigh`, {
      token: tokens.assistant,
      key: nextKey(),
      body: {
        actualLengthCm: 20,
        actualWidthCm: 20,
        actualHeightCm: 20,
        actualWeightKg: 3.2,
      },
    }),
    200,
  );
  assert(
    reweighed.status === 'PENDING_FINAL_PAYMENT',
    `Expected PENDING_FINAL_PAYMENT, got ${reweighed.status}`,
  );
  data(
    await api('POST', `/v1/parcels/${parcelId}/final-payment`, {
      token: tokens.sender,
      key: nextKey(),
      body: { paymentMethod: 'WALLET' },
    }),
    200,
  );
  await poll(
    'Wallet final-payment event makes Parcel ready to load',
    () => getParcel(tokens.sender),
    (parcel) => parcel.status === 'READY_TO_LOAD' && parcel.balancePaidVnd === 2560,
  );
  const loaded = data(
    await api('POST', `/v1/assistant/parcels/${parcelId}/load`, {
      token: tokens.assistant,
      key: nextKey(),
      body: { tripId: ids.trip, parcelCode: created.parcelCode },
    }),
    200,
  );
  assert(loaded.status === 'LOADED', `Expected LOADED, got ${loaded.status}`);
  pass('PARCEL quote HMAC, recipient link, Wallet payment, cargo reserve and load');
}

async function exerciseTrackingIncidentAndLifecycle(tokens) {
  tripSql(`UPDATE trips SET status='BOARDING',updated_at=now() WHERE id='${ids.trip}';`);
  pass('fixture advanced SCHEDULED -> BOARDING before driver start API');
  const cold = await trackingApi(`/v1/tracking/trips/${ids.trip}/eta`, tokens.sender);
  assert(cold.status === 200 && cold.json?.data?.eta === null, 'Cold ETA must be null');

  const routeBefore = await trackingApi(
    `/v1/tracking/trips/${ids.trip}/route-geometry`,
    tokens.driver,
  );
  assert(routeBefore.status === 200, `Route context failed: ${JSON.stringify(routeBefore.json)}`);
  assert(
    routeBefore.json?.data?.tripStatus === 'BOARDING',
    'Pre-start route status is not BOARDING',
  );
  assert(routeBefore.etag, 'REST route context ETag missing');

  let socket = await connectSocket(tokens.driver);
  const join = await emitAck(socket, 'joinTripTracking', {
    tripId: ids.trip,
    includeRouteSnapshot: true,
  });
  assert(join.success === true, `Socket join failed: ${JSON.stringify(join)}`);
  assert(join.routeVersion === routeBefore.etag, 'REST/Socket route version mismatch');
  assert(join.routeContext?.tripStatus === 'BOARDING', 'Socket pre-start route status drifted');

  const preOriginBatchPromise = onceEvent(socket, 'eta:batch:update');
  const preOriginRecordedAt = new Date().toISOString();
  const preOriginAck = await emitAck(socket, 'gps:update', {
    tripId: ids.trip,
    latitude: 10.75,
    longitude: 106.65,
    speedKmh: 35,
    headingDeg: 45,
    recordedAt: preOriginRecordedAt,
  });
  assert(preOriginAck.success === true, `Pre-origin GPS failed: ${JSON.stringify(preOriginAck)}`);
  const preOriginBatch = await preOriginBatchPromise;
  assert(
    preOriginBatch.etas?.[0]?.targetKind === 'STATION' &&
      preOriginBatch.etas[0].stationId === ids.originStation,
    `Pre-origin ETA did not target origin: ${JSON.stringify(preOriginBatch)}`,
  );
  if (process.env.EXPECT_GOONG === 'true') {
    audit(
      preOriginBatch.etas?.[0]?.estimateQuality === 'ROUTE_BASED',
      'Integrated Tracking ETA uses the real Goong Directions provider',
      JSON.stringify(preOriginBatch.etas?.[0] ?? null),
    );
  }
  const originEta = await trackingApi(`/v1/tracking/trips/${ids.trip}/eta`, tokens.sender);
  assert(originEta.json?.data?.eta?.targetKind === 'STATION', 'GET /eta did not return origin ETA');
  evidence.dynamicEta = {
    preOrigin: {
      gps: { latitude: 10.75, longitude: 106.65, speedKmh: 35, recordedAt: preOriginRecordedAt },
      targets: (preOriginBatch.etas ?? []).map((eta) =>
        projectEtaEvidence(eta, preOriginRecordedAt),
      ),
    },
  };
  pass('ETA cold cache and real pre-origin GPS -> origin station');

  data(
    await api('POST', `/v1/driver/trips/${ids.trip}/start`, {
      token: tokens.driver,
      key: nextKey(),
    }),
    200,
  );
  await poll(
    'TripStarted RabbitMQ event advances loaded Parcel to IN_TRANSIT',
    () => getParcel(tokens.sender),
    (parcel) => parcel.status === 'IN_TRANSIT',
  );
  await poll(
    'Tracking consumes TripStarted and invalidates BOARDING route cache',
    () => trackingApi(`/v1/tracking/trips/${ids.trip}/route-geometry`, tokens.driver),
    (response) => response.status === 200 && response.json?.data?.tripStatus === 'IN_PROGRESS',
    20_000,
  );

  const afterStartBatchPromise = optionalEvent(socket, 'eta:batch:update');
  const afterStartRecordedAt = new Date(Date.now() + 1_000).toISOString();
  const afterStartAck = await emitAck(socket, 'gps:update', {
    tripId: ids.trip,
    latitude: 10.762,
    longitude: 106.662,
    speedKmh: 35,
    headingDeg: 45,
    recordedAt: afterStartRecordedAt,
  });
  assert(afterStartAck.success === true, 'Post-start GPS was rejected');
  const afterStartBatch = await afterStartBatchPromise;
  const etaImmediatelyAfterStart = await trackingApi(
    `/v1/tracking/trips/${ids.trip}/eta`,
    tokens.sender,
  );
  audit(
    afterStartBatch?.etas?.[0]?.targetKind === 'STOP' &&
      afterStartBatch.etas[0].stopId === ids.stop &&
      afterStartBatch.etas.some(
        (eta) => eta.targetKind === 'STATION' && eta.stationId === ids.destinationStation,
      ) &&
      etaImmediatelyAfterStart.json?.data?.eta?.targetKind === 'STOP',
    'ETA switches from origin to next stop immediately after Trip start',
    JSON.stringify({ event: afterStartBatch, rest: etaImmediatelyAfterStart.json?.data }),
  );

  socket.disconnect();
  sockets.delete(socket);
  socket = await connectSocket(tokens.driver);
  const reconnect = await emitAck(socket, 'joinTripTracking', {
    tripId: ids.trip,
    includeRouteSnapshot: true,
  });
  assert(reconnect.success === true, `Reconnect join failed: ${JSON.stringify(reconnect)}`);
  assert(reconnect.routeContext?.tripStatus === 'IN_PROGRESS', 'Reconnect snapshot is not current');
  assert(
    reconnect.routeVersion !== join.routeVersion,
    'Route ETag did not change with trip status',
  );

  const cachedInProgressEtas = await trackingApi(
    `/v1/tracking/trips/${ids.trip}/etas`,
    tokens.sender,
  );
  assert(cachedInProgressEtas.status === 200, 'Cached IN_PROGRESS ETA chain was unavailable');
  assert(
    cachedInProgressEtas.json?.data?.etas?.[0]?.targetKind === 'STOP' &&
      cachedInProgressEtas.json.data.etas.some(
        (eta) => eta.targetKind === 'STATION' && eta.stationId === ids.destinationStation,
      ),
    'Cached IN_PROGRESS ETA chain omitted stop or destination',
  );
  const inProgressTargets = cachedInProgressEtas.json?.data?.etas ?? [];
  const stopTarget = inProgressTargets.find((eta) => eta.targetKind === 'STOP');
  const destinationTarget = inProgressTargets.find(
    (eta) => eta.targetKind === 'STATION' && eta.stationId === ids.destinationStation,
  );
  evidence.dynamicEta.inProgress = {
    gps: { latitude: 10.762, longitude: 106.662, speedKmh: 35, recordedAt: afterStartRecordedAt },
    targets: inProgressTargets.map((eta) => projectEtaEvidence(eta, afterStartRecordedAt)),
    stopToDestinationMinutes:
      stopTarget && destinationTarget
        ? Math.round(
            ((Date.parse(destinationTarget.estimatedArrivalTime) -
              Date.parse(stopTarget.estimatedArrivalTime)) /
              60_000) *
              100,
          ) / 100
        : null,
  };
  pass(
    'Socket reconnect receives current snapshot/version and cached stop -> destination ETA chain',
  );

  const incident = data(
    await api('POST', `/v1/driver/trips/${ids.trip}/incident`, {
      token: tokens.driver,
      key: nextKey(),
      body: {
        category: 'TRAFFIC_JAM',
        description: `Traffic incident ${runTag}`,
        photoUrls: [],
        latitude: 10.764,
        longitude: 106.664,
      },
    }),
    201,
  );
  const incidentId = incident.incidentId;
  data(
    await api('PATCH', `/v1/operator/incidents/${incidentId}/resolve`, {
      token: tokens.staff,
      key: nextKey(),
      body: { resolutionNote: 'Staff must not resolve' },
    }),
    403,
    'FORBIDDEN',
  );
  data(
    await api('PATCH', `/v1/operator/incidents/${incidentId}/resolve`, {
      token: tokens.foreignAdmin,
      key: nextKey(),
      body: { resolutionNote: 'Cross tenant' },
    }),
    404,
    'INCIDENT_NOT_FOUND',
  );
  const resolveKey = nextKey();
  const resolved = data(
    await api('PATCH', `/v1/operator/incidents/${incidentId}/resolve`, {
      token: tokens.admin,
      key: resolveKey,
      body: { resolutionNote: '  Switched to the verified bypass route.  ' },
    }),
    200,
  );
  assert(resolved.status === 'RESOLVED', 'Resolved incident status is not RESOLVED');
  assert(resolved.resolvedByUserId === ids.admin, 'resolvedByUserId is not JWT sub');
  assert(resolved.resolutionNote === 'Switched to the verified bypass route.', 'Note not trimmed');
  const replay = data(
    await api('PATCH', `/v1/operator/incidents/${incidentId}/resolve`, {
      token: tokens.admin,
      key: resolveKey,
      body: { resolutionNote: '  Switched to the verified bypass route.  ' },
    }),
    200,
  );
  assert(replay.resolvedAt === resolved.resolvedAt, 'Same-key resolve did not replay result');
  data(
    await api('PATCH', `/v1/operator/incidents/${incidentId}/resolve`, {
      token: tokens.admin,
      key: nextKey(),
      body: { resolutionNote: 'Second resolution' },
    }),
    409,
    'INCIDENT_ALREADY_RESOLVED',
  );
  const openList = data(
    await api('GET', `/v1/operator/incidents?tripId=${ids.trip}&status=OPEN`, {
      token: tokens.admin,
    }),
    200,
  );
  assert(!JSON.stringify(openList).includes(incidentId), 'Resolved incident remained in OPEN list');
  pass('Incident report -> tenant/role guards -> resolve -> replay -> OPEN list removal');

  data(
    await api('POST', `/v1/driver/trips/${ids.trip}/stops/${ids.stop}/arrive`, {
      token: tokens.driver,
      key: nextKey(),
    }),
    200,
  );
  data(
    await api('POST', `/v1/driver/trips/${ids.trip}/stops/${ids.stop}/depart`, {
      token: tokens.driver,
      key: nextKey(),
    }),
    200,
  );
  const destinationBatchPromise = optionalEvent(socket, 'eta:batch:update');
  const afterStopRecordedAt = new Date(Date.now() + 3_000).toISOString();
  assert(
    (
      await emitAck(socket, 'gps:update', {
        tripId: ids.trip,
        latitude: 10.77,
        longitude: 106.67,
        speedKmh: 35,
        headingDeg: 45,
        recordedAt: afterStopRecordedAt,
      })
    ).success === true,
    'After-stop GPS was rejected',
  );
  const destinationBatch = await destinationBatchPromise;
  audit(
    destinationBatch?.etas?.[0]?.targetKind === 'STATION' &&
      destinationBatch.etas[0].stationId === ids.destinationStation,
    'ETA refreshes destination immediately after the last stop',
    JSON.stringify(destinationBatch),
  );
  const allEtas = await trackingApi(`/v1/tracking/trips/${ids.trip}/etas`, tokens.recipient);
  assert(
    allEtas.status === 200 &&
      allEtas.json?.data?.etas?.some(
        (eta) => eta.targetKind === 'STATION' && eta.stationId === ids.destinationStation,
      ),
    'GET /etas omitted cached destination ETA',
  );
  const afterStopTargets = allEtas.json?.data?.etas ?? [];
  assert(
    afterStopTargets.length === 1 &&
      afterStopTargets[0]?.targetKind === 'STATION' &&
      afterStopTargets[0]?.stationId === ids.destinationStation,
    `GET /etas retained a completed stop: ${JSON.stringify(afterStopTargets)}`,
  );
  evidence.dynamicEta.afterLastStop = {
    gps: { latitude: 10.77, longitude: 106.67, speedKmh: 35, recordedAt: afterStopRecordedAt },
    targets: afterStopTargets.map((eta) => projectEtaEvidence(eta, afterStopRecordedAt)),
  };

  data(
    await api('POST', `/v1/driver/trips/${ids.trip}/destination/arrive`, {
      token: tokens.driver,
      key: nextKey(),
    }),
    200,
  );
  const unloaded = data(
    await api('POST', `/v1/assistant/parcels/${parcelId}/unload`, {
      token: tokens.assistant,
      key: nextKey(),
    }),
    200,
  );
  assert(unloaded.status === 'UNLOADED', `Expected UNLOADED, got ${unloaded.status}`);
  const delivered = data(
    await api('POST', `/v1/assistant/parcels/${parcelId}/deliver`, {
      token: tokens.assistant,
      key: nextKey(),
      body: { photoUrls: [] },
    }),
    200,
  );
  assert(
    delivered.status === 'DELIVERED_PENDING_CONFIRM',
    `Expected DELIVERED_PENDING_CONFIRM, got ${delivered.status}`,
  );
  const confirmed = data(
    await api('POST', `/v1/crew/parcels/${parcelId}/manual-confirm`, {
      token: tokens.assistant,
      key: nextKey(),
      body: { confirmNote: 'Recipient verified in person.' },
    }),
    200,
  );
  assert(confirmed.status === 'DELIVERY_CONFIRMED', 'Parcel delivery was not confirmed');
  data(
    await api('POST', `/v1/driver/trips/${ids.trip}/complete`, {
      token: tokens.driver,
      key: nextKey(),
    }),
    200,
  );
  await poll(
    'Trip and Parcel terminal state persisted after full journey',
    () => ({
      trip: tripSql(`SELECT status::text FROM trips WHERE id='${ids.trip}'`),
      parcel: parcelSql(`SELECT status::text FROM parcels WHERE id='${parcelId}'`),
    }),
    (state) => state.trip === 'COMPLETED' && state.parcel === 'DELIVERY_CONFIRMED',
  );
  assert(
    tripSql(
      `SELECT count(*) FROM incidents WHERE id='${incidentId}' AND resolved_at IS NOT NULL AND resolved_by_user_id='${ids.admin}' AND resolution_note='Switched to the verified bypass route.'`,
    ) === '1',
    'Incident resolve columns were not persisted',
  );
  assert(
    tripSql(
      `SELECT state::text FROM trip_cargo_parcels WHERE trip_id='${ids.trip}' AND parcel_id='${parcelId}'`,
    ) === 'RELEASED',
    'Parcel cargo ledger was not released after unload',
  );
  pass('stop/destination ETA, unload/deliver/confirm, Trip complete and DB ledger');
}

function cleanupRedis() {
  const needles = [ids.trip, ...generatedTripIds, ...createdParcelIds, ...idempotencyKeys].filter(
    Boolean,
  );
  for (const needle of needles) {
    const keys = run('docker', [
      'exec',
      redisContainer,
      'redis-cli',
      '--scan',
      '--pattern',
      `*${needle}*`,
    ])
      .split(/\r?\n/u)
      .filter(Boolean);
    if (keys.length > 0) run('docker', ['exec', redisContainer, 'redis-cli', 'DEL', ...keys]);
  }
}

function cleanup() {
  for (const socket of sockets) socket.disconnect();
  sockets.clear();
  try {
    const notificationPredicate = [
      `data::text LIKE '%${ids.trip}%'`,
      ...generatedTripIds.map((id) => `data::text LIKE '%${id}%'`),
      ...createdParcelIds.map((id) => `data::text LIKE '%${id}%'`),
    ].join(' OR ');
    notificationSql(`
      DELETE FROM email_deliveries WHERE lower(to_email)=lower('${recipientEmail}');
      DELETE FROM notification_deliveries WHERE notification_id IN
        (SELECT id FROM notifications WHERE ${notificationPredicate});
      DELETE FROM notifications WHERE ${notificationPredicate};
    `);
  } catch {
    // Notification is not required for cleanup of the feature-owned aggregates.
  }
  if (createdParcelIds.length > 0) {
    const parcelIdList = [...new Set(createdParcelIds)].map((id) => `'${id}'`).join(',');
    parcelSql(`
      BEGIN;
      ALTER TABLE parcel_status_history DISABLE TRIGGER trg_parcel_status_history_immutable;
      DELETE FROM platform_parcel_stats WHERE parcel_id IN (${parcelIdList});
      DELETE FROM parcel_delivery_tokens WHERE parcel_id IN (${parcelIdList});
      DELETE FROM parcel_status_history WHERE parcel_id IN (${parcelIdList});
      DELETE FROM parcel_cargo_recovery_operations WHERE parcel_id IN (${parcelIdList});
      DELETE FROM outbox_events WHERE ${[...new Set(createdParcelIds)].map((id) => `payload::text LIKE '%${id}%'`).join(' OR ')};
      DELETE FROM parcels WHERE id IN (${parcelIdList});
      ALTER TABLE parcel_status_history ENABLE TRIGGER trg_parcel_status_history_immutable;
      COMMIT;
    `);
    paymentSql(`
      DELETE FROM invoices WHERE payment_id IN (SELECT id FROM payments WHERE reference_id IN (${parcelIdList}));
      DELETE FROM wallet_transactions WHERE reference_id IN (${parcelIdList});
      DELETE FROM platform_wallet_transactions WHERE reference_id IN (${parcelIdList});
      DELETE FROM operator_ledger_entries WHERE reference_id IN (${parcelIdList});
      DELETE FROM outbox_events WHERE ${[...new Set(createdParcelIds)].map((id) => `payload::text LIKE '%${id}%'`).join(' OR ')};
      DELETE FROM payments WHERE reference_id IN (${parcelIdList});
    `);
  }
  parcelSql(`
    DELETE FROM operator_deposit_policies WHERE id='${ids.depositPolicy}';
    DELETE FROM parcel_route_fares WHERE route_id='${ids.route}';
  `);
  paymentSql(`DELETE FROM wallets WHERE user_id='${ids.sender}';`);
  if (paymentPlatformSnapshot) {
    const [walletId, balance, rowVersion] = paymentPlatformSnapshot.split('|');
    paymentSql(`
      UPDATE platform_wallets SET balance=${balance},row_version=${rowVersion},updated_at=now()
      WHERE id='${walletId}';
    `);
  }
  if (generatedTripIds.length > 0) {
    const generatedTripIdList = generatedTripIds.map((id) => `'${id}'`).join(',');
    tripSql(`
      DELETE FROM platform_trip_stats WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM incidents WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM trip_audit_logs WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM trip_cargo_parcels WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM trip_stop_fares WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM trip_stops WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM trip_seats WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM resource_reservations WHERE trip_id IN (${generatedTripIdList});
      DELETE FROM outbox_events WHERE ${generatedTripIds.map((id) => `payload::text LIKE '%${id}%'`).join(' OR ')};
      DELETE FROM trips WHERE id IN (${generatedTripIdList});
    `);
    identitySql(`
      DELETE FROM subscription_quota_allocations WHERE resource_id IN (${generatedTripIdList});
    `);
  }
  if (driverScheduleId) {
    tripSql(`
      DELETE FROM trip_generation_skip_logs WHERE driver_schedule_id='${driverScheduleId}';
      DELETE FROM driver_schedule_audit_logs WHERE driver_schedule_id='${driverScheduleId}';
      DELETE FROM driver_schedules WHERE id='${driverScheduleId}';
    `);
  }
  tripSql(`
    DELETE FROM platform_trip_stats WHERE trip_id='${ids.trip}';
    DELETE FROM incidents WHERE trip_id='${ids.trip}';
    DELETE FROM trip_audit_logs WHERE trip_id='${ids.trip}';
    DELETE FROM trip_cargo_parcels WHERE trip_id='${ids.trip}';
    DELETE FROM trip_stop_fares WHERE trip_id='${ids.trip}';
    DELETE FROM trip_stops WHERE trip_id='${ids.trip}';
    DELETE FROM trip_seats WHERE trip_id='${ids.trip}';
    DELETE FROM outbox_events WHERE payload::text LIKE '%${ids.trip}%';
    DELETE FROM trips WHERE id='${ids.trip}';
    DELETE FROM route_stops WHERE route_id='${ids.route}';
    DELETE FROM routes WHERE id='${ids.route}';
    DELETE FROM stops WHERE id='${ids.stop}';
    DELETE FROM vehicles WHERE id='${ids.vehicle}';
    DELETE FROM vehicle_types WHERE id='${ids.vehicleType}';
    DELETE FROM stations WHERE id IN ('${ids.legacyRootStation}','${ids.originStation}','${ids.destinationStation}');
    DELETE FROM locations WHERE id IN ('${ids.originLocation}','${ids.destinationLocation}');
    DELETE FROM locations WHERE id='${ids.rootLocation}';
  `);
  identitySql(`
    DELETE FROM operator_subscriptions WHERE id='${ids.subscription}';
    DELETE FROM users WHERE id IN
      ('${ids.admin}','${ids.staff}','${ids.foreignAdmin}','${ids.driver}','${ids.assistant}','${ids.sender}','${ids.recipient}');
    DELETE FROM operators WHERE id IN ('${ids.operator}','${ids.foreignOperator}');
    DELETE FROM subscription_plans WHERE id='${ids.plan}';
  `);
  cleanupRedis();
  pass('feature E2E database and Redis fixtures cleaned');
}

let failure;
try {
  for (const url of [
    `${gateway}/health`,
    `${tracking}/health`,
    'http://localhost:5001/health',
    'http://localhost:5002/health',
    'http://localhost:5004/health',
    'http://localhost:5005/health',
  ]) {
    const response = await fetch(url, { signal: AbortSignal.timeout(8_000) });
    assert(response.ok, `${url} is not healthy`);
  }
  const fixture = seedFixtures();
  const tokens = {
    admin: await mintToken(ids.admin, 'OPERATOR_ADMIN', ids.operator),
    staff: await mintToken(ids.staff, 'OPERATOR_STAFF', ids.operator),
    foreignAdmin: await mintToken(ids.foreignAdmin, 'OPERATOR_ADMIN', ids.foreignOperator),
    driver: await mintToken(ids.driver, 'DRIVER', ids.operator),
    assistant: await mintToken(ids.assistant, 'ASSISTANT', ids.operator),
    sender: await mintToken(ids.sender, 'PASSENGER'),
    recipient: await mintToken(ids.recipient, 'PASSENGER', null, recipientEmail),
  };
  await exerciseImmediateDriverScheduleGeneration(tokens);
  await exerciseLocation(fixture);
  await exerciseParcel(tokens, fixture);
  await exerciseTrackingIncidentAndLifecycle(tokens);
  if (auditFailures.length > 0) {
    throw new Error(`Audit failures: ${auditFailures.join(' | ')}`);
  }
} catch (error) {
  failure = error;
  console.error(
    `FAILURE | ${error instanceof Error ? error.stack || error.message : String(error)}`,
  );
  for (const service of ['vietride_trip', 'vietride_parcel', 'vietride_tracking']) {
    try {
      const logs = run('docker', ['logs', '--tail', '100', service]);
      const relevant = logs
        .split(/\r?\n/u)
        .filter((line) => /error|exception|failed|ETA|incident|parcel/iu.test(line))
        .slice(-30)
        .join('\n');
      console.error(`DIAGNOSTIC ${service}\n${relevant || '(no relevant logs)'}`);
    } catch {
      // Cleanup below remains mandatory.
    }
  }
} finally {
  try {
    cleanup();
  } catch (error) {
    failure ||= error;
    console.error(`CLEANUP FAILURE | ${error instanceof Error ? error.message : String(error)}`);
  }
}

console.log(
  JSON.stringify(
    {
      suite: 'incident-parcel-eta-lifecycle-e2e',
      runTag,
      assertions,
      auditFailures,
      evidence,
      passed: !failure,
      failure: failure instanceof Error ? failure.message : failure ? String(failure) : null,
    },
    null,
    2,
  ),
);
process.exitCode = failure ? 1 : 0;
