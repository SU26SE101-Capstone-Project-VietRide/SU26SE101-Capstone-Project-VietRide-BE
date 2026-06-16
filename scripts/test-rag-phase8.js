const crypto = require('node:crypto');

const DEFAULT_RAG_URL = 'http://localhost:3003';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const TOKEN_TTL_SECONDS = 120;
const USER_ID = process.env.USER_ID ?? '11111111-1111-1111-1111-111111111111';
const USER_ROLE = process.env.USER_ROLE ?? 'PASSENGER';
const BASE_URL = (process.env.BASE_URL ?? process.env.RAG_URL ?? DEFAULT_RAG_URL).replace(/\/$/, '');
const INTERNAL_JWT_SECRET = process.env.INTERNAL_JWT_SECRET;

async function main() {
  if (!INTERNAL_JWT_SECRET) {
    fail('INTERNAL_JWT_SECRET is required to sign X-Internal-Auth for RAG smoke tests.');
  }

  const token = signInternalJwt({
    sub: USER_ID,
    role: USER_ROLE,
    reqId: 'rag-phase8-script-verify',
  });

  await testHappyPath(token);
  await testAuthFail();
  await testValidationFail(token);
  await testOffTopicRefusal(token);

  pass('RAG Phase 8 script verify passed.');
}

async function testHappyPath(token) {
  const response = await postChat({
    token,
    payload: { message: 'Tôi muốn biết quy định hành lý của VietRide' },
  });
  const body = await response.text();
  assert(response.status === 200, `happy path expected 200, got ${response.status}`);
  assertSse(body, 'happy path');
  pass('happy path SSE returned 200.');
}

async function testAuthFail() {
  const response = await postChat({
    payload: { message: 'Tôi muốn biết quy định hành lý của VietRide' },
  });
  assert(response.status === 401, `auth fail expected 401, got ${response.status}`);
  pass('auth fail returned 401.');
}

async function testValidationFail(token) {
  const response = await postChat({
    token,
    payload: { message: '' },
  });
  assert(response.status === 400, `validation fail expected 400, got ${response.status}`);
  pass('validation fail returned 400.');
}

async function testOffTopicRefusal(token) {
  const response = await postChat({
    token,
    payload: { message: 'Viết thơ về chứng khoán' },
  });
  const body = await response.text();
  assert(response.status === 200, `off-topic refusal expected 200 SSE, got ${response.status}`);
  assertSse(body, 'off-topic refusal');
  assert(
    body.includes('chỉ có thể hỗ trợ') || body.includes('event: error'),
    'off-topic response should refuse or return controlled SSE error',
  );
  pass('off-topic refusal returned controlled SSE.');
}

async function postChat({ token, payload }) {
  return fetch(`${BASE_URL}/api/v1/rag/chat`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'X-Internal-Auth': `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(payload),
  });
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

function assertSse(body, label) {
  assert(body.includes('event: token') || body.includes('event: error'), `${label} missing token/error SSE event`);
  assert(body.includes('data:'), `${label} missing SSE data`);
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
