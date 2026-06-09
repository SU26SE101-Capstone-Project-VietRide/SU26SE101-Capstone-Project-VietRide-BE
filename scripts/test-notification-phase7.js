const crypto = require('node:crypto');
const fs = require('node:fs');

const DEFAULT_NOTIFICATION_URL = 'http://localhost:3002';
const DEFAULT_IDENTITY_INTERNAL_URL = 'http://localhost:5001';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const INTERNAL_JWT_TTL_SECONDS = Number(process.env.INTERNAL_JWT_TTL_SEC ?? '120');

const notificationUrl = trimTrailingSlash(
  process.env.BASE_URL ?? process.env.NOTIFICATION_URL ?? DEFAULT_NOTIFICATION_URL,
);
const identityUrl = trimTrailingSlash(
  process.env.IDENTITY_INTERNAL_BASE_URL ?? process.env.IDENTITY_URL ?? DEFAULT_IDENTITY_INTERNAL_URL,
);

async function main() {
  const results = [];

  results.push(await runCase('ready endpoint happy path', assertNotificationReady));
  results.push(await runCase('REST auth fail envelope', assertAuthFailEnvelope));
  results.push(await runCase('Identity internal device-token lookup', assertIdentityDeviceTokens));
  results.push(await runCase('Firebase credential config present', assertFirebaseConfig));

  const failed = results.filter((result) => !result.ok);
  for (const result of results) {
    const prefix = result.ok ? 'PASS' : 'FAIL';
    console.log(`${prefix} ${result.name}${result.detail ? ` - ${result.detail}` : ''}`);
  }

  if (failed.length > 0) {
    process.exitCode = 1;
  }
}

async function runCase(name, fn) {
  try {
    const detail = await fn();
    return { name, ok: true, detail };
  } catch (error) {
    return { name, ok: false, detail: error instanceof Error ? error.message : String(error) };
  }
}

async function assertNotificationReady() {
  const response = await fetch(`${notificationUrl}/ready`);
  assert(response.status === 200, `expected 200, got ${response.status}`);
  const body = await response.json();
  assert(body.status === 'ok' && body.service === 'notification', 'unexpected ready payload');
  return notificationUrl;
}

async function assertAuthFailEnvelope() {
  const response = await fetch(`${notificationUrl}/api/v1/notifications`);
  assert(response.status === 401, `expected 401, got ${response.status}`);
  const body = await response.json();
  assert(body.success === false, 'expected ApiResponse failure envelope');
  assert(body.error && typeof body.error.code === 'string', 'expected error code');
  return body.error.code;
}

async function assertIdentityDeviceTokens() {
  const userId = process.env.USER_ID;
  const secret = process.env.INTERNAL_JWT_SECRET;
  assert(userId, 'USER_ID is required for Identity device-token verify');
  assert(secret && secret.length >= 32, 'INTERNAL_JWT_SECRET >= 32 chars is required');

  const token = createInternalJwt(secret);
  const response = await fetch(`${identityUrl}/internal/v1/users/${userId}/device-tokens`, {
    headers: {
      [INTERNAL_AUTH_HEADER]: `Bearer ${token}`,
    },
  });
  assert(response.status === 200, `expected 200 from Identity, got ${response.status}`);
  const body = await response.json();
  assert(Array.isArray(body), 'expected Identity to return device-token array');
  for (const item of body) {
    assert(typeof item.fcmToken === 'string', 'device token item missing fcmToken');
    assert(typeof item.platform === 'string', 'device token item missing platform');
  }
  return `${body.length} active token(s)`;
}

async function assertFirebaseConfig() {
  if (process.env.GOOGLE_APPLICATION_CREDENTIALS) {
    assert(
      fs.existsSync(process.env.GOOGLE_APPLICATION_CREDENTIALS),
      'GOOGLE_APPLICATION_CREDENTIALS file does not exist',
    );
    return 'GOOGLE_APPLICATION_CREDENTIALS';
  }

  assert(process.env.FCM_PROJECT_ID, 'FCM_PROJECT_ID is required without GOOGLE_APPLICATION_CREDENTIALS');
  assert(process.env.FCM_CLIENT_EMAIL, 'FCM_CLIENT_EMAIL is required without GOOGLE_APPLICATION_CREDENTIALS');
  assert(process.env.FCM_PRIVATE_KEY, 'FCM_PRIVATE_KEY is required without GOOGLE_APPLICATION_CREDENTIALS');
  return 'service account env';
}

function createInternalJwt(secret) {
  const now = Math.floor(Date.now() / 1000);
  const header = { alg: 'HS256', typ: 'JWT' };
  const payload = {
    sub: 'notification-service',
    iss: INTERNAL_JWT_ISSUER,
    aud: INTERNAL_JWT_AUDIENCE,
    iat: now,
    nbf: now - 5,
    exp: now + INTERNAL_JWT_TTL_SECONDS,
  };
  const encodedHeader = base64Url(JSON.stringify(header));
  const encodedPayload = base64Url(JSON.stringify(payload));
  const signature = crypto
    .createHmac('sha256', secret)
    .update(`${encodedHeader}.${encodedPayload}`)
    .digest('base64url');

  return `${encodedHeader}.${encodedPayload}.${signature}`;
}

function base64Url(value) {
  return Buffer.from(value).toString('base64url');
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, '');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
