const { randomUUID } = require('node:crypto');

const DEFAULT_GATEWAY_URL = 'http://localhost:3000';
const baseUrl = (process.env.BASE_URL || process.env.GATEWAY_URL || DEFAULT_GATEWAY_URL).replace(
  /\/$/,
  '',
);
const systemAdminToken = process.env.SYSTEM_ADMIN_TOKEN || process.env.ACCESS_TOKEN;
const operatorAdminToken = process.env.OPERATOR_ADMIN_TOKEN || process.env.OPERATOR_TOKEN;

async function main() {
  assert(systemAdminToken, 'SYSTEM_ADMIN_TOKEN or ACCESS_TOKEN is required');
  assert(operatorAdminToken, 'OPERATOR_ADMIN_TOKEN or OPERATOR_TOKEN is required');

  await assertMissingAuth();
  await assertValidationFailure(systemAdminToken);
  await exercisePolicyScope('admin', systemAdminToken, 'FOR_OPERATOR');
  await exercisePolicyScope('operator', operatorAdminToken, 'FOR_USER');

  console.log('PASS | RAG UI-06 Gateway Policy smoke test completed');
}

async function assertMissingAuth() {
  const response = await request('/v1/admin/policies');
  assert(response.status === 401, `Missing auth expected 401, got ${response.status}`);
  assertErrorEnvelope(response.body, 'AUTH_TOKEN_INVALID');
  console.log('PASS | missing user token returns 401 envelope');
}

async function assertValidationFailure(token) {
  const response = await request('/v1/admin/policies', {
    method: 'POST',
    token,
    idempotencyKey: randomUUID(),
    body: {
      title: 'UI-06 invalid payload',
      description: 'Content is intentionally omitted',
      policyType: 'FOR_OPERATOR',
      category: 'UI06_SMOKE',
      active: true,
    },
  });
  assert(response.status === 422, `Invalid Policy expected 422, got ${response.status}`);
  assertErrorEnvelope(response.body, 'VALIDATION_ERROR');
  console.log('PASS | invalid Policy returns 422 VALIDATION_ERROR');
}

async function exercisePolicyScope(scope, token, policyType) {
  const collectionPath = `/v1/${scope}/policies`;
  const title = `UI-06 ${scope} smoke ${Date.now()}`;
  let policyId;
  let deleted = false;

  try {
    const createKey = randomUUID();
    const createOptions = {
      method: 'POST',
      token,
      idempotencyKey: createKey,
      body: {
        title,
        description: `Created by the UI-06 ${scope} real-stack smoke script`,
        content: 'Initial smoke-test content',
        policyType,
        category: 'UI06_SMOKE',
        active: true,
      },
    };
    const created = await request(collectionPath, createOptions);
    assert(created.status === 201, `${scope} create expected 201, got ${created.status}`);
    assertSuccessEnvelope(created.body);
    policyId = readPolicy(created.body).id;
    assert(readPolicy(created.body).version === 1, `${scope} create must start at version 1`);

    const replay = await request(collectionPath, createOptions);
    assert(replay.status === 201, `${scope} create replay expected 201, got ${replay.status}`);
    assert(readPolicy(replay.body).id === policyId, `${scope} replay created a different Policy`);

    const list = await request(
      `${collectionPath}?search=${encodeURIComponent(title)}&page=1&pageSize=20&sortBy=updatedAt&sortDir=desc`,
      { token },
    );
    assert(list.status === 200, `${scope} list expected 200, got ${list.status}`);
    assertSuccessEnvelope(list.body);
    assert(
      Array.isArray(list.body.data?.items) &&
        list.body.data.items.some((item) => item.id === policyId),
      `${scope} list did not return the created Policy`,
    );

    const detailPath = `${collectionPath}/${policyId}`;
    const detail = await request(detailPath, { token });
    assert(detail.status === 200, `${scope} detail expected 200, got ${detail.status}`);
    assert(readPolicy(detail.body).id === policyId, `${scope} detail returned the wrong Policy`);

    const updateKey = randomUUID();
    const updateOptions = {
      method: 'PATCH',
      token,
      idempotencyKey: updateKey,
      body: { version: 1, content: 'Updated smoke-test content', active: false },
    };
    const updated = await request(detailPath, updateOptions);
    assert(updated.status === 200, `${scope} update expected 200, got ${updated.status}`);
    assertSuccessEnvelope(updated.body);
    assert(
      readPolicy(updated.body).version === 2,
      `${scope} content update must increment version`,
    );
    assert(readPolicy(updated.body).active === false, `${scope} update must deactivate the Policy`);

    const updateReplay = await request(detailPath, updateOptions);
    assert(updateReplay.status === 200, `${scope} update replay expected 200`);
    assert(readPolicy(updateReplay.body).version === 2, `${scope} update replay mutated twice`);

    const deleteKey = randomUUID();
    const deleteOptions = { method: 'DELETE', token, idempotencyKey: deleteKey };
    const removed = await request(detailPath, deleteOptions);
    assert(removed.status === 200, `${scope} delete expected 200, got ${removed.status}`);
    assertSuccessEnvelope(removed.body);
    deleted = true;

    const deleteReplay = await request(detailPath, deleteOptions);
    assert(deleteReplay.status === 200, `${scope} delete replay expected 200`);

    const missing = await request(detailPath, { token });
    assert(missing.status === 404, `${scope} deleted detail expected 404, got ${missing.status}`);
    assertErrorEnvelope(missing.body, 'POLICY_NOT_FOUND');
    console.log(`PASS | ${scope} Policy CRUD, replay, pagination and soft-delete`);
  } finally {
    if (policyId && !deleted) {
      const cleanup = await request(`${collectionPath}/${policyId}`, {
        method: 'DELETE',
        token,
        idempotencyKey: randomUUID(),
      });
      if (cleanup.status !== 200 && cleanup.status !== 404) {
        console.warn(`WARN | cleanup ${scope} Policy ${policyId} returned ${cleanup.status}`);
      }
    }
  }
}

async function request(path, options = {}) {
  const headers = { Accept: 'application/json' };
  if (options.token) headers.Authorization = `Bearer ${options.token}`;
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';

  const response = await fetch(`${baseUrl}${path}`, {
    method: options.method || 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });
  const text = await response.text();
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    throw new Error(`${options.method || 'GET'} ${path} returned non-JSON: ${text}`);
  }
  return { status: response.status, body };
}

function readPolicy(envelope) {
  assertSuccessEnvelope(envelope);
  assert(
    envelope.data && typeof envelope.data.id === 'string',
    'Policy envelope must include data.id',
  );
  return envelope.data;
}

function assertSuccessEnvelope(body) {
  assert(body && body.success === true, 'Expected success envelope');
  assert(typeof body.statusCode === 'number', 'Success envelope must include statusCode');
  assert(
    body.meta && typeof body.meta.timestamp === 'string',
    'Envelope must include meta.timestamp',
  );
}

function assertErrorEnvelope(body, expectedCode) {
  assert(body && body.success === false, 'Expected error envelope');
  assert(typeof body.statusCode === 'number', 'Error envelope must include statusCode');
  assert(body.error?.code === expectedCode, `Expected ${expectedCode}, got ${body.error?.code}`);
  assert(
    body.meta && typeof body.meta.timestamp === 'string',
    'Envelope must include meta.timestamp',
  );
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

main().catch((error) => {
  console.error(`FAIL | ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
