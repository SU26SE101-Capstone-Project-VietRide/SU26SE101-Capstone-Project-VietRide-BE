import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { importPKCS8, SignJWT } from 'jose';
import { io } from 'socket.io-client';
import { day36IdempotencyKey } from './day36-idempotency-keys.mjs';

const root = process.cwd();
const useDev = process.env.DAY36_E2E_USE_DEV_STACK === '1';
const gateway =
  process.env.DAY36_GATEWAY_BASE_URL ||
  (useDev ? 'http://localhost:3000' : 'http://localhost:56300');
const tracking =
  process.env.DAY36_TRACKING_BASE_URL ||
  (useDev ? 'http://localhost:3001' : 'http://localhost:56011');
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day36-e2e.yml',
  '--profile',
  'app',
];
const postgres = useDev ? 'vietride_postgres' : 'day36-e2e-postgres';
const redis = useDev ? 'vietride_redis' : 'day36-e2e-redis';
const ids = {
  operatorA: '36000000-0000-4000-8000-000000000001',
  operatorB: '36000000-0000-4000-8000-000000000002',
  admin: '36000000-0000-4000-8000-000000000011',
  staff: '36000000-0000-4000-8000-000000000012',
  mainDriver: '36000000-0000-4000-8000-000000000013',
  driverA: '36000000-0000-4000-8000-000000000014',
  driverB: '36000000-0000-4000-8000-000000000015',
  otherDriver: '36000000-0000-4000-8000-000000000016',
  passengers: Array.from({ length: 5 }, (_, i) => `36000000-0000-4000-8000-00000000002${i + 1}`),
  origin: '36000000-0000-4000-8000-000000000101',
  destination: '36000000-0000-4000-8000-000000000102',
  unsupported: '36000000-0000-4000-8000-000000000103',
  stop: '36000000-0000-4000-8000-000000000104',
  route: '36000000-0000-4000-8000-000000000111',
  vehicleType: '36000000-0000-4000-8000-000000000121',
  mainVehicle: '36000000-0000-4000-8000-000000000122',
  shuttle12: '36000000-0000-4000-8000-000000000123',
  shuttle4: '36000000-0000-4000-8000-000000000124',
  otherVehicle: '36000000-0000-4000-8000-000000000125',
  mainTrip: '36000000-0000-4000-8000-000000000131',
  conflictTrip: '36000000-0000-4000-8000-000000000136',
  warning120Trip: '36000000-0000-4000-8000-000000000132',
  warning60Trip: '36000000-0000-4000-8000-000000000133',
  cutoffTrip: '36000000-0000-4000-8000-000000000134',
  raceTrip: '36000000-0000-4000-8000-000000000135',
};
const results = [];

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...options.env },
  });
  if (result.status !== 0)
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
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
    process.env.POSTGRES_USER || 'vietride',
    '-d',
    database,
    '-qAtc',
    `SET search_path TO ${schema},public; ${statement}`,
  ]);
}

function redisCommand(...args) {
  return run('docker', ['exec', redis, 'redis-cli', ...args]);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function record(name, detail = 'PASS') {
  results.push({ name, passed: true, detail });
  console.log(`PASS | ${name} | ${detail}`);
}

async function waitFor(url, timeoutMs = 240_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
      // The service may still be starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`Timed out waiting for ${url}`);
}

async function poll(fn, message, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await fn();
    if (value) return value;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(message);
}

function seed() {
  const users = [
    [
      ids.admin,
      'day36-admin@example.test',
      '+84910036001',
      'Day 36 Admin',
      'OPERATOR_ADMIN',
      ids.operatorA,
    ],
    [
      ids.staff,
      'day36-staff@example.test',
      '+84910036002',
      'Day 36 Staff',
      'OPERATOR_STAFF',
      ids.operatorA,
    ],
    [
      ids.mainDriver,
      'day36-main-driver@example.test',
      '+84910036003',
      'Main Driver',
      'DRIVER',
      ids.operatorA,
    ],
    [
      ids.driverA,
      'day36-driver-a@example.test',
      '+84910036004',
      'Shuttle Driver A',
      'DRIVER',
      ids.operatorA,
    ],
    [
      ids.driverB,
      'day36-driver-b@example.test',
      '+84910036005',
      'Shuttle Driver B',
      'DRIVER',
      ids.operatorA,
    ],
    [
      ids.otherDriver,
      'day36-other-driver@example.test',
      '+84910036006',
      'Other Driver',
      'DRIVER',
      ids.operatorB,
    ],
    ...ids.passengers.map((id, index) => [
      id,
      `day36-p${index + 1}@example.test`,
      `+8491003610${index}`,
      `Passenger ${index + 1}`,
      'PASSENGER',
      null,
    ]),
  ];
  sql(
    'vietride_identity',
    'vietride_identity',
    `
    INSERT INTO operators (id,name,business_registration_number,tax_code,contact_email,contact_phone,registration_status,approved_at,is_active)
    VALUES
      ('${ids.operatorA}','Day 36 Operator A','D36-A-BRN','D36-A-TAX','a@day36.test','+84910036991','APPROVED',now(),true),
      ('${ids.operatorB}','Day 36 Operator B','D36-B-BRN','D36-B-TAX','b@day36.test','+84910036992','APPROVED',now(),true)
    ON CONFLICT (id) DO UPDATE SET registration_status='APPROVED',is_active=true,deleted_at=NULL;
    ${users
      .map(
        ([id, email, phone, name, role, operatorId]) => `
      INSERT INTO users (id,email,phone,display_name,role,status,operator_id)
      VALUES ('${id}','${email}','${phone}','${name}','${role}', 'ACTIVE', ${operatorId ? `'${operatorId}'` : 'NULL'})
      ON CONFLICT (id) DO UPDATE SET status='ACTIVE',deleted_at=NULL;`,
      )
      .join('')}
    ${ids.passengers
      .map(
        (id, index) => `
      INSERT INTO user_devices (id,user_id,fcm_token,platform,is_active)
      VALUES ('36000000-0000-4000-8000-0000000003${index + 1}1','${id}','day36-token-${index + 1}','ANDROID',true)
      ON CONFLICT (user_id,fcm_token) DO UPDATE SET is_active=true;`,
      )
      .join('')}
  `,
  );

  sql(
    'vietride_trip',
    'vietride_trip',
    `
    INSERT INTO stations (id,name,slug,city,province,latitude,longitude,supports_shuttle,is_active)
    VALUES
      ('${ids.origin}','Day 36 Origin','day36-origin','HCM','HCM',10.7765000,106.7009000,true,true),
      ('${ids.destination}','Day 36 Destination','day36-destination','Dong Nai','Dong Nai',10.9500000,106.8200000,false,true),
      ('${ids.unsupported}','Day 36 Unsupported','day36-unsupported','HCM','HCM',10.7600000,106.6800000,false,true)
    ON CONFLICT (id) DO UPDATE SET supports_shuttle=EXCLUDED.supports_shuttle,is_active=true,deleted_at=NULL;
    INSERT INTO routes (id,operator_id,name,origin_station_id,destination_station_id,base_fare,is_active)
    VALUES ('${ids.route}','${ids.operatorA}','Day 36 Route','${ids.origin}','${ids.destination}',100000,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true,deleted_at=NULL;
    INSERT INTO vehicle_types (id,code,display_name,default_seat_count,is_active)
    VALUES ('${ids.vehicleType}','DAY36_BUS','Day 36 Bus',20,true)
    ON CONFLICT (id) DO UPDATE SET is_active=true;
    INSERT INTO vehicles (id,operator_id,vehicle_type_id,license_plate,seat_layout_json,total_seats,status,is_active)
    VALUES
      ('${ids.mainVehicle}','${ids.operatorA}','${ids.vehicleType}','51B-360.01','{}',20,'ACTIVE',true),
      ('${ids.shuttle12}','${ids.operatorA}','${ids.vehicleType}','51B-360.12','{}',12,'ACTIVE',true),
      ('${ids.shuttle4}','${ids.operatorA}','${ids.vehicleType}','51B-360.04','{}',4,'ACTIVE',true),
      ('${ids.otherVehicle}','${ids.operatorB}','${ids.vehicleType}','51B-360.99','{}',12,'ACTIVE',true)
    ON CONFLICT (id) DO UPDATE SET status='ACTIVE',is_active=true,deleted_at=NULL;
    INSERT INTO trips (id,operator_id,route_id,vehicle_id,driver_user_id,departure_date_time,estimated_arrival_time,status,source,base_fare)
    VALUES
      ('${ids.mainTrip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '6 hours',now()+interval '8 hours','SCHEDULED','MANUAL',100000),
      ('${ids.warning120Trip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '6 hours 1 minute',now()+interval '8 hours 1 minute','SCHEDULED','MANUAL',100000),
      ('${ids.warning60Trip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '6 hours 2 minutes',now()+interval '8 hours 2 minutes','SCHEDULED','MANUAL',100000),
      ('${ids.cutoffTrip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '6 hours 3 minutes',now()+interval '8 hours 3 minutes','SCHEDULED','MANUAL',100000),
      ('${ids.raceTrip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '6 hours 4 minutes',now()+interval '8 hours 4 minutes','SCHEDULED','MANUAL',100000),
      ('${ids.conflictTrip}','${ids.operatorA}','${ids.route}','${ids.mainVehicle}','${ids.mainDriver}',now()+interval '2 hours',now()+interval '3 hours','SCHEDULED','MANUAL',100000)
    ON CONFLICT (id) DO UPDATE SET departure_date_time=EXCLUDED.departure_date_time,estimated_arrival_time=EXCLUDED.estimated_arrival_time,status='SCHEDULED';
    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    SELECT ('36000000-0000-4000-8000-' || lpad((4000+n)::text,12,'0'))::uuid,'${ids.mainTrip}','D'||lpad(n::text,2,'0'),'STANDARD','AVAILABLE'
    FROM generate_series(1,20) n ON CONFLICT (trip_id,seat_number) DO UPDATE SET status='AVAILABLE',disabled_reason=NULL;
    INSERT INTO trip_seats (id,trip_id,seat_number,seat_type,status)
    SELECT gen_random_uuid(), trip_id, 'D01', 'STANDARD', 'AVAILABLE'
    FROM (VALUES
      ('${ids.warning120Trip}'::uuid),('${ids.warning60Trip}'::uuid),
      ('${ids.cutoffTrip}'::uuid),('${ids.raceTrip}'::uuid)) fixture(trip_id)
    ON CONFLICT (trip_id,seat_number) DO UPDATE SET status='AVAILABLE',disabled_reason=NULL;
  `,
  );
  sql(
    'vietride_payment',
    'vietride_payment',
    ids.passengers
      .map(
        (id) => `
    INSERT INTO wallets (user_id,balance,currency,row_version) VALUES ('${id}',5000000,'VND',0)
    ON CONFLICT (user_id) DO UPDATE SET balance=5000000,row_version=0;`,
      )
      .join(''),
  );
}

async function privateKey() {
  const config = JSON.parse(
    fs.readFileSync(
      path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
      'utf8',
    ),
  );
  return {
    key: await importPKCS8(
      process.env.USER_JWT_PRIVATE_KEY || config.IdentityJwt.PrivateKey,
      'RS256',
    ),
    kid: process.env.USER_JWT_KID || config.IdentityJwt.Kid,
  };
}

async function token(userId, role, operatorId) {
  const material = await privateKey();
  return new SignJWT({
    role,
    ...(role === 'PASSENGER' ? { hasPhone: true } : {}),
    ...(operatorId ? { operatorId } : {}),
  })
    .setProtectedHeader({ alg: 'RS256', kid: material.kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(userId)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(material.key);
}

async function api(method, pathname, accessToken, body, idempotencyKey) {
  const maxAttempts = method === 'GET' || idempotencyKey ? 3 : 1;
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      const response = await fetch(`${gateway}${pathname}`, {
        method,
        headers: {
          ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
          ...(body ? { 'Content-Type': 'application/json' } : {}),
          ...(idempotencyKey ? { 'Idempotency-Key': day36IdempotencyKey(idempotencyKey) } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      const json = await response.json().catch(() => null);
      return { status: response.status, json };
    } catch (error) {
      if (attempt === maxAttempts) throw error;
      await new Promise((resolve) => setTimeout(resolve, attempt * 500));
    }
  }

  throw new Error('API retry loop exhausted');
}

async function createBookings() {
  const sizes = [5, 4, 3, 2, 1];
  const bookingIds = [];
  let seat = 1;
  for (let index = 0; index < sizes.length; index += 1) {
    const accessToken = await token(ids.passengers[index], 'PASSENGER');
    const seats = Array.from({ length: sizes[index] }, () => {
      const seatNumber = `D${String(seat++).padStart(2, '0')}`;
      return { seatNumber };
    });
    const response = await api(
      'POST',
      '/v1/bookings',
      accessToken,
      {
        tripId: ids.mainTrip,
        pickup: { stationId: ids.origin },
        dropoff: { stationId: ids.destination },
        shuttlePickup: {
          address: `${100 + index} Nguyen Hue, Quan 1`,
          latitude: 10.7 + index * 0.01,
          longitude: 106.65 + index * 0.01,
        },
        seats,
        paymentMethod: 'WALLET',
      },
      `day36-booking-${index + 1}`,
    );
    assert(
      response.status === 201 && response.json?.success === true,
      `Booking ${index + 1} failed: ${JSON.stringify(response)}`,
    );
    bookingIds.push(response.json.data.bookingId);
  }
  await poll(
    () =>
      Number(
        sql(
          'vietride_trip',
          'vietride_trip',
          `SELECT count(*) FROM shuttle_passengers WHERE main_trip_id='${ids.mainTrip}'`,
        ),
      ) === 15,
    'Trip fan-out did not reach 15 manifests',
  );
  const bookingIdSql = bookingIds.map((id) => `'${id}'`).join(',');
  assert(
    Number(
      sql(
        'vietride_booking',
        'vietride_booking',
        `SELECT count(*) FROM bookings WHERE id IN (${bookingIdSql}) AND status='CONFIRMED'`,
      ),
    ) === 5,
    'Booking DB does not contain five confirmed bookings',
  );
  assert(
    Number(
      sql(
        'vietride_booking',
        'vietride_booking',
        `SELECT count(*) FROM tickets WHERE booking_id IN (${bookingIdSql})`,
      ),
    ) === 15,
    'Booking DB does not contain 15 tickets',
  );
  assert(
    Number(
      sql(
        'vietride_booking',
        'vietride_booking',
        `SELECT count(*) FROM booking_shuttle_intents WHERE booking_id IN (${bookingIdSql}) AND is_active=true`,
      ),
    ) === 5,
    'Booking DB does not contain five active shuttle intents',
  );
  assert(
    Number(
      sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT count(*) FROM (SELECT booking_id,ticket_id FROM shuttle_passengers WHERE main_trip_id='${ids.mainTrip}' GROUP BY booking_id,ticket_id HAVING count(*)>1) duplicate`,
      ),
    ) === 0,
    'Fan-out created duplicate booking/ticket manifests',
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_booking',
          'vietride_booking',
          `SELECT count(*) FROM outbox_events WHERE event_type='booking.booking.confirmed' AND payload->>'bookingId' IN (${bookingIdSql}) AND status='PUBLISHED'`,
        ),
      ) === 5,
    'Booking confirmed Outbox events were not published',
  );
  const editLocked = await api(
    'POST',
    `/v1/bookings/${bookingIds[0]}/edit-pickup`,
    await token(ids.passengers[0], 'PASSENGER'),
    {
      pickup: { stationId: ids.origin },
      paymentMethod: 'WALLET',
    },
    'day36-edit-shuttle-pickup',
  );
  assert(
    editLocked.status === 409 && editLocked.json?.error?.code === 'SHUTTLE_PICKUP_LOCKED',
    `Active shuttle intent did not lock edit-pickup: ${JSON.stringify(editLocked)}`,
  );
  return bookingIds;
}

async function verifyBookingValidation() {
  const accessToken = await token(ids.passengers[0], 'PASSENGER');
  const base = {
    tripId: ids.mainTrip,
    pickup: { stationId: ids.unsupported },
    dropoff: { stationId: ids.destination },
    shuttlePickup: { address: 'Unsupported', latitude: 10.7, longitude: 106.7 },
    seats: [
      {
        seatNumber: 'D20',
      },
    ],
    paymentMethod: 'WALLET',
  };
  const unsupported = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    base,
    'day36-unsupported-station',
  );
  assert(
    unsupported.status === 422 && unsupported.json?.error?.code === 'SHUTTLE_STATION_NOT_SUPPORTED',
    `Unsupported Station validation failed: ${JSON.stringify(unsupported)}`,
  );
  const invalidCoordinates = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    {
      ...base,
      pickup: { stationId: ids.origin },
      shuttlePickup: { address: '', latitude: 91, longitude: 181 },
    },
    'day36-invalid-coordinates',
  );
  assert(
    invalidCoordinates.status === 422,
    `Coordinate validation failed: ${JSON.stringify(invalidCoordinates)}`,
  );

  const stopPickup = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    {
      ...base,
      pickup: { stopId: ids.stop },
      shuttlePickup: { address: 'Stop pickup', latitude: 10.7, longitude: 106.7 },
    },
    'day36-stop-shuttle',
  );
  assert(
    stopPickup.status === 422 && stopPickup.json?.error?.code === 'SHUTTLE_STATION_NOT_SUPPORTED',
    `Stop shuttle validation failed: ${JSON.stringify(stopPickup)}`,
  );

  sql(
    'vietride_trip',
    'vietride_trip',
    `UPDATE stations SET latitude=NULL,longitude=NULL WHERE id='${ids.origin}'`,
  );
  const missingCoordinates = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    {
      ...base,
      pickup: { stationId: ids.origin },
      shuttlePickup: { address: 'Missing station coordinates', latitude: 10.7, longitude: 106.7 },
    },
    'day36-missing-station-coordinates',
  );
  sql(
    'vietride_trip',
    'vietride_trip',
    `UPDATE stations SET latitude=10.7765000,longitude=106.7009000 WHERE id='${ids.origin}'`,
  );
  assert(
    missingCoordinates.status === 422 &&
      missingCoordinates.json?.error?.code === 'SHUTTLE_STATION_NOT_SUPPORTED',
    `Missing Station coordinates validation failed: ${JSON.stringify(missingCoordinates)}`,
  );

  sql(
    'vietride_trip',
    'vietride_trip',
    `UPDATE trips SET departure_date_time=now()+interval '20 minutes' WHERE id='${ids.mainTrip}'`,
  );
  const afterCutoff = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    {
      ...base,
      pickup: { stationId: ids.origin },
      shuttlePickup: { address: 'After cutoff', latitude: 10.7, longitude: 106.7 },
    },
    'day36-booking-after-cutoff',
  );
  sql(
    'vietride_trip',
    'vietride_trip',
    `UPDATE trips SET departure_date_time=now()+interval '6 hours',estimated_arrival_time=now()+interval '8 hours' WHERE id='${ids.mainTrip}'`,
  );
  assert(
    afterCutoff.status === 409 && afterCutoff.json?.error?.code === 'SHUTTLE_REQUEST_CUTOFF_PASSED',
    `Booking cutoff validation failed: ${JSON.stringify(afterCutoff)}`,
  );

  assert(
    Number(
      sql(
        'vietride_booking',
        'vietride_booking',
        "SELECT count(*) FROM booking_shuttle_intents WHERE pickup_address IN ('Unsupported','Stop pickup','Missing station coordinates','After cutoff')",
      ),
    ) === 0,
    'Negative validation left shuttle intent rows',
  );
}

async function createSingleBooking(tripId, passengerIndex, idempotencyKey) {
  const accessToken = await token(ids.passengers[passengerIndex], 'PASSENGER');
  const response = await api(
    'POST',
    '/v1/bookings',
    accessToken,
    {
      tripId,
      pickup: { stationId: ids.origin },
      dropoff: { stationId: ids.destination },
      shuttlePickup: {
        address: `Safety ${tripId.slice(-3)} Nguyen Hue`,
        latitude: 10.72,
        longitude: 106.67,
      },
      seats: [
        {
          seatNumber: 'D01',
        },
      ],
      paymentMethod: 'WALLET',
    },
    idempotencyKey,
  );
  assert(response.status === 201, `Safety booking failed: ${JSON.stringify(response)}`);
  await poll(
    () =>
      Number(
        sql(
          'vietride_trip',
          'vietride_trip',
          `SELECT count(*) FROM shuttle_passengers WHERE booking_id='${response.json.data.bookingId}'`,
        ),
      ) === 1,
    'Safety manifest fan-out timeout',
  );
  return response.json.data.bookingId;
}

async function verifySafetyAndRace() {
  const warning120Booking = await createSingleBooking(
    ids.warning120Trip,
    0,
    'day36-warning-120-booking',
  );
  const warning60Booking = await createSingleBooking(
    ids.warning60Trip,
    1,
    'day36-warning-60-booking',
  );
  const cutoffBooking = await createSingleBooking(ids.cutoffTrip, 2, 'day36-cutoff-booking');
  sql(
    'vietride_trip',
    'vietride_trip',
    `
    UPDATE trips SET departure_date_time=now()+interval '110 minutes' WHERE id='${ids.warning120Trip}';
    UPDATE trips SET departure_date_time=now()+interval '50 minutes' WHERE id='${ids.warning60Trip}';
    UPDATE trips SET departure_date_time=now()+interval '20 minutes' WHERE id='${ids.cutoffTrip}';
  `,
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_trip',
          'vietride_trip',
          `SELECT count(*) FROM shuttle_dispatch_alerts WHERE main_trip_id IN ('${ids.warning120Trip}','${ids.warning60Trip}','${ids.cutoffTrip}')`,
        ),
      ) === 3,
    'Warning/cutoff markers timeout',
    150_000,
  );
  assert(
    sql(
      'vietride_trip',
      'vietride_trip',
      `SELECT status FROM shuttle_passengers WHERE booking_id='${cutoffBooking}'`,
    ).trim() === 'CANCELLED',
    'Cutoff manifest was not cancelled',
  );
  assert(
    sql(
      'vietride_booking',
      'vietride_booking',
      `SELECT status FROM bookings WHERE id='${cutoffBooking}'`,
    ).trim() === 'CONFIRMED',
    'Cutoff changed main Booking',
  );
  assert(
    Number(
      sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT count(*) FROM outbox_events WHERE event_type='trip.shuttle.warning_issued' AND payload->>'mainTripId' IN ('${ids.warning120Trip}','${ids.warning60Trip}')`,
      ),
    ) === 2,
    'Warning events missing or duplicated',
  );
  assert(
    Number(
      sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT count(*) FROM outbox_events WHERE event_type='trip.shuttle.unfulfilled' AND payload->>'bookingId'='${cutoffBooking}'`,
      ),
    ) === 1,
    'Unfulfilled event missing or duplicated',
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_notification',
          'vietride_notification',
          `SELECT count(*) FROM notifications WHERE type='SHUTTLE_UNFULFILLED' AND data->>'bookingId'='${cutoffBooking}'`,
        ),
      ) === 1,
    'Unfulfilled notification timeout',
  );

  const raceBooking = await createSingleBooking(ids.raceTrip, 3, 'day36-race-booking');
  sql(
    'vietride_trip',
    'vietride_trip',
    `UPDATE trips SET departure_date_time=now()+interval '29 minutes 55 seconds' WHERE id='${ids.raceTrip}'`,
  );
  const waitToCron = Math.max(0, 60_000 - (Date.now() % 60_000) - 150);
  await new Promise((resolve) => setTimeout(resolve, waitToCron));
  const adminToken = await token(ids.admin, 'OPERATOR_ADMIN', ids.operatorA);
  const raceResponsePromise = api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    {
      mainTripId: ids.raceTrip,
      driverUserId: ids.driverA,
      vehicleId: ids.shuttle4,
      scheduledDepartureTime: new Date(Date.now() - 10 * 60_000).toISOString(),
      scheduledEndTime: new Date(Date.now() - 60_000).toISOString(),
      orderedBookingIds: [raceBooking],
    },
    'day36-race-dispatch',
  );
  const [raceResponse] = await Promise.all([
    raceResponsePromise,
    new Promise((resolve) => setTimeout(() => resolve(null), 3_000)),
  ]);
  await poll(
    () => {
      const state = sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT status FROM shuttle_passengers WHERE booking_id='${raceBooking}'`,
      ).trim();
      return state === 'PENDING' || state === 'CANCELLED' ? state : null;
    },
    'Race did not reach a terminal manifest state',
    90_000,
  );
  const statuses = sql(
    'vietride_trip',
    'vietride_trip',
    `SELECT string_agg(DISTINCT status, ',') FROM shuttle_passengers WHERE booking_id='${raceBooking}'`,
  ).trim();
  assert(
    statuses === 'PENDING' || statuses === 'CANCELLED',
    `Race produced mixed state: ${statuses}`,
  );
  const shuttleCount = Number(
    sql(
      'vietride_trip',
      'vietride_trip',
      `SELECT count(DISTINCT st.id) FROM shuttle_trips st JOIN shuttle_passengers sp ON sp.shuttle_trip_id=st.id WHERE sp.booking_id='${raceBooking}'`,
    ),
  );
  const unfulfilledCount = Number(
    sql(
      'vietride_trip',
      'vietride_trip',
      `SELECT count(*) FROM outbox_events WHERE event_type='trip.shuttle.unfulfilled' AND payload->>'bookingId'='${raceBooking}'`,
    ),
  );
  assert(
    (statuses === 'PENDING' && shuttleCount === 1 && unfulfilledCount === 0) ||
      (statuses === 'CANCELLED' && shuttleCount === 0 && unfulfilledCount === 1),
    `Race invariant failed: state=${statuses} shuttle=${shuttleCount} unfulfilled=${unfulfilledCount}`,
  );
  assert(
    (statuses === 'PENDING' && raceResponse.status === 201) ||
      (statuses === 'CANCELLED' &&
        raceResponse.status === 409 &&
        ['SHUTTLE_REQUEST_CUTOFF_PASSED', 'SHUTTLE_REQUEST_SET_CHANGED'].includes(
          raceResponse.json?.error?.code,
        )),
    `Race loser response is inconsistent: state=${statuses} response=${JSON.stringify(raceResponse)}`,
  );
  return { warning120Booking, warning60Booking };
}

async function dispatchAndTrack(bookingIds) {
  const adminToken = await token(ids.admin, 'OPERATOR_ADMIN', ids.operatorA);
  const pending = await api('GET', '/v1/operator/shuttle-requests', adminToken);
  assert(
    pending.status === 200 && pending.json?.data?.items?.[0]?.bookingGroups?.length === 5,
    `Pending query failed: ${JSON.stringify(pending)}`,
  );
  const pendingGroup = pending.json.data.items[0];
  assert(
    JSON.stringify(
      pendingGroup.bookingGroups.map((item) => item.passengerCount).sort((a, b) => a - b),
    ) === JSON.stringify([1, 2, 3, 4, 5]),
    'Pending Booking group sizes are incorrect',
  );
  assert(
    JSON.stringify(pendingGroup.suggestedBookingOrder) === JSON.stringify(bookingIds),
    'Suggested Booking order is not farthest-first',
  );
  assert(
    pendingGroup.pendingPassengerCount ===
      Number(
        sql(
          'vietride_trip',
          'vietride_trip',
          `SELECT count(*) FROM shuttle_passengers WHERE main_trip_id='${ids.mainTrip}' AND status='PENDING_ASSIGNMENT'`,
        ),
      ),
    'Pending count does not match Trip DB',
  );
  const otherAdminToken = await token(ids.otherDriver, 'OPERATOR_ADMIN', ids.operatorB);
  const otherPending = await api('GET', '/v1/operator/shuttle-requests', otherAdminToken);
  assert(
    otherPending.status === 200 && otherPending.json?.data?.totalItems === 0,
    'Cross-tenant pending data leaked',
  );
  const departure = new Date(Date.now() + 60 * 60_000).toISOString();
  const end = new Date(Date.now() + 4 * 60 * 60_000).toISOString();
  const conflictDeparture = new Date(Date.now() + 135 * 60_000).toISOString();
  const conflictEnd = new Date(Date.now() + 165 * 60_000).toISOString();
  const firstBody = {
    mainTripId: ids.mainTrip,
    driverUserId: ids.driverA,
    vehicleId: ids.shuttle12,
    scheduledDepartureTime: departure,
    scheduledEndTime: end,
    orderedBookingIds: bookingIds.slice(0, 3),
  };
  const overCapacity = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, orderedBookingIds: bookingIds },
    'day36-over-capacity',
  );
  assert(
    overCapacity.status === 409 && overCapacity.json?.error?.code === 'SHUTTLE_CAPACITY_EXCEEDED',
    `Capacity validation failed: ${JSON.stringify(overCapacity)}`,
  );
  const crossTenant = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, vehicleId: ids.otherVehicle },
    'day36-cross-tenant',
  );
  assert(
    crossTenant.status === 404,
    `Cross-tenant vehicle was accepted: ${JSON.stringify(crossTenant)}`,
  );
  const crossTenantDriver = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, driverUserId: ids.otherDriver },
    'day36-cross-tenant-driver',
  );
  assert(
    crossTenantDriver.status === 404 && crossTenantDriver.json?.error?.code === 'DRIVER_NOT_FOUND',
    `Cross-tenant driver was accepted: ${JSON.stringify(crossTenantDriver)}`,
  );
  const mainDriverConflict = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    {
      ...firstBody,
      driverUserId: ids.mainDriver,
      scheduledDepartureTime: conflictDeparture,
      scheduledEndTime: conflictEnd,
    },
    'day36-main-driver-conflict',
  );
  assert(
    mainDriverConflict.status === 409 &&
      mainDriverConflict.json?.error?.code === 'SHUTTLE_DRIVER_CONFLICT',
    `Main Trip driver overlap was accepted: ${JSON.stringify(mainDriverConflict)}`,
  );
  const mainVehicleConflict = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    {
      ...firstBody,
      vehicleId: ids.mainVehicle,
      scheduledDepartureTime: conflictDeparture,
      scheduledEndTime: conflictEnd,
    },
    'day36-main-vehicle-conflict',
  );
  assert(
    mainVehicleConflict.status === 409 &&
      mainVehicleConflict.json?.error?.code === 'SHUTTLE_VEHICLE_CONFLICT',
    `Main Trip vehicle overlap was accepted: ${JSON.stringify(mainVehicleConflict)}`,
  );
  const first = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    firstBody,
    'day36-dispatch-1',
  );
  assert(
    first.status === 201 && first.json?.data?.assignedPassengerCount === 12,
    `First dispatch failed: ${JSON.stringify(first)}`,
  );
  const replay = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    firstBody,
    'day36-dispatch-1',
  );
  assert(
    replay.status === 201 && replay.json?.data?.shuttleTripId === first.json.data.shuttleTripId,
    'Dispatch idempotency replay failed',
  );
  const stale = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, driverUserId: ids.driverB, vehicleId: ids.shuttle4 },
    'day36-stale-selection',
  );
  assert(
    stale.status === 409 && stale.json?.error?.code === 'SHUTTLE_REQUEST_SET_CHANGED',
    `Stale selection validation failed: ${JSON.stringify(stale)}`,
  );
  const shuttleDriverConflict = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, vehicleId: ids.shuttle4, orderedBookingIds: bookingIds.slice(3) },
    'day36-shuttle-driver-conflict',
  );
  assert(
    shuttleDriverConflict.status === 409 &&
      shuttleDriverConflict.json?.error?.code === 'SHUTTLE_DRIVER_CONFLICT',
    `ShuttleTrip driver overlap was accepted: ${JSON.stringify(shuttleDriverConflict)}`,
  );
  const shuttleVehicleConflict = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    { ...firstBody, driverUserId: ids.driverB, orderedBookingIds: bookingIds.slice(3) },
    'day36-shuttle-vehicle-conflict',
  );
  assert(
    shuttleVehicleConflict.status === 409 &&
      shuttleVehicleConflict.json?.error?.code === 'SHUTTLE_VEHICLE_CONFLICT',
    `ShuttleTrip vehicle overlap was accepted: ${JSON.stringify(shuttleVehicleConflict)}`,
  );
  const second = await api(
    'POST',
    '/v1/operator/shuttle-trips',
    adminToken,
    {
      ...firstBody,
      driverUserId: ids.driverB,
      vehicleId: ids.shuttle4,
      orderedBookingIds: bookingIds.slice(3),
    },
    'day36-dispatch-2',
  );
  assert(
    second.status === 201 && second.json?.data?.assignedPassengerCount === 3,
    `Second dispatch failed: ${JSON.stringify(second)}`,
  );

  await poll(
    () =>
      Number(
        sql(
          'vietride_notification',
          'vietride_notification',
          "SELECT count(*) FROM notifications WHERE type='SHUTTLE_ASSIGNED'",
        ),
      ) >= 5,
    'Assignment notifications missing',
  );
  assert(
    Number(
      sql(
        'vietride_notification',
        'vietride_notification',
        "SELECT count(*) FROM notifications WHERE type='SHUTTLE_ASSIGNED'",
      ),
    ) === 5,
    'Assignment notifications were duplicated per ticket',
  );
  assert(
    Number(
      sql(
        'vietride_notification',
        'vietride_notification',
        "SELECT count(*) FROM notifications WHERE type='SHUTTLE_ASSIGNED' AND data->'driver'->>'phone' IS NOT NULL AND data->'vehicle'->>'licensePlate' IS NOT NULL AND data->>'deepLink' IS NOT NULL",
      ),
    ) === 5,
    'Assignment notification snapshot/deep-link is incomplete',
  );
  await poll(
    () =>
      Number(
        sql(
          'vietride_notification',
          'vietride_notification',
          "SELECT count(*) FROM notification_deliveries d JOIN notifications n ON n.id=d.notification_id WHERE n.type='SHUTTLE_ASSIGNED' AND d.status='SENT'",
        ),
      ) === 5,
    'Test FCM provider did not persist five sent deliveries',
  );
  const passengerToken = await token(ids.passengers[0], 'PASSENGER');
  const driverToken = await token(ids.driverA, 'DRIVER', ids.operatorA);
  const passengerSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: passengerToken },
    transports: ['websocket'],
  });
  const driverSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: driverToken },
    transports: ['websocket'],
  });
  await Promise.all([onceConnected(passengerSocket), onceConnected(driverSocket)]);
  const shuttleTripId = first.json.data.shuttleTripId;
  const join = await emitAck(passengerSocket, 'joinShuttleTracking', { shuttleTripId });
  assert(join.success, `Passenger join failed: ${JSON.stringify(join)}`);
  const unrelatedPassengerSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: await token(ids.passengers[4], 'PASSENGER') },
    transports: ['websocket'],
  });
  await onceConnected(unrelatedPassengerSocket);
  const unrelatedPassengerJoin = await emitAck(unrelatedPassengerSocket, 'joinShuttleTracking', {
    shuttleTripId,
  });
  unrelatedPassengerSocket.close();
  assert(
    unrelatedPassengerJoin.success === false && unrelatedPassengerJoin.error === 'ACCESS_DENIED',
    `Unrelated passenger joined ShuttleTrip: ${JSON.stringify(unrelatedPassengerJoin)}`,
  );
  const otherOperatorSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: await token(ids.otherDriver, 'OPERATOR_ADMIN', ids.operatorB) },
    transports: ['websocket'],
  });
  await onceConnected(otherOperatorSocket);
  const otherOperatorJoin = await emitAck(otherOperatorSocket, 'joinShuttleTracking', {
    shuttleTripId,
  });
  otherOperatorSocket.close();
  assert(
    otherOperatorJoin.success === false && otherOperatorJoin.error === 'ACCESS_DENIED',
    `Cross-tenant operator joined ShuttleTrip: ${JSON.stringify(otherOperatorJoin)}`,
  );
  const gpsReceived = onceEvent(passengerSocket, 'shuttle:gps:update');
  const etaReceived = onceEvent(passengerSocket, 'shuttle:eta:update');
  const gpsAck = await emitAck(driverSocket, 'shuttle:gps:update', {
    shuttleTripId,
    latitude: 10.7,
    longitude: 106.65,
    speedKmh: 30,
    recordedAt: new Date().toISOString(),
  });
  assert(gpsAck.success, `Driver GPS failed: ${JSON.stringify(gpsAck)}`);
  await Promise.all([gpsReceived, etaReceived]);
  passengerSocket.close();
  driverSocket.close();
  assert(
    redisCommand('EXISTS', `tracking:shuttle:latest:${shuttleTripId}`).trim() === '1',
    'Shuttle latest Redis key missing',
  );
  assert(
    Number(redisCommand('TTL', `tracking:shuttle:latest:${shuttleTripId}`)) > 0,
    'Shuttle latest Redis TTL missing',
  );
  assert(
    redisCommand('EXISTS', `tracking:shuttle:gps_buffer:${shuttleTripId}`).trim() === '1',
    'Shuttle GPS buffer missing',
  );
  assert(
    Number(redisCommand('LLEN', `tracking:shuttle:gps_buffer:${shuttleTripId}`)) <= 1000,
    'Shuttle GPS buffer exceeded max length',
  );
  assert(
    Number(redisCommand('TTL', `tracking:shuttle:gps_buffer:${shuttleTripId}`)) > 0,
    'Shuttle GPS buffer TTL missing',
  );
  assert(
    redisCommand('EXISTS', `tracking:shuttle:eta_state:${shuttleTripId}`).trim() === '1',
    'Shuttle ETA state missing',
  );
  const etaKeys = redisCommand('KEYS', `tracking:shuttle:eta:${shuttleTripId}:*`)
    .split(/\r?\n/)
    .filter(Boolean);
  assert(
    etaKeys.length === 1 && Number(redisCommand('TTL', etaKeys[0])) > 0,
    'Shuttle ETA key/TTL missing',
  );
  assert(
    redisCommand('SISMEMBER', 'tracking:active_trips', shuttleTripId).trim() === '0',
    'Shuttle leaked into active main trips',
  );
  assert(
    Number(
      sql(
        'vietride_tracking',
        'vietride_tracking',
        `SELECT count(*) FROM gps_trails WHERE trip_id='${shuttleTripId}'`,
      ),
    ) === 0,
    'Shuttle leaked into GpsTrail',
  );

  const cancellation = await api(
    'POST',
    `/v1/bookings/${bookingIds[0]}/cancel`,
    passengerToken,
    {
      reason: 'USER_INITIATED',
    },
    'day36-cancel-assigned',
  );
  assert(
    cancellation.status === 200,
    `Assigned booking cancellation failed: ${JSON.stringify(cancellation)}`,
  );
  await poll(
    () =>
      sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT string_agg(DISTINCT status, ',') FROM shuttle_passengers WHERE booking_id='${bookingIds[0]}'`,
      ).trim() === 'CANCELLED',
    'Cancelled Booking manifests did not cancel',
  );
  assert(
    Number(
      sql(
        'vietride_trip',
        'vietride_trip',
        `SELECT count(*) FROM shuttle_passengers WHERE shuttle_trip_id='${shuttleTripId}'`,
      ),
    ) === 12,
    'Cancellation unexpectedly backfilled or reshuffled ShuttleTrip capacity',
  );
  const nextPassengerSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: await token(ids.passengers[1], 'PASSENGER') },
    transports: ['websocket'],
  });
  const resumedDriverSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: driverToken },
    transports: ['websocket'],
  });
  await Promise.all([onceConnected(nextPassengerSocket), onceConnected(resumedDriverSocket)]);
  const nextJoin = await emitAck(nextPassengerSocket, 'joinShuttleTracking', { shuttleTripId });
  assert(nextJoin.success, `Remaining passenger could not rejoin: ${JSON.stringify(nextJoin)}`);
  const nextEtaPromise = onceEvent(nextPassengerSocket, 'shuttle:eta:update');
  const resumedGpsAck = await emitAck(resumedDriverSocket, 'shuttle:gps:update', {
    shuttleTripId,
    latitude: 10.72,
    longitude: 106.67,
    speedKmh: 30,
    recordedAt: new Date().toISOString(),
  });
  assert(resumedGpsAck.success, `GPS after cancellation failed: ${JSON.stringify(resumedGpsAck)}`);
  const nextEta = await nextEtaPromise;
  nextPassengerSocket.close();
  resumedDriverSocket.close();
  assert(
    nextEta.nextPickupOrder === 2,
    `Tracking did not skip cancelled pickup order: ${JSON.stringify(nextEta)}`,
  );
  const deniedSocket = io(tracking, {
    path: '/tracking/socket.io',
    auth: { token: passengerToken },
    transports: ['websocket'],
  });
  await onceConnected(deniedSocket);
  const denied = await emitAck(deniedSocket, 'joinShuttleTracking', { shuttleTripId });
  deniedSocket.close();
  assert(
    denied.success === false && denied.error === 'ACCESS_DENIED',
    `Cancelled passenger still authorized: ${JSON.stringify(denied)}`,
  );
}

function onceConnected(socket) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Socket connect timeout')), 10_000);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve();
    });
    socket.once('connect_error', reject);
  });
}

function emitAck(socket, event, payload) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} ack timeout`)), 10_000);
    socket.emit(event, payload, (ack) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

function onceEvent(socket, event) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event} timeout`)), 3_000);
    socket.once(event, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

async function main() {
  if (!useDev) {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
    for (const service of [
      'identity',
      'trip',
      'booking',
      'payment',
      'parcel',
      'gateway',
      'tracking',
      'notification',
    ]) {
      run('docker', [...compose, 'build', service], { env: { COMPOSE_PARALLEL_LIMIT: '1' } });
    }
    run('docker', [...compose, 'up', '-d', '--no-build', 'gateway', 'tracking', 'notification']);
  }
  await Promise.all([
    waitFor(`${gateway}/health`),
    waitFor(`${gateway}/ready`),
    waitFor(`${tracking}/health`),
    waitFor(`${tracking}/ready`),
    waitFor(useDev ? 'http://localhost:3002/ready' : 'http://localhost:56012/ready'),
  ]);
  seed();
  record('seed');
  await verifyBookingValidation();
  const bookingIds = await createBookings();
  record('gateway REST');
  record('booking/event fan-out');
  await dispatchAndTrack(bookingIds);
  record('subset dispatch');
  record('notification');
  record('socket/eta');
  await verifySafetyAndRace();
  record('warning/cutoff');
  record('race invariant');
  record('database assertions');
}

try {
  await main();
} catch (error) {
  results.push({
    name: 'harness',
    passed: false,
    detail: error instanceof Error ? error.message : String(error),
  });
  console.error(error);
} finally {
  if (!useDev) {
    try {
      run('docker', [...compose, 'down', '-v', '--remove-orphans']);
      record('cleanup');
    } catch (error) {
      results.push({ name: 'cleanup', passed: false, detail: String(error) });
    }
  }
}

console.log(JSON.stringify({ suite: 'day36-shuttle-e2e', results }, null, 2));
process.exitCode = results.every((result) => result.passed) ? 0 : 1;
