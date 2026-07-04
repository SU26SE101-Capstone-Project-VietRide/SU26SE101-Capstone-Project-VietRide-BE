const DEFAULT_RAG_URL = 'http://localhost:3003';
const SERVICE_URL = process.env.BASE_URL || process.env.RAG_URL || DEFAULT_RAG_URL;
const CHAT_URL = `${SERVICE_URL.replace(/\/$/, '')}/v1/rag/chat`;
const TEST_USER_ID = process.env.USER_ID || '11111111-1111-1111-1111-111111111111';
const TEST_ROLE = process.env.RAG_TEST_ROLE || 'PASSENGER';
const TEST_OPERATOR_ID = process.env.OPERATOR_ID;

async function main() {
  const internalAuth = await resolveInternalAuth();
  const results = [];
  results.push(await testAuthFail());
  results.push(await testValidationFail(internalAuth));
  results.push(await testHappyPath(internalAuth));

  const failed = results.filter((result) => !result.pass);
  for (const result of results) {
    const status = result.pass ? 'PASS' : 'FAIL';
    console.log(`${status} ${result.name}${result.detail ? ` - ${result.detail}` : ''}`);
  }

  if (failed.length > 0) {
    process.exitCode = 1;
    return;
  }
  process.exitCode = 0;
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

async function testHappyPath(internalAuth) {
  const response = await fetch(CHAT_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Auth': internalAuth,
    },
    body: JSON.stringify({ message: process.env.RAG_TEST_MESSAGE || 'VietRide ho tro hanh khach nhu the nao?' }),
  });

  const text = await response.text();
  const hasTokenOrDone = text.includes('event: token') || text.includes('event: done');
  const hasDone = text.includes('event: done');
  return {
    name: 'happy path streams SSE',
    pass: response.status === 200 && hasTokenOrDone && hasDone,
    detail: `status=${response.status}, bytes=${text.length}`,
  };
}

async function resolveInternalAuth() {
  if (process.env.ACCESS_TOKEN) {
    return process.env.ACCESS_TOKEN.startsWith('Bearer ')
      ? process.env.ACCESS_TOKEN
      : `Bearer ${process.env.ACCESS_TOKEN}`;
  }

  if (!process.env.INTERNAL_JWT_SECRET) {
    throw new Error('Set ACCESS_TOKEN or INTERNAL_JWT_SECRET before running RAG Phase 5 verify script');
  }

  const { SignJWT } = await import('jose');
  const payload = {
    sub: TEST_USER_ID,
    role: TEST_ROLE,
    reqId: 'script-rag-phase5',
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
  console.error(`FAIL rag phase5 script - ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
