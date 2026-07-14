import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const bookingId = '17171717-1717-4171-8171-171717171721';
const passengerId = '17171717-1717-4171-8171-171717171701';
const operatorId = '11111111-1111-4111-8111-111111111111';
const tripId = '00000000-0000-4000-8000-000000000013';

function ensureStubBookingStack() {
  const env = {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: 'true',
    BOOKING_PAYMENT_USE_DEV_STUB: 'true',
    BOOKING_IDENTITY_USE_DEV_STUB: 'true',
  };
  execFileSync('docker', [
    'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml',
    '--profile', 'app', 'up', '-d', '--force-recreate', 'booking', 'gateway',
  ], { cwd: root, env, stdio: 'inherit' });
}

function psql(sql) {
  return execFileSync('docker', [
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
    '-d', 'vietride_booking', '-Atc', sql,
  ], { encoding: 'utf8' }).trim();
}
function cleanup() {
  psql(`DELETE FROM vietride_booking.tickets WHERE booking_id='${bookingId}';
DELETE FROM vietride_booking.passengers WHERE booking_id='${bookingId}';
DELETE FROM vietride_booking.booking_status_history WHERE booking_id='${bookingId}';
DELETE FROM vietride_booking.bookings WHERE id='${bookingId}' OR booking_code='VR-20260701-DAYE1722';`);
}
function assertClean() {
  const rows = psql(`SELECT count(*) FROM vietride_booking.bookings WHERE id='${bookingId}' OR booking_code='VR-20260701-DAYE1722';`);
  if (rows !== '0') throw new Error(`Day-17 fixture cleanup failed: bookings=${rows}`);
}
let runError;
try {
  ensureStubBookingStack();
  cleanup();
  psql(`
    INSERT INTO vietride_booking.bookings
      (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
       base_fare, discount_amount, total_amount, status, confirmed_at)
    VALUES ('${bookingId}', 'VR-20260701-DAYE1722', '${passengerId}', '${tripId}', '${operatorId}',
      '44444444-4444-4444-8444-444444444444', 200000, 0, 200000, 'CONFIRMED', now());
    INSERT INTO vietride_booking.passengers (booking_id, seat_number, boarding_status)
    VALUES ('${bookingId}', 'C17', 'PENDING');`);

  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  async function token(sub, role, operator) {
    const claims = { role, email: `${role.toLowerCase()}@day17.local`, hasPhone: 'true' };
    if (operator) claims.operatorId = operator;
    return new SignJWT(claims).setProtectedHeader({ alg: 'RS256', kid })
      .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(sub)
      .setIssuedAt().setExpirationTime('15m').sign(key);
  }
  const passengerToken = await token(passengerId, 'PASSENGER');
  const operatorToken = await token('17171717-1717-4171-8171-171717171702', 'OPERATOR_ADMIN', operatorId);
  const args = [
    '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
    '-e', 'docs/api/postman/vietride.local.postman_environment.json',
    '--folder', process.platform === 'win32' ? '"Booking - Day 17 cancellation + booking stats carry-over"' : 'Booking - Day 17 cancellation + booking stats carry-over',
    '--env-var', `passengerAccessToken=${passengerToken}`,
    '--env-var', `operatorAdminAccessToken=${operatorToken}`,
    '--env-var', `day17BookingId=${bookingId}`,
  ];
  if (process.env.DAY17_FORCE_NEWMAN_FAILURE === 'true')
    throw new Error('Forced Day-17 Newman failure requested');
  const result = spawnSync('npx', args, { cwd: root, shell: process.platform === 'win32', stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Newman failed with status ${result.status ?? 1}`);
} catch (error) {
  runError = error;
} finally {
  cleanup();
  assertClean();
  console.log('PASS | D17 fixture cleanup | temporary booking removed');
}
if (runError) throw runError;
