const crypto = require('node:crypto');

const DEFAULT_RAG_URL = 'http://localhost:3003';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const TOKEN_TTL_SECONDS = 120;
const USER_ID = process.env.USER_ID ?? '11111111-1111-1111-1111-111111111111';
const BASE_URL = (process.env.BASE_URL ?? process.env.RAG_URL ?? DEFAULT_RAG_URL).replace(/\/$/, '');
const INTERNAL_JWT_SECRET = process.env.INTERNAL_JWT_SECRET;

async function main() {
  if (!INTERNAL_JWT_SECRET) {
    fail('INTERNAL_JWT_SECRET is required to sign X-Internal-Auth for RAG smoke tests.');
  }

  const passengerToken = signInternalJwt({ sub: USER_ID, role: 'PASSENGER', reqId: 'rag-phase9-passenger' });
  const otherToken = signInternalJwt({
    sub: '99999999-9999-9999-9999-999999999999',
    role: 'PASSENGER',
    reqId: 'rag-phase9-other',
  });

  const assistantMessageId = await testChatHappyPath(passengerToken);
  await testFeedbackHappyPath(passengerToken, assistantMessageId);
  await testAuthFail(assistantMessageId);
  await testFeedbackValidationFail(passengerToken, assistantMessageId);
  await testFeedbackOwnershipFail(otherToken, assistantMessageId);

  pass('RAG Phase 9 script verify passed.');
}

async function testChatHappyPath(token) {
  const response = await postChat({
    token,
    payload: { message: 'Tôi muốn biết quy định hành lý của VietRide' },
  });
  const body = await response.text();
  assert(response.status === 200, `chat happy path expected 200, got ${response.status}`);
  const assistantMessageId = readSseDone(body).assistantMessageId;
  assert(assistantMessageId, 'chat done event missing assistantMessageId');
  pass('chat happy path returned assistant message.');
  return assistantMessageId;
}

async function testFeedbackHappyPath(token, assistantMessageId) {
  const response = await postFeedback({ token, assistantMessageId, payload: { rating: 1 } });
  const body = await response.json();
  assert(response.status === 201, `feedback happy path expected 201, got ${response.status}`);
  const data = body.data ?? body;
  assert(data.rating === 1, 'feedback response rating should be 1');
  pass('feedback happy path returned 201.');
}

async function testAuthFail(assistantMessageId) {
  const response = await postFeedback({ assistantMessageId, payload: { rating: 1 } });
  assert(response.status === 401, `auth fail expected 401, got ${response.status}`);
  pass('feedback auth fail returned 401.');
}

async function testFeedbackValidationFail(token, assistantMessageId) {
  const response = await postFeedback({ token, assistantMessageId, payload: { rating: 0 } });
  assert(response.status === 400, `feedback validation fail expected 400, got ${response.status}`);
  pass('feedback validation fail returned 400.');
}

async function testFeedbackOwnershipFail(token, assistantMessageId) {
  const response = await postFeedback({ token, assistantMessageId, payload: { rating: -1 } });
  assert(response.status === 403, `feedback ownership fail expected 403, got ${response.status}`);
  pass('feedback ownership fail returned 403.');
}

async function postChat({ token, payload }) {
  return fetch(`${BASE_URL}/v1/rag/chat`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': `Bearer ${token}`,
    },
    body: JSON.stringify(payload),
  });
}

async function postFeedback({ token, assistantMessageId, payload }) {
  return fetch(`${BASE_URL}/v1/rag/messages/${assistantMessageId}/feedback`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'X-Internal-Auth': `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(payload),
  });
}

function readSseDone(body) {
  const events = body.split('\n\n').filter(Boolean);
  for (const event of events) {
    if (!event.startsWith('event: done')) continue;
    const dataLine = event.split('\n').find((line) => line.startsWith('data: '));
    if (!dataLine) return {};
    return JSON.parse(dataLine.slice('data: '.length));
  }
  fail('SSE body missing done event.');
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
