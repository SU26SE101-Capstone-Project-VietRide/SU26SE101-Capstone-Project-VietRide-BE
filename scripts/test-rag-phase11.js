const DEFAULT_GATEWAY_URL = 'http://localhost:3000';
const BASE_URL = (process.env.BASE_URL ?? process.env.GATEWAY_URL ?? DEFAULT_GATEWAY_URL).replace(
  /\/$/,
  '',
);
const ACCESS_TOKEN = process.env.PASSENGER_TOKEN ?? process.env.ACCESS_TOKEN;
const OPERATOR_ID = process.env.OPERATOR_ID;
const POLICY_ID = process.env.POLICY_ID;

async function main() {
  const authorization = resolveAuthorization();

  await testPublishedList(authorization);
  await testAuthFailure();
  await testValidationFailure(authorization);
  if (POLICY_ID) await testPublishedDetail(authorization, POLICY_ID);

  pass('RAG Phase 11 published Policy verification passed.');
}

async function testPublishedList(authorization) {
  const query = new URLSearchParams({ page: '1', pageSize: '20' });
  if (OPERATOR_ID) query.set('operatorId', OPERATOR_ID);
  const response = await fetch(`${BASE_URL}/v1/policies?${query}`, {
    headers: { Authorization: authorization },
  });
  const body = await readJson(response);

  assert(response.status === 200, `published list expected 200, got ${response.status}`);
  assert(body.success === true, 'published list must use the success ApiResponse envelope');
  assert(Array.isArray(body.data?.items), 'published list data.items must be an array');
  for (const item of body.data.items) {
    assert(!('createdBy' in item), 'published item must not expose createdBy');
    assert(!('policyType' in item), 'published item must not expose policyType');
    assert(!('active' in item), 'published item must not expose active');
  }
  pass('authenticated published Policy list returned a sanitized paged response.');
}

async function testAuthFailure() {
  const response = await fetch(`${BASE_URL}/v1/policies`);
  const body = await readJson(response);

  assert(response.status === 401, `missing-token case expected 401, got ${response.status}`);
  assert(body.success === false, 'missing-token case must use the error ApiResponse envelope');
  pass('anonymous published Policy read returned 401.');
}

async function testValidationFailure(authorization) {
  const response = await fetch(`${BASE_URL}/v1/policies?operatorId=not-a-uuid`, {
    headers: { Authorization: authorization },
  });
  const body = await readJson(response);

  assert(response.status === 422, `invalid operatorId expected 422, got ${response.status}`);
  assert(
    body.error?.code === 'VALIDATION_ERROR',
    'invalid operatorId must return VALIDATION_ERROR',
  );
  pass('invalid published Policy query returned 422 VALIDATION_ERROR.');
}

async function testPublishedDetail(authorization, policyId) {
  const response = await fetch(`${BASE_URL}/v1/policies/${policyId}`, {
    headers: { Authorization: authorization },
  });
  const body = await readJson(response);

  assert(response.status === 200, `published detail expected 200, got ${response.status}`);
  assert(body.success === true && body.data?.id === policyId, 'published detail has invalid data');
  assert(!('createdBy' in body.data), 'published detail must not expose createdBy');
  pass('published Policy detail returned a sanitized response.');
}

function resolveAuthorization() {
  if (!ACCESS_TOKEN) {
    fail('Set PASSENGER_TOKEN or ACCESS_TOKEN to a real Identity access token.');
  }
  return ACCESS_TOKEN.startsWith('Bearer ') ? ACCESS_TOKEN : `Bearer ${ACCESS_TOKEN}`;
}

async function readJson(response) {
  const text = await response.text();
  try {
    return JSON.parse(text);
  } catch {
    fail(`expected JSON response from ${response.url}, got: ${text.slice(0, 200)}`);
  }
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
