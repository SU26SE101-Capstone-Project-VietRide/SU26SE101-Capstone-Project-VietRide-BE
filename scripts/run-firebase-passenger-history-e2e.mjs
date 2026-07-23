import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { initializeApp, cert, deleteApp } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { getStorage } from 'firebase-admin/storage';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const gateway = 'http://localhost:3000';
const env = readEnv(path.join(root, '.env'));
const tag = `fe-e2e-${Date.now().toString(36)}`;
const operatorId = '10000000-0000-0000-0000-000000000009';
const systemAdminId = '31000000-0000-4000-8000-000000000106';
const tripId = '40000000-0000-4000-8000-000000000502';
const operatorAdminId = crypto.randomUUID();
const passengerAId = crypto.randomUUID();
const passengerBId = crypto.randomUUID();
const driverId = crypto.randomUUID();
const bookingAId = crypto.randomUUID();
const bookingBId = crypto.randomUUID();
const passengerRowA1Id = crypto.randomUUID();
const passengerRowA2Id = crypto.randomUUID();
const passengerRowBId = crypto.randomUUID();
const ticketA1Id = crypto.randomUUID();
const ticketA2Id = crypto.randomUUID();
const ticketBId = crypto.randomUUID();
const parcelSentAId = crypto.randomUUID();
const parcelSentBId = crypto.randomUUID();
const parcelReceivedAId = crypto.randomUUID();
const datePart = new Date().toISOString().slice(0, 10).replaceAll('-', '');
const codeSuffix = () => crypto.randomBytes(6).toString('hex').slice(0, 8).toUpperCase();
const bookingACode = `VR-${datePart}-${codeSuffix()}`;
const bookingBCode = `VR-${datePart}-${codeSuffix()}`;
const ticketA1Code = `VT-${datePart}-${codeSuffix()}`;
const ticketA2Code = `VT-${datePart}-${codeSuffix()}`;
const ticketBCode = `VT-${datePart}-${codeSuffix()}`;
const parcelACode = `VR-PCL-${datePart}-${codeSuffix()}`;
const parcelBCode = `VR-PCL-${datePart}-${codeSuffix()}`;
const parcelReceivedCode = `VR-PCL-${datePart}-${codeSuffix()}`;
const objectPath = `vehicles/${operatorId}/${crypto.randomUUID()}.png`;
const staffFirebaseId = crypto.randomUUID();
let firebaseApp;
const storageObjectPaths = new Set();

function readEnv(file) {
  const result = {};
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const match = line.match(/^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/);
    if (!match) continue;
    let value = match[2].trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    result[match[1]] = value;
  }
  return result;
}

function required(name) {
  const value = env[name];
  if (!value) throw new Error(`Missing required environment variable ${name}.`);
  return value;
}

function pass(label, evidence = '') {
  console.log(`PASS | ${label}${evidence ? ` | ${evidence}` : ''}`);
}

function runSql(database, sql, capture = false) {
  const result = spawnSync(
    'docker',
    ['exec', '-i', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', database, ...(capture ? ['-At'] : [])],
    {
      cwd: root,
      input: sql,
      encoding: 'utf8',
      stdio: capture ? ['pipe', 'pipe', 'pipe'] : ['pipe', 'ignore', 'pipe'],
    },
  );
  if (result.error || result.status !== 0) {
    throw new Error(`psql failed for ${database}: ${(result.stderr ?? '').slice(0, 500)}`);
  }
  return capture ? result.stdout.trim() : '';
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
    throw new Error(`${method} ${url}: expected ${expectedStatus}, got ${response.status}: ${text.slice(0, 500)}`);
  }
  return json;
}

async function issueToken({ subject, role, email, operator }) {
  const settings = JSON.parse(fs.readFileSync(
    path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
    'utf8',
  ));
  const key = await importPKCS8(settings.IdentityJwt.PrivateKey, 'RS256');
  const claims = { role, email, hasPhone: 'true' };
  if (operator) claims.operatorId = operator;
  return new SignJWT(claims)
    .setProtectedHeader({ alg: 'RS256', kid: settings.IdentityJwt.Kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
}

function decodeJwtPayload(token) {
  return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'));
}

function seedData() {
  const createdA = new Date(Date.now() - 120_000).toISOString();
  const createdB = new Date(Date.now() - 180_000).toISOString();
  runSql('vietride_identity', `
    insert into vietride_identity.users (id,email,password_hash,display_name,role,status,operator_id)
    values
      ('${operatorAdminId}','${tag}-operator@example.test',null,'${tag} operator','OPERATOR_ADMIN','ACTIVE','${operatorId}'),
      ('${passengerAId}','${tag}-a@example.test',null,'${tag} passenger A','PASSENGER','ACTIVE',null),
      ('${passengerBId}','${tag}-b@example.test',null,'${tag} passenger B','PASSENGER','ACTIVE',null),
      ('${driverId}','${tag}-driver@example.test',null,'${tag} driver','DRIVER','ACTIVE','${operatorId}');
  `);
  runSql('vietride_booking', `
    begin;
    insert into vietride_booking.bookings
      (id,booking_code,passenger_user_id,trip_id,operator_id,pickup_station_id,base_fare,discount_amount,total_amount,status,
       trip_snapshot_origin_name,trip_snapshot_dest_name,trip_snapshot_departure,trip_snapshot_route_name,trip_current_departure,created_at,updated_at)
    values
      ('${bookingAId}','${bookingACode}','${passengerAId}','${tripId}','${operatorId}',gen_random_uuid(),350000,0,350000,'CONFIRMED',
       '${tag} Origin','${tag} Destination',now()+interval '1 day','${tag} Route',now()+interval '1 day 10 minutes','${createdA}','${createdA}'),
      ('${bookingBId}','${bookingBCode}','${passengerBId}','${tripId}','${operatorId}',gen_random_uuid(),200000,0,200000,'CONFIRMED',
       '${tag} Other Origin','${tag} Other Destination',now()+interval '2 days','${tag} Other Route',now()+interval '2 days','${createdB}','${createdB}');
    insert into vietride_booking.passengers (id,booking_id,seat_number) values
      ('${passengerRowA1Id}','${bookingAId}','A01'),
      ('${passengerRowA2Id}','${bookingAId}','A02'),
      ('${passengerRowBId}','${bookingBId}','B01');
    insert into vietride_booking.tickets
      (id,booking_id,passenger_id,ticket_code,seat_number,status,fare_amount,discount_amount,paid_amount,issued_at)
    values
      ('${ticketA1Id}','${bookingAId}','${passengerRowA1Id}','${ticketA1Code}','A01','ISSUED',175000,0,175000,now()),
      ('${ticketA2Id}','${bookingAId}','${passengerRowA2Id}','${ticketA2Code}','A02','ISSUED',175000,0,175000,now()),
      ('${ticketBId}','${bookingBId}','${passengerRowBId}','${ticketBCode}','B01','ISSUED',200000,0,200000,now());
    commit;
  `);
  runSql('vietride_parcel', `
    insert into vietride_parcel.parcels
      (id,parcel_code,sender_user_id,recipient_user_id,recipient_name,recipient_phone,operator_id,trip_id,description,photo_url,
       size_category,estimated_weight_kg,delivery_method,deposit_amount,status,original_deposit_amount,estimated_chargeable_weight_kg,
       estimated_dim_weight_kg,estimated_height_cm,estimated_length_cm,estimated_volume_m3,estimated_width_cm,total_price_vnd,created_at,updated_at)
    values
      ('${parcelSentAId}','${parcelACode}','${passengerAId}',null,'Recipient A','+84901234567','${operatorId}','${tripId}','${tag}','https://example.test/${tag}.png',
       'MEDIUM',2.0,'TERMINAL_PICKUP',120000,'PENDING_OPERATOR_REVIEW',120000,2.0,1.0,10,10,0.001,10,120000,'${createdA}','${createdA}'),
      ('${parcelSentBId}','${parcelBCode}','${passengerBId}',null,'Recipient B','+84901234568','${operatorId}','${tripId}','${tag}',null,
       'SMALL',1.0,'TERMINAL_PICKUP',80000,'PENDING_OPERATOR_REVIEW',80000,1.0,1.0,10,10,0.001,10,80000,'${createdB}','${createdB}'),
      ('${parcelReceivedAId}','${parcelReceivedCode}','${passengerBId}','${passengerAId}','Passenger A','+84901234569','${operatorId}','${tripId}','${tag}',null,
       'SMALL',1.0,'TERMINAL_PICKUP',90000,'PENDING_OPERATOR_REVIEW',90000,1.0,1.0,10,10,0.001,10,90000,now()-interval '1 minute',now()-interval '1 minute');
  `);
}

async function testHistory(passengerAToken) {
  const from = encodeURIComponent(new Date(Date.now() - 86_400_000).toISOString());
  const to = encodeURIComponent(new Date(Date.now() + 86_400_000).toISOString());
  const ticketResponse = await api(
    'GET',
    `/v1/passenger/history?type=TICKET&status=CONFIRMED&from=${from}&to=${to}&page=1&pageSize=20`,
    passengerAToken,
    undefined,
    200,
  );
  const ticketItem = ticketResponse?.data?.items?.find((item) => item.id === bookingAId);
  if (!ticketItem || ticketItem.type !== 'TICKET' || ticketItem.parcel !== null || ticketItem.ticket?.tickets?.length !== 2) {
    throw new Error('TICKET history did not return the expected Booking card and two nested Tickets.');
  }
  if (ticketResponse.data.items.some((item) => item.id === bookingBId)) {
    throw new Error('TICKET history leaked passenger B Booking.');
  }
  pass('Passenger history TICKET owner isolation and nested Tickets', `booking=${bookingAId}`);

  const parcelResponse = await api(
    'GET',
    `/v1/passenger/history?type=PARCEL&status=PENDING_OPERATOR_REVIEW&from=${from}&to=${to}&page=1&pageSize=20`,
    passengerAToken,
    undefined,
    200,
  );
  const parcelItem = parcelResponse?.data?.items?.find((item) => item.id === parcelSentAId);
  if (!parcelItem || parcelItem.type !== 'PARCEL' || parcelItem.ticket !== null || !parcelItem.parcel) {
    throw new Error('PARCEL history did not return the expected normalized Parcel card.');
  }
  if (parcelResponse.data.items.some((item) => item.id === parcelSentBId || item.id === parcelReceivedAId)) {
    throw new Error('PARCEL history leaked passenger B or received-only Parcel data.');
  }
  pass('Passenger history PARCEL sender-only isolation', `parcel=${parcelSentAId}`);

  await api('GET', '/v1/passenger/history?type=ALL', passengerAToken, undefined, 422);
  pass('Passenger history rejects unsupported type=ALL');
  const empty = await api('GET', '/v1/passenger/history?type=TICKET&page=999&pageSize=20', passengerAToken, undefined, 200);
  if ((empty?.data?.items?.length ?? -1) !== 0) throw new Error('History empty page was not empty.');
  pass('Passenger history deterministic empty page');
}

async function exchangeCustomToken(customToken) {
  const response = await fetch(
    `https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key=${encodeURIComponent(required('FIREBASE_WEB_API_KEY'))}`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: customToken, returnSecureToken: true }),
    },
  );
  const text = await response.text();
  if (!response.ok) throw new Error(`Firebase custom-token exchange failed (${response.status}): ${text.slice(0, 500)}`);
  return JSON.parse(text);
}

async function upload(pathValue, idToken, contentType, body, expectedStatus) {
  const endpoint = `https://firebasestorage.googleapis.com/v0/b/${encodeURIComponent(required('FIREBASE_WEB_STORAGE_BUCKET'))}/o?name=${encodeURIComponent(pathValue)}`;
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: {
      ...(idToken ? { Authorization: `Firebase ${idToken}` } : {}),
      'Content-Type': contentType,
      'X-Firebase-Storage-Version': 'webjs/e2e',
    },
    body,
  });
  const text = await response.text();
  if (response.status !== expectedStatus) {
    throw new Error(`Storage upload ${pathValue}: expected ${expectedStatus}, got ${response.status}: ${text.slice(0, 500)}`);
  }
  return text ? JSON.parse(text) : undefined;
}

function getFirebaseAdminApp() {
  if (!firebaseApp) {
    firebaseApp = initializeApp({
      credential: cert({
        projectId: required('FIREBASE_PROJECT_ID'),
        clientEmail: required('FIREBASE_CLIENT_EMAIL'),
        privateKey: required('FIREBASE_PRIVATE_KEY').replace(/\\n/g, '\n'),
      }),
      storageBucket: required('FIREBASE_WEB_STORAGE_BUCKET'),
    }, `e2e-${tag}`);
  }
  return firebaseApp;
}

async function requestFirebaseSession(vietRideToken, purpose) {
  const tokenResponse = await api(
    'POST',
    '/v1/firebase/custom-token',
    vietRideToken,
    purpose ? { purpose } : undefined,
    200,
  );
  const customToken = tokenResponse?.data?.token;
  if (!customToken) throw new Error(`Identity did not return a Firebase custom token for ${purpose ?? 'default purpose'}.`);
  const session = await exchangeCustomToken(customToken);
  if (!session.idToken || !session.refreshToken) {
    throw new Error(`Firebase token exchange returned no session tokens for ${purpose ?? 'default purpose'}.`);
  }
  return { tokenResponse, customToken, session };
}

async function uploadValidImage(pathValue, idToken, png) {
  const result = await upload(pathValue, idToken, 'image/png', png, 200);
  storageObjectPaths.add(pathValue);
  if (result?.name !== pathValue) throw new Error(`Firebase Storage returned an unexpected object path for ${pathValue}.`);
}

function downloadUrl(pathValue) {
  return `https://firebasestorage.googleapis.com/v0/b/${encodeURIComponent(required('FIREBASE_WEB_STORAGE_BUCKET'))}/o/${encodeURIComponent(pathValue)}?alt=media`;
}

async function testFirebase(operatorToken, passengerToken, driverToken, systemAdminToken) {
  const { tokenResponse, customToken, session } = await requestFirebaseSession(operatorToken);
  const claims = decodeJwtPayload(customToken);
  if (claims.uid !== operatorAdminId
      || claims.claims?.operatorId !== operatorId
      || claims.claims?.role !== 'OPERATOR_ADMIN'
      || claims.claims?.uploadPurpose !== 'VEHICLE_IMAGE'
      || tokenResponse?.data?.uploadPath !== `vehicles/${operatorId}/`) {
    throw new Error('Firebase custom-token UID/custom claims do not match VietRide identity.');
  }
  pass('Firebase Custom Token UID and claims');

  const idTokenClaims = decodeJwtPayload(session.idToken);
  if (idTokenClaims.user_id !== operatorAdminId
      || idTokenClaims.operatorId !== operatorId
      || idTokenClaims.role !== 'OPERATOR_ADMIN'
      || idTokenClaims.uploadPurpose !== 'VEHICLE_IMAGE') {
    throw new Error('Firebase ID token did not carry the expected exchanged custom claims.');
  }
  pass('Firebase custom-token exchange');

  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
  await uploadValidImage(objectPath, session.idToken, png);
  pass('Firebase Storage valid operator upload', objectPath);

  const publicRead = await fetch(downloadUrl(objectPath));
  if (!publicRead.ok || !Buffer.from(await publicRead.arrayBuffer()).equals(png)) {
    throw new Error(`Firebase Storage public read failed (${publicRead.status}).`);
  }
  pass('Firebase Storage public read');

  const logoPath = `operators/${operatorId}/logo/${crypto.randomUUID()}.webp`;
  const logoAuth = await requestFirebaseSession(operatorToken, 'OPERATOR_LOGO');
  await uploadValidImage(logoPath, logoAuth.session.idToken, png);
  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.png`, logoAuth.session.idToken, 'image/png', png, 403);
  pass('Firebase Storage OPERATOR_LOGO purpose and cross-purpose isolation', logoPath);

  const parcelPath = `parcels/${passengerAId}/${crypto.randomUUID()}.jpg`;
  const parcelAuth = await requestFirebaseSession(passengerToken, 'PARCEL_PHOTO');
  await uploadValidImage(parcelPath, parcelAuth.session.idToken, png);
  await upload(`parcels/${passengerBId}/${crypto.randomUUID()}.jpg`, parcelAuth.session.idToken, 'image/jpeg', png, 403);
  pass('Firebase Storage PARCEL_PHOTO owner isolation', parcelPath);

  const avatarPath = `avatars/${passengerAId}/${crypto.randomUUID()}.png`;
  const avatarAuth = await requestFirebaseSession(passengerToken, 'USER_AVATAR');
  await uploadValidImage(avatarPath, avatarAuth.session.idToken, png);
  await upload(`avatars/${passengerBId}/${crypto.randomUUID()}.png`, avatarAuth.session.idToken, 'image/png', png, 403);
  await api(
    'PATCH',
    '/v1/users/me/avatar',
    passengerToken,
    { avatarUrl: downloadUrl(avatarPath) },
    200,
    true,
  );
  const me = await api('GET', '/v1/users/me', passengerToken, undefined, 200);
  if (me?.data?.avatarUrl !== downloadUrl(avatarPath)) {
    throw new Error('Passenger avatar URL was not persisted by Identity.');
  }
  pass('Firebase Storage USER_AVATAR owner isolation and Identity persistence', avatarPath);

  const incidentPath = `incidents/${operatorId}/${driverId}/${crypto.randomUUID()}.png`;
  const incidentAuth = await requestFirebaseSession(driverToken, 'INCIDENT_PHOTO');
  await uploadValidImage(incidentPath, incidentAuth.session.idToken, png);
  await upload(`incidents/${operatorId}/${passengerAId}/${crypto.randomUUID()}.png`, incidentAuth.session.idToken, 'image/png', png, 403);
  await upload(`incidents/${crypto.randomUUID()}/${driverId}/${crypto.randomUUID()}.png`, incidentAuth.session.idToken, 'image/png', png, 403);
  pass('Firebase Storage INCIDENT_PHOTO user/operator isolation', incidentPath);

  await upload(`vehicles/${crypto.randomUUID()}/${crypto.randomUUID()}.png`, session.idToken, 'image/png', png, 403);
  pass('Firebase Storage rejects mismatched operator path');
  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.png`, null, 'image/png', png, 403);
  pass('Firebase Storage rejects anonymous upload');

  const staffCustomToken = await getAuth(getFirebaseAdminApp()).createCustomToken(
    staffFirebaseId,
    { operatorId, role: 'OPERATOR_STAFF' },
  );
  const staffSession = await exchangeCustomToken(staffCustomToken);
  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.png`, staffSession.idToken, 'image/png', png, 403);
  pass('Firebase Storage rejects OPERATOR_STAFF upload');

  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.png`, session.idToken, 'image/png', Buffer.alloc(0), 403);
  pass('Firebase Storage rejects empty file');
  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.txt`, session.idToken, 'text/plain', Buffer.from('not-image'), 403);
  pass('Firebase Storage rejects unsupported MIME');
  await upload(`vehicles/${operatorId}/${crypto.randomUUID()}.png`, session.idToken, 'image/png', Buffer.alloc(5 * 1024 * 1024), 403);
  pass('Firebase Storage rejects file at 5 MiB');

  await new Promise((resolve) => setTimeout(resolve, 2100));
  await api('POST', `/v1/admin/users/${operatorAdminId}/lock`, systemAdminToken, undefined, 200, true);
  pass('VietRide admin lock enqueued Firebase session revoke');

  let revoked = false;
  for (let attempt = 0; attempt < 30; attempt += 1) {
    const response = await fetch(
      `https://securetoken.googleapis.com/v1/token?key=${encodeURIComponent(required('FIREBASE_WEB_API_KEY'))}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: session.refreshToken }),
      },
    );
    if (!response.ok) { revoked = true; break; }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  if (!revoked) throw new Error('Firebase refresh token remained usable after VietRide user lock.');
  pass('Firebase refresh token revoked after Outbox consumer');
  await api('POST', '/v1/firebase/custom-token', operatorToken, undefined, 403);
  pass('Locked VietRide token cannot mint a new Firebase custom token');
}

async function cleanup() {
  try {
    getFirebaseAdminApp();
    for (const pathValue of storageObjectPaths) {
      await getStorage(firebaseApp).bucket().file(pathValue).delete({ ignoreNotFound: true });
    }
    await getAuth(firebaseApp).deleteUser(operatorAdminId).catch(() => {});
    await getAuth(firebaseApp).deleteUser(passengerAId).catch(() => {});
    await getAuth(firebaseApp).deleteUser(driverId).catch(() => {});
    await getAuth(firebaseApp).deleteUser(staffFirebaseId).catch(() => {});
    await deleteApp(firebaseApp);
  } catch (error) {
    console.warn(`WARN | Firebase cleanup incomplete | ${error.message}`);
  }
  try {
    runSql('vietride_booking', `delete from vietride_booking.bookings where id in ('${bookingAId}','${bookingBId}');`);
    runSql('vietride_parcel', `delete from vietride_parcel.parcels where id in ('${parcelSentAId}','${parcelSentBId}','${parcelReceivedAId}');`);
    runSql('vietride_identity', `
      delete from vietride_identity.activity_logs where user_id in ('${operatorAdminId}','${passengerAId}','${passengerBId}','${driverId}');
      delete from vietride_identity.refresh_tokens where user_id in ('${operatorAdminId}','${passengerAId}','${passengerBId}','${driverId}');
      delete from vietride_identity.users where id in ('${operatorAdminId}','${passengerAId}','${passengerBId}','${driverId}');
    `);
    pass('E2E database cleanup');
  } catch (error) {
    console.warn(`WARN | Database cleanup incomplete | ${error.message}`);
  }
}

async function main() {
  for (const name of [
    'FIREBASE_PROJECT_ID', 'FIREBASE_CLIENT_EMAIL', 'FIREBASE_PRIVATE_KEY',
    'FIREBASE_WEB_API_KEY', 'FIREBASE_WEB_STORAGE_BUCKET',
  ]) required(name);
  seedData();
  pass('E2E fixtures seeded', tag);
  const operatorToken = await issueToken({
    subject: operatorAdminId,
    role: 'OPERATOR_ADMIN',
    email: `${tag}-operator@example.test`,
    operator: operatorId,
  });
  const systemAdminToken = await issueToken({
    subject: systemAdminId,
    role: 'SYSTEM_ADMIN',
    email: `${tag}-system@example.test`,
  });
  const passengerAToken = await issueToken({
    subject: passengerAId,
    role: 'PASSENGER',
    email: `${tag}-a@example.test`,
  });
  const driverToken = await issueToken({
    subject: driverId,
    role: 'DRIVER',
    email: `${tag}-driver@example.test`,
    operator: operatorId,
  });
  await testHistory(passengerAToken);
  await testFirebase(operatorToken, passengerAToken, driverToken, systemAdminToken);
}

try {
  await main();
  console.log('RESULT | PASS | Firebase upload and unified passenger history E2E');
} catch (error) {
  console.error(`RESULT | FAIL | ${error.message}`);
  process.exitCode = 1;
} finally {
  await cleanup();
}
