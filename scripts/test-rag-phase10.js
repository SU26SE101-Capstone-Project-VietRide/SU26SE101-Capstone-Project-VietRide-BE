const crypto = require('node:crypto');

const DEFAULT_RAG_URL = 'http://localhost:3003';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const TOKEN_TTL_SECONDS = 120;
const BASE_URL = (process.env.BASE_URL ?? process.env.RAG_URL ?? DEFAULT_RAG_URL).replace(/\/$/, '');
const CONFIG_URL = `${BASE_URL}/api/v1/admin/rag-config`;
const ADMIN_USER_ID = process.env.USER_ID ?? '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = process.env.PASSENGER_USER_ID ?? '22222222-2222-2222-2222-222222222222';
const INTERNAL_JWT_SECRET = process.env.INTERNAL_JWT_SECRET;

async function main() {
  const adminAuth = resolveAuthHeader({
    token: process.env.ADMIN_ACCESS_TOKEN ?? process.env.ACCESS_TOKEN,
    role: 'SYSTEM_ADMIN',
    sub: ADMIN_USER_ID,
    reqId: 'rag-phase10-admin',
  });
  const passengerAuth = resolveAuthHeader({
    token: process.env.PASSENGER_TOKEN,
    role: 'PASSENGER',
    sub: PASSENGER_USER_ID,
    reqId: 'rag-phase10-passenger',
  });

  await testListHappyPath(adminAuth);
  await testReloadHappyPath(adminAuth);
  await testAuthFail();
  await testRoleFail(passengerAuth);
  await testValidationFail(adminAuth);

  pass('RAG Phase 10 runtime config script verify passed.');
}

async function testListHappyPath(adminAuth) {
  const response = await fetch(CONFIG_URL, {
    headers: { 'X-Internal-Auth': adminAuth },
  });
  const body = await response.json();
  const data = body.data ?? body;
  assert(response.status === 200, `config list expected 200, got ${response.status}`);
  assert(Array.isArray(data), 'config list response must be an array or ApiResponse data array');
  assert(data.some((item) => item.key === 'chat.system_prompt'), 'config list missing chat.system_prompt');
  assert(data.some((item) => item.key === 'intent.off_topic_refusal'), 'config list missing intent.off_topic_refusal');
  pass('config list returned runtime config keys.');
}

async function testReloadHappyPath(adminAuth) {
  const response = await fetch(`${CONFIG_URL}/reload`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': adminAuth },
  });
  const body = await response.json();
  const data = body.data ?? body;
  assert(response.status === 201 || response.status === 200, `config reload expected 200/201, got ${response.status}`);
  assert(data.reloaded === true, 'config reload response must include reloaded=true');
  pass('config reload refreshed runtime config cache.');
}

async function testAuthFail() {
  const response = await fetch(CONFIG_URL);
  assert(response.status === 401, `auth fail expected 401, got ${response.status}`);
  pass('missing internal JWT returned 401.');
}

async function testRoleFail(passengerAuth) {
  const response = await fetch(`${CONFIG_URL}/chat.no_context_text`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': passengerAuth,
    },
    body: JSON.stringify({ value: 'No retrieved context.' }),
  });
  assert(response.status === 403, `role fail expected 403, got ${response.status}`);
  pass('non-admin update returned 403.');
}

async function testValidationFail(adminAuth) {
  const response = await fetch(`${CONFIG_URL}/documents.max_file_bytes`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': adminAuth,
    },
    body: JSON.stringify({ value: 1 }),
  });
  assert(response.status === 400, `validation fail expected 400, got ${response.status}`);
  pass('invalid runtime config value returned 400.');
}

function resolveAuthHeader({ token, role, sub, reqId }) {
  if (token) {
    return token.startsWith('Bearer ') ? token : `Bearer ${token}`;
  }
  if (!INTERNAL_JWT_SECRET) {
    fail('Set INTERNAL_JWT_SECRET or access token env vars before running RAG Phase 10 verify script.');
  }
  return `Bearer ${signInternalJwt({ sub, role, reqId })}`;
}

function signInternalJwt(payload) {
  const now = Math.floor(Date.now() / 1000);
  const header = { alg: 'HS256', typ: 'JWT' };
  const claims = {
    ...payload,
    iss: INTERNAL_JWT_ISSUER,
    aud: INTERNAL_JWT_AUDIENCE,
    iat: now,
    exp: now + TOKEN_TTL_SECONDS,
  };
  const encodedHeader = base64UrlJson(header);
  const encodedClaims = base64UrlJson(claims);
  const signature = crypto
    .createHmac('sha256', INTERNAL_JWT_SECRET)
    .update(`${encodedHeader}.${encodedClaims}`)
    .digest('base64url');
  return `${encodedHeader}.${encodedClaims}.${signature}`;
}

function base64UrlJson(value) {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

function assert(condition, message) {
  if (!condition) fail(message);
}

function pass(message) {
  console.log(`[PASS] ${message}`);
}

function fail(message) {
  console.error(`[FAIL] ${message}`);
  process.exit(1);
}

main().catch((error) => {
  console.error('[FAIL] Unexpected script error:', error instanceof Error ? error.message : error);
  process.exit(1);
});
