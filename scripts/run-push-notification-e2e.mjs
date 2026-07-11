// Local-only end-to-end proof for push notifications without a mobile client.
// It uses real Gateway, Identity, Trip, Postgres, RabbitMQ, Redis and Firebase
// Admin dry-run. Firebase dry-run validates credentials and payload acceptance,
// but cannot prove that an Android/iOS device displayed a notification.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const gatewayBaseUrl = process.env.GATEWAY_BASE_URL || 'http://gateway:3000';
const ids = {
  schedule: '91a10000-0000-4000-8000-000000000001',
  scheduledTrip: '91a10000-0000-4000-8000-000000000002',
  boardingTrip: '91a10000-0000-4000-8000-000000000003',
  oldDriver: '91a10000-0000-4000-8000-000000000011',
  oldAssistant: '91a10000-0000-4000-8000-000000000012',
  newDriver: '91a10000-0000-4000-8000-000000000013',
  newAssistant: '91a10000-0000-4000-8000-000000000014',
  operatorAdmin: '91a10000-0000-4000-8000-000000000015',
};
const runId = Date.now().toString(36);

function docker(args, options = {}) {
  return execFileSync('docker', args, { cwd: root, encoding: 'utf8', ...options }).trim();
}

function psql(database, sql) {
  return docker([
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, '-Atc', sql,
  ]);
}

function requireSafeLocalTarget() {
  const isLocalHostTarget = /^https?:\/\/(localhost|127\.0\.0\.1)(?::\d+)?$/i.test(gatewayBaseUrl);
  const isDockerGatewayTarget = gatewayBaseUrl === 'http://gateway:3000';
  if (process.env.NODE_ENV === 'production' || (!isLocalHostTarget && !isDockerGatewayTarget)) {
    throw new Error('This E2E harness only permits a local non-production Gateway target.');
  }
  const notificationEnv = docker(['exec', 'vietride_notification', 'sh', '-c', 'printf "$NODE_ENV|$FCM_DRY_RUN|$FCM_PROJECT_ID"']);
  const [nodeEnv, dryRun, projectId] = notificationEnv.split('|');
  if (nodeEnv === 'production' || dryRun !== 'true' || !projectId) {
    throw new Error('Notification must run locally with real Firebase credentials and FCM_DRY_RUN=true.');
  }
}

function cleanup() {
  const userIds = Object.values(ids).slice(3).map((id) => `'${id}'`).join(', ');
  const tripIds = `'${ids.scheduledTrip}', '${ids.boardingTrip}'`;
  psql('vietride_notification', `DELETE FROM vietride_notification.notifications WHERE user_id IN (${userIds});`);
  psql('vietride_trip', `DELETE FROM vietride_trip.trips WHERE id IN (${tripIds}); DELETE FROM vietride_trip.driver_schedules WHERE id = '${ids.schedule}';`);
  psql('vietride_identity', `DELETE FROM vietride_identity.user_devices WHERE user_id IN (${userIds}); DELETE FROM vietride_identity.users WHERE id IN (${userIds});`);
}

function seed(operatorId, routeId, vehicleId) {
  cleanup();
  const users = [
    [ids.oldDriver, 'DRIVER'], [ids.oldAssistant, 'ASSISTANT'], [ids.newDriver, 'DRIVER'],
    [ids.newAssistant, 'ASSISTANT'], [ids.operatorAdmin, 'OPERATOR_ADMIN'],
  ];
  const values = users.map(([id, role]) =>
    `('${id}', '${id}@push-e2e.local', 'Push E2E ${role}', '${role}'::public.user_role, 'ACTIVE'::public.user_status, '${operatorId}', 0)`,
  ).join(',');
  psql('vietride_identity', `INSERT INTO vietride_identity.users (id,email,display_name,role,status,operator_id,failed_login_attempts) VALUES ${values};`);
  psql('vietride_trip', `
    INSERT INTO vietride_trip.driver_schedules
      (id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,day_of_week,departure_time,valid_from,is_active)
    VALUES ('${ids.schedule}','${operatorId}','${routeId}','${vehicleId}','${ids.oldDriver}','${ids.oldAssistant}','[1]'::jsonb,'08:00',CURRENT_DATE,true);
    INSERT INTO vietride_trip.trips
      (id,operator_id,route_id,vehicle_id,driver_user_id,assistant_user_id,driver_schedule_id,departure_date_time,estimated_arrival_time,status,source,base_fare)
    VALUES
      ('${ids.scheduledTrip}','${operatorId}','${routeId}','${vehicleId}','${ids.oldDriver}','${ids.oldAssistant}','${ids.schedule}',now()+interval '2 days',now()+interval '2 days 4 hours','SCHEDULED','MANUAL',200000),
      ('${ids.boardingTrip}','${operatorId}','${routeId}','${vehicleId}','${ids.oldDriver}','${ids.oldAssistant}','${ids.schedule}',now()+interval '3 days',now()+interval '3 days 4 hours','BOARDING','MANUAL',200000);`);
}

function findApprovedOperatorTripFixture() {
  const operatorIds = psql(
    'vietride_identity',
    "SELECT id FROM vietride_identity.operators WHERE registration_status='APPROVED' AND is_active=true AND deleted_at IS NULL ORDER BY created_at",
  ).split(/\r?\n/).filter(Boolean);

  for (const operatorId of operatorIds) {
    const existingTrip = psql(
      'vietride_trip',
      `SELECT route_id || '|' || vehicle_id FROM vietride_trip.trips WHERE operator_id='${operatorId}' LIMIT 1`,
    );
    if (existingTrip) return [operatorId, ...existingTrip.split('|')];

    const routeVehicle = psql(
      'vietride_trip',
      `SELECT r.id || '|' || v.id FROM vietride_trip.routes r JOIN vietride_trip.vehicles v ON v.operator_id=r.operator_id WHERE r.operator_id='${operatorId}' AND r.is_active=true AND r.deleted_at IS NULL AND v.is_active=true AND v.deleted_at IS NULL LIMIT 1`,
    );
    if (routeVehicle) return [operatorId, ...routeVehicle.split('|')];
  }

  throw new Error('Push E2E needs one APPROVED active Identity operator with an active Trip route and vehicle.');
}

async function makeToken(userId, role, operatorId) {
  const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
  const privateKey = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  return new SignJWT({ role, operatorId, email: `${userId}@push-e2e.local`, hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(userId)
    .setIssuedAt().setExpirationTime('15m').sign(privateKey);
}

async function api(pathname, token, method, body, headers = {}) {
  const request = Buffer.from(JSON.stringify({
    url: `${gatewayBaseUrl}${pathname}`,
    method,
    headers: { authorization: `Bearer ${token}`, ...(body ? { 'content-type': 'application/json' } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  })).toString('base64url');
  const runner = [
    "const input=JSON.parse(Buffer.from(process.argv[1],'base64url').toString());",
    "fetch(input.url,{method:input.method,headers:input.headers,body:input.body}).then(async response=>",
    "console.log(JSON.stringify({status:response.status,text:await response.text()}))).catch(error=>{console.error(error);process.exit(1)});",
  ].join('');
  const raw = docker(['exec', 'vietride_gateway', 'node', '-e', runner, request]);
  const response = JSON.parse(raw);
  if (response.status < 200 || response.status >= 300) throw new Error(`${method} ${pathname} failed (${response.status}): ${response.text}`);
  const text = response.text;
  return text ? JSON.parse(text) : null;
}

async function waitFor(predicate, description, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`Timed out waiting for ${description}`);
}

requireSafeLocalTarget();
const [operatorId, routeId, vehicleId] = findApprovedOperatorTripFixture();
console.log(`Using approved operator fixture ${operatorId}.`);

try {
  seed(operatorId, routeId, vehicleId);
  console.log('PASS | seeded approved operator, crew, schedule and two future trips');
  const adminToken = await makeToken(ids.operatorAdmin, 'OPERATOR_ADMIN', operatorId);
  for (const [userId, role] of [[ids.oldDriver, 'DRIVER'], [ids.oldAssistant, 'ASSISTANT'], [ids.newDriver, 'DRIVER'], [ids.newAssistant, 'ASSISTANT']]) {
    const token = await makeToken(userId, role, operatorId);
    await api('/v1/auth/device-token', token, 'POST', { fcmToken: `push-e2e-${userId}`, platform: 'ANDROID' });
  }
  console.log('PASS | registered four active Android device-token fixtures through Gateway → Identity');

  await api(`/v1/operator/driver-schedules/${ids.schedule}/crew`, adminToken, 'PATCH', {
    driverUserId: ids.newDriver,
    assistantUserId: ids.newAssistant,
  }, { 'Idempotency-Key': `push-e2e-crew-change-${runId}` });
  console.log('PASS | changed crew through Gateway → Trip and wrote transactional Outbox events');

  await waitFor(() => psql('vietride_notification', `SELECT count(*) FROM vietride_notification.notification_deliveries d JOIN vietride_notification.notifications n ON n.id=d.notification_id WHERE n.user_id IN ('${ids.oldDriver}','${ids.oldAssistant}','${ids.newDriver}','${ids.newAssistant}') AND d.status='VALIDATED'`) === '8', 'two crew-change events validated by Firebase dry-run');
  console.log('PASS | RabbitMQ consumer created 8 delivery records validated by Firebase dry-run');
  const crewSnapshots = psql('vietride_trip', `SELECT count(*) FROM vietride_trip.trips WHERE id IN ('${ids.scheduledTrip}','${ids.boardingTrip}') AND driver_user_id='${ids.newDriver}' AND assistant_user_id='${ids.newAssistant}'`);
  if (crewSnapshots !== '2') throw new Error(`Crew snapshots not propagated correctly: ${crewSnapshots}/2`);
  console.log('PASS | updated crew snapshot for both SCHEDULED and BOARDING trips');

  const announcement = await api('/v1/operator/notifications', adminToken, 'POST', {
    scope: 'OPERATOR', title: 'Push E2E announcement', body: 'Firebase dry-run validation.',
  }, { 'Idempotency-Key': `push-e2e-announcement-${runId}` });
  const announcementAgain = await api('/v1/operator/notifications', adminToken, 'POST', {
    scope: 'OPERATOR', title: 'Push E2E announcement', body: 'Firebase dry-run validation.',
  }, { 'Idempotency-Key': `push-e2e-announcement-${runId}` });
  const first = announcement.data ?? announcement;
  const second = announcementAgain.data ?? announcementAgain;
  if (first.announcementId !== second.announcementId) throw new Error('Announcement Idempotency-Key did not replay the original response.');
  console.log('PASS | announcement endpoint replayed the same Idempotency-Key response');
  console.log('PASS | real DB + Gateway + Identity + Trip Outbox + RabbitMQ + Notification + Firebase dry-run');
} finally {
  cleanup();
  console.log('PASS | fixture cleanup');
}
