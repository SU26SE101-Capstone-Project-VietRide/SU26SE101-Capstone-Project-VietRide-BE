// Reproducible Day-19 operator-booking Gateway E2E harness.  The fixtures are
// intentionally local-only and are always removed, including after a failed run.
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const baseUrl = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const ids = {
  operatorAdmin: '19191919-1919-4191-8191-191919191901',
  operatorStaff: '19191919-1919-4191-8191-191919191902',
  adminUser: '19191919-1919-4191-8191-191919191911',
  staffUser: '19191919-1919-4191-8191-191919191912',
  adminBooking: '19191919-1919-4191-8191-191919191921',
  staffBooking: '19191919-1919-4191-8191-191919191922',
  adminPassenger: '19191919-1919-4191-8191-191919191931',
  adminTicket: '19191919-1919-4191-8191-191919191941',
  historyPending: '19191919-1919-4191-8191-191919191951',
  historyConfirmed: '19191919-1919-4191-8191-191919191952',
  historyCancelled: '19191919-1919-4191-8191-191919191953',
};
const adminCode = 'VR-20260711-DAY19A01';
const staffCode = 'VR-20260711-DAY19A02';

function psql(database, sql) {
  return execFileSync(
    'docker',
    [
      'exec',
      'vietride_postgres',
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      'vietride',
      '-d',
      database,
      '-Atc',
      sql,
    ],
    { encoding: 'utf8' },
  ).trim();
}

function cleanup() {
  // Reverse FK order: history/tickets/passengers -> bookings, then identity users -> operators.
  psql(
    'vietride_booking',
    `
    DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}');
    DELETE FROM vietride_booking.tickets WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}');
    DELETE FROM vietride_booking.passengers WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}');
    DELETE FROM vietride_booking.bookings WHERE id IN ('${ids.adminBooking}', '${ids.staffBooking}')
       OR booking_code IN ('${adminCode}', '${staffCode}');
  `,
  );
  psql(
    'vietride_identity',
    `
    DELETE FROM vietride_identity.users WHERE id IN ('${ids.adminUser}', '${ids.staffUser}');
    DELETE FROM vietride_identity.operators WHERE id IN ('${ids.operatorAdmin}', '${ids.operatorStaff}');
  `,
  );
}

function assertClean() {
  const bookingRows = psql(
    'vietride_booking',
    `SELECT count(*) FROM vietride_booking.booking_status_history WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}')
     UNION ALL SELECT count(*) FROM vietride_booking.tickets WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}')
     UNION ALL SELECT count(*) FROM vietride_booking.passengers WHERE booking_id IN ('${ids.adminBooking}', '${ids.staffBooking}')
     UNION ALL SELECT count(*) FROM vietride_booking.bookings WHERE id IN ('${ids.adminBooking}', '${ids.staffBooking}') OR booking_code IN ('${adminCode}', '${staffCode}');`,
  );
  const identityRows = psql(
    'vietride_identity',
    `SELECT count(*) FROM vietride_identity.users WHERE id IN ('${ids.adminUser}', '${ids.staffUser}') UNION ALL SELECT count(*) FROM vietride_identity.operators WHERE id IN ('${ids.operatorAdmin}', '${ids.operatorStaff}');`,
  );
  if (
    bookingRows.split(/\s+/).some((value) => value !== '0') ||
    identityRows.split(/\s+/).some((value) => value !== '0')
  ) {
    throw new Error(
      `Day-19 fixture cleanup failed: booking=${bookingRows}, identity=${identityRows}`,
    );
  }
}

function seed() {
  cleanup();
  psql(
    'vietride_identity',
    `
    INSERT INTO vietride_identity.operators (id, name, business_registration_number, tax_code, contact_email, contact_phone, registration_status, approved_at)
    VALUES
      ('${ids.operatorAdmin}', 'Day 19 Admin Operator', 'DAY19-ADMIN-REG', 'DAY19-ADMIN-TAX', 'day19-admin-operator@local.test', '+84919191901', 'APPROVED', '2026-07-11T00:00:00Z'),
      ('${ids.operatorStaff}', 'Day 19 Staff Operator', 'DAY19-STAFF-REG', 'DAY19-STAFF-TAX', 'day19-staff-operator@local.test', '+84919191902', 'APPROVED', '2026-07-11T00:00:00Z');
    INSERT INTO vietride_identity.users (id, email, phone, display_name, role, status, operator_id)
    VALUES
      ('${ids.adminUser}', 'day19-admin@local.test', '+84919191911', 'Day 19 Admin Passenger', 'OPERATOR_ADMIN', 'ACTIVE', '${ids.operatorAdmin}'),
      ('${ids.staffUser}', 'day19-staff@local.test', '+84919191912', 'Day 19 Staff Passenger', 'OPERATOR_STAFF', 'ACTIVE', '${ids.operatorStaff}');
  `,
  );
  psql(
    'vietride_booking',
    `
    INSERT INTO vietride_booking.bookings (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id, base_fare, discount_amount, total_amount, status, trip_snapshot_origin_name, trip_snapshot_dest_name, trip_snapshot_departure, trip_snapshot_route_name, confirmed_at, created_at, updated_at)
    VALUES
      ('${ids.adminBooking}', '${adminCode}', '${ids.adminUser}', '19191919-1919-4191-8191-191919191961', '${ids.operatorAdmin}', '19191919-1919-4191-8191-191919191971', 600000, 100000, 500000, 'CANCELLED', 'Sai Gon', 'Da Lat', '2026-07-12T01:00:00Z', 'Day 19 Sai Gon - Da Lat', '2026-07-11T00:05:00Z', '2026-07-11T00:00:00Z', '2026-07-11T00:10:00Z'),
      ('${ids.staffBooking}', '${staffCode}', '${ids.staffUser}', '19191919-1919-4191-8191-191919191962', '${ids.operatorStaff}', '19191919-1919-4191-8191-191919191972', 200000, 0, 200000, 'CONFIRMED', 'Can Tho', 'Da Nang', '2026-07-13T01:00:00Z', 'Day 19 Can Tho - Da Nang', '2026-07-11T00:00:00Z', '2026-07-11T00:00:00Z', '2026-07-11T00:00:00Z');
    INSERT INTO vietride_booking.passengers (id, booking_id, seat_number, boarding_status)
    VALUES ('${ids.adminPassenger}', '${ids.adminBooking}', 'A01', 'PENDING');
    INSERT INTO vietride_booking.tickets (id, booking_id, passenger_id, ticket_code, seat_number, status, fare_amount, discount_amount, paid_amount, issued_at)
    VALUES ('${ids.adminTicket}', '${ids.adminBooking}', '${ids.adminPassenger}', 'VT-20260711-DAY19001', 'A01', 'CANCELLED', 600000, 100000, 500000, '2026-07-11T00:05:00Z');
    INSERT INTO vietride_booking.booking_status_history (id, booking_id, status, occurred_at, reason_code, actor_user_id, source)
    VALUES
      ('${ids.historyPending}', '${ids.adminBooking}', 'PENDING_PAYMENT', '2026-07-11T00:00:00Z', NULL, '${ids.adminUser}', 'CREATE_BOOKING'),
      ('${ids.historyConfirmed}', '${ids.adminBooking}', 'CONFIRMED', '2026-07-11T00:05:00Z', NULL, NULL, 'CONFIRM_ON_PAYMENT'),
      ('${ids.historyCancelled}', '${ids.adminBooking}', 'CANCELLED', '2026-07-11T00:05:00Z', 'USER_INITIATED', '${ids.adminUser}', 'CANCEL_BOOKING');
  `,
  );
}

const settings = JSON.parse(
  fs.readFileSync(
    path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
    'utf8',
  ),
);
const privateKey = await importPKCS8(
  process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
  'RS256',
);
const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
async function token(subject, role, operatorId) {
  return new SignJWT({
    role,
    operatorId,
    email: `${role.toLowerCase()}@day19.local`,
    hasPhone: 'true',
  })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('5m')
    .sign(privateKey);
}

let runError;
try {
  seed();
  const [adminToken, staffToken, deniedToken] = await Promise.all([
    token(ids.adminUser, 'OPERATOR_ADMIN', ids.operatorAdmin),
    token(ids.staffUser, 'OPERATOR_STAFF', ids.operatorStaff),
    token(ids.adminUser, 'PASSENGER', ids.operatorAdmin),
  ]);
  if (process.env.DAY19_FORCE_NEW_MAN_FAILURE === 'true')
    throw new Error('Forced Day-19 Newman failure requested');
  const run = spawnSync(
    'npx',
    [
      '--yes',
      'newman',
      'run',
      'docs/api/postman/vietride.postman_collection.json',
      '-e',
      'docs/api/postman/vietride.local.postman_environment.json',
      '--folder',
      process.platform === 'win32'
        ? '"Booking - Day 19 operator booking reads"'
        : 'Booking - Day 19 operator booking reads',
      '--env-var',
      `baseUrl=${baseUrl}`,
      '--env-var',
      `day19OperatorAdminToken=${adminToken}`,
      '--env-var',
      `day19OperatorStaffToken=${staffToken}`,
      '--env-var',
      `day19DeniedToken=${deniedToken}`,
      '--env-var',
      `day19AdminBookingId=${ids.adminBooking}`,
      '--env-var',
      `day19StaffBookingId=${ids.staffBooking}`,
      '--env-var',
      `day19BookingCode=${adminCode}`,
      '--env-var',
      'day19PassengerPhone=0919191911',
      '--reporters',
      'cli',
    ],
    { cwd: root, shell: process.platform === 'win32', stdio: 'inherit' },
  );
  if (run.error) throw run.error;
  if (run.status !== 0) throw new Error(`Newman failed with status ${run.status ?? 1}`);
} catch (error) {
  runError = error;
} finally {
  cleanup();
  assertClean();
  console.log('PASS | Day-19 fixture cleanup | no temporary Identity or Booking rows remain');
}
if (runError) throw runError;
