const DEFAULT_RAG_URL = 'http://localhost:3003';
const SERVICE_URL = process.env.BASE_URL || process.env.RAG_URL || DEFAULT_RAG_URL;
const BASE_URL = SERVICE_URL.replace(/\/$/, '');
const READY_URL = `${BASE_URL}/ready`;
const CHAT_URL = `${BASE_URL}/api/v1/rag/chat`;
const TEST_USER_ID = process.env.USER_ID || '11111111-1111-1111-1111-111111111111';
const TEST_ROLE = process.env.RAG_TEST_ROLE || 'PASSENGER';
const TEST_OPERATOR_ID = process.env.OPERATOR_ID;
const EXPECT_HYBRID = process.env.RAG_EXPECT_HYBRID === 'true';

async function main() {
  const internalAuth = await resolveInternalAuth();
  const results = [];
  results.push(await testReady());
  results.push(await testAuthFail());
  results.push(await testValidationFail(internalAuth));
  results.push(await testHybridHappyPath(internalAuth));

  const failed = results.filter((result) => !result.pass);
  for (const result of results) {
    const status = result.skipped ? 'SKIP' : result.pass ? 'PASS' : 'FAIL';
    console.log(`${status} ${result.name}${result.detail ? ` - ${result.detail}` : ''}`);
  }

  process.exitCode = failed.length > 0 ? 1 : 0;
}

async function testReady() {
  const response = await fetch(READY_URL);
  const body = await readJson(response);
  const data = body?.data || body;
  return {
    name: 'ready returns dependency envelope',
    pass: response.status === 200 && data?.status === 'ok' && data?.service === 'rag',
    detail: `status=${response.status}`,
  };
}

async function testAuthFail() {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message: 'Kiem tra auth fail' }),
  });
  return {
    name: 'auth fail returns 401',
    pass: response.status === 401,
    detail: `status=${response.status}`,
  };
}

async function testValidationFail(internalAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': internalAuth,
    },
    body: JSON.stringify({ message: '' }),
  });
  return {
    name: 'validation fail returns 400',
    pass: response.status === 400,
    detail: `status=${response.status}`,
  };
}

async function testHybridHappyPath(internalAuth) {
  if (!EXPECT_HYBRID) {
    return {
      name: 'hybrid search flag path',
      pass: true,
      skipped: true,
      detail: 'start service with HYBRID_SEARCH_ENABLED=true and RAG_EXPECT_HYBRID=true to enforce',
    };
  }

  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': internalAuth,
    },
    body: JSON.stringify({
      message: process.env.RAG_TEST_MESSAGE || 'VietRide co quy dinh hanh ly ky gui nhu the nao?',
    }),
  });
  const text = await response.text();
  return {
    name: 'hybrid search chat streams SSE',
    pass: response.status === 200 && text.includes('event: done'),
    detail: `status=${response.status}, bytes=${text.length}`,
  };
}

async function readJson(response) {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

async function resolveInternalAuth() {
  if (process.env.ACCESS_TOKEN) {
    return process.env.ACCESS_TOKEN.startsWith('Bearer ')
      ? process.env.ACCESS_TOKEN
      : `Bearer ${process.env.ACCESS_TOKEN}`;
  }

  if (!process.env.INTERNAL_JWT_SECRET) {
    throw new Error('Set ACCESS_TOKEN or INTERNAL_JWT_SECRET before running RAG Phase 7 verify script');
  }

  const { SignJWT } = await import('jose');
  const payload = {
    sub: TEST_USER_ID,
    role: TEST_ROLE,
    reqId: 'script-rag-phase7',
    ...(TEST_OPERATOR_ID ? { operatorId: TEST_OPERATOR_ID } : {}),
  };
  const token = await new SignJWT(payload)
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(process.env.INTERNAL_JWT_SECRET));

  return `Bearer ${token}`;
}

main().catch((error) => {
  console.error(`FAIL rag phase7 script - ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
