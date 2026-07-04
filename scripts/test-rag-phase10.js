const crypto = require('node:crypto');

const DEFAULT_RAG_URL = 'http://localhost:3003';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const TOKEN_TTL_SECONDS = 120;
const BASE_URL = (process.env.BASE_URL ?? process.env.RAG_URL ?? DEFAULT_RAG_URL).replace(/\/$/, '');
const CONFIG_URL = `${BASE_URL}/v1/admin/rag-config`;
const CHAT_URL = `${BASE_URL}/v1/rag/chat`;
const FEEDBACK_URL = (id) => `${BASE_URL}/v1/rag/messages/${id}/feedback`;
const ADMIN_USER_ID = process.env.USER_ID ?? '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = process.env.PASSENGER_USER_ID ?? '22222222-2222-2222-2222-222222222222';
const OTHER_USER_ID = process.env.OTHER_USER_ID ?? '33333333-3333-3333-3333-333333333333';
const OPERATOR_ID = process.env.OPERATOR_ID ?? 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
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
  const driverAuth = resolveAuthHeader({
    token: process.env.DRIVER_TOKEN,
    role: 'DRIVER',
    sub: OTHER_USER_ID,
    operatorId: OPERATOR_ID,
    reqId: 'rag-phase10-driver',
  });

  // Runtime config tests
  await testListHappyPath(adminAuth);
  await testReloadHappyPath(adminAuth);
  await testAuthFail();
  await testRoleFail(passengerAuth);
  await testValidationFail(adminAuth);

  // Phase 10 chat production verification
  const assistantMessageId = await testChatHappyPath(passengerAuth);
  await testChatAuthFail();
  await testChatNonAdminOperatorIdForbidden(passengerAuth);
  await testChatAdminOperatorScope(adminAuth);
  await testChatAdminGlobalScope(adminAuth);
  await testChatDriverOperatorScope(driverAuth);
  await testFeedbackOwner(passengerAuth, assistantMessageId);
  await testFeedbackNonOwner(driverAuth, assistantMessageId);
  await testFeedbackAdminNonOwner(adminAuth, assistantMessageId);

  pass('RAG Phase 10 chat + config script verify passed.');
}

// --- Runtime config tests (existing) ---

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

// --- Phase 10 chat production verification tests ---

async function testChatHappyPath(passengerAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': passengerAuth,
    },
    body: JSON.stringify({ message: 'Quy định hành lý là gì?' }),
  });
  const body = await response.text();
  assert(response.status === 200, `chat happy path expected 200, got ${response.status}`);
  assert(body.includes('event: token'), 'chat response missing token events');
  assert(body.includes('event: done'), 'chat response missing done event');
  const messageId = parseAssistantMessageId(body);
  assert(messageId, 'chat response must include assistantMessageId in done event');
  pass('chat happy path returned SSE stream with token and done events.');
  return messageId;
}

async function testChatAuthFail() {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message: 'Test' }),
  });
  assert(response.status === 401, `chat auth fail expected 401, got ${response.status}`);
  const body = tryParseJson(await response.text());
  assert(body && body.success === false, 'chat auth fail should return ApiResponse error envelope');
  pass('chat without internal JWT returned 401 with ApiResponse envelope.');
}

async function testChatNonAdminOperatorIdForbidden(passengerAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': passengerAuth,
    },
    body: JSON.stringify({ message: 'Test', operatorId: OPERATOR_ID }),
  });
  assert(response.status === 403, `non-admin operatorId expected 403, got ${response.status}`);
  const body = tryParseJson(await response.text());
  assert(body && body.success === false, 'non-admin operatorId should return ApiResponse error envelope');
  assert(body.error && body.error.code === 'RAG_OPERATOR_SCOPE_FORBIDDEN', 'non-admin operatorId should return RAG_OPERATOR_SCOPE_FORBIDDEN');
  pass('non-admin sending operatorId returned 403 with error code RAG_OPERATOR_SCOPE_FORBIDDEN.');
}

async function testFeedbackOwner(passengerAuth, messageId) {
  const response = await fetch(FEEDBACK_URL(messageId), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': passengerAuth,
    },
    body: JSON.stringify({ rating: 1 }),
  });
  assert(response.status === 201, `owner feedback expected 201, got ${response.status}`);
  pass('feedback on own message returned 201.');
}

async function testFeedbackNonOwner(otherAuth, messageId) {
  const response = await fetch(FEEDBACK_URL(messageId), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': otherAuth,
    },
    body: JSON.stringify({ rating: 1 }),
  });
  const body = tryParseJson(await response.text());
  assert(response.status === 403, `non-owner feedback expected 403, got ${response.status}`);
  assert(body && body.success === false, 'non-owner feedback must return ApiResponse error envelope');
  pass('non-owner feedback returned 403 with ApiResponse envelope.');
}

async function testFeedbackAdminNonOwner(adminAuth, messageId) {
  const response = await fetch(FEEDBACK_URL(messageId), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': adminAuth,
    },
    body: JSON.stringify({ rating: 1 }),
  });
  const body = tryParseJson(await response.text());
  assert(response.status === 403, `admin non-owner feedback expected 403, got ${response.status}`);
  assert(body && body.success === false, 'admin non-owner feedback must return ApiResponse error envelope');
  pass('SYSTEM_ADMIN feedback on non-owned message returned 403 with ApiResponse envelope.');
}

async function testChatAdminOperatorScope(adminAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': adminAuth,
    },
    body: JSON.stringify({ message: 'Admin operator scoped query', operatorId: OPERATOR_ID }),
  });
  const body = await response.text();
  assert(response.status === 200, `admin operator scope expected 200, got ${response.status}`);
  assert(body.includes('event: token'), 'admin operator scope response missing token events');
  pass('admin with operatorId scope returned 200 SSE stream.');
}

async function testChatAdminGlobalScope(adminAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': adminAuth,
    },
    body: JSON.stringify({ message: 'Admin global query' }),
  });
  const body = await response.text();
  assert(response.status === 200, `admin global scope expected 200, got ${response.status}`);
  assert(body.includes('event: token'), 'admin global scope response missing token events');
  pass('admin without operatorId returned 200 SSE stream (global scope).');
}

async function testChatDriverOperatorScope(driverAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': driverAuth,
    },
    body: JSON.stringify({ message: 'Driver operator query' }),
  });
  const body = await response.text();
  assert(response.status === 200, `driver operator scope expected 200, got ${response.status}`);
  assert(body.includes('event: token'), 'driver operator scope response missing token events');
  pass('driver with operatorId returned 200 SSE stream.');
}

function parseAssistantMessageId(sseText) {
  const lines = sseText.split('\n');
  let currentEvent;
  for (const line of lines) {
    if (line.startsWith('event: ')) {
      currentEvent = line.slice(7).trim();
    } else if (line.startsWith('data: ') && currentEvent === 'done') {
      const data = tryParseJson(line.slice(6));
      if (data && data.assistantMessageId) return data.assistantMessageId;
    }
  }
  return null;
}

// --- Helper functions ---

function resolveAuthHeader({ token, role, sub, operatorId, reqId }) {
  if (token) {
    return token.startsWith('Bearer ') ? token : `Bearer ${token}`;
  }
  if (!INTERNAL_JWT_SECRET) {
    fail('Set INTERNAL_JWT_SECRET or access token env vars before running RAG Phase 10 verify script.');
  }
  const payload = { sub, role, reqId };
  if (operatorId) payload.operatorId = operatorId;
  return `Bearer ${signInternalJwt(payload)}`;
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

function tryParseJson(text) {
  try { return JSON.parse(text); } catch { return null; }
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
