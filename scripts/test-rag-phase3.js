const DEFAULT_RAG_URL = 'http://localhost:3003';
require('dotenv').config();

const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const ADMIN_USER_ID = '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = '22222222-2222-2222-2222-222222222222';

const baseUrl = (process.env.BASE_URL || process.env.RAG_URL || DEFAULT_RAG_URL).replace(/\/$/, '');
const internalJwtSecret = process.env.INTERNAL_JWT_SECRET;

async function main() {
  if (!internalJwtSecret) {
    throw new Error('INTERNAL_JWT_SECRET is required to sign internal test JWTs');
  }

  const adminToken = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');
  const passengerToken = await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER');

  await assertMissingAuth();
  await assertPermissionFail(passengerToken);
  await assertValidationFail(adminToken);
  const documentId = await assertCreateDocument(adminToken);
  await assertApproveDocument(adminToken, documentId);

  console.log('PASS: RAG Phase 3 document upload and approve smoke test completed');
}

async function assertMissingAuth() {
  const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
    method: 'POST',
    body: makeDocumentForm('faq.txt', 'text/plain'),
  });
  const body = await readJson(response);
  assert(response.status === 401, `Expected 401 for missing auth, got ${response.status}`);
  assertErrorEnvelope(body);
  console.log('PASS: missing internal JWT returns 401 envelope');
}

async function assertPermissionFail(passengerToken) {
  const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': passengerToken },
    body: makeDocumentForm('faq.txt', 'text/plain'),
  });
  const body = await readJson(response);
  assert(response.status === 403, `Expected 403 for non-admin, got ${response.status}`);
  assertErrorEnvelope(body);
  console.log('PASS: non-admin caller returns 403 envelope');
}

async function assertValidationFail(adminToken) {
  const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': adminToken },
    body: makeDocumentForm('bad.pdf', 'application/pdf'),
  });
  const body = await readJson(response);
  assert(response.status === 400, `Expected 400 for invalid file, got ${response.status}`);
  assertErrorEnvelope(body);
  console.log('PASS: invalid file returns 400 envelope');
}

async function assertCreateDocument(adminToken) {
  const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': adminToken },
    body: makeDocumentForm('faq-phase3.txt', 'text/plain'),
  });
  const body = await readJson(response);
  assert(response.status === 201, `Expected 201 for document upload, got ${response.status}`);
  assertSuccessEnvelope(body);
  assert(body.data && typeof body.data.id === 'string', 'Create response must include data.id');
  assert(typeof body.data.previewUrl === 'string', 'Create response must include data.previewUrl');
  console.log(`PASS: created document ${body.data.id}`);
  return body.data.id;
}

async function assertApproveDocument(adminToken, documentId) {
  const response = await fetch(`${baseUrl}/api/v1/rag/documents/${documentId}/approve`, {
    method: 'PUT',
    headers: { 'X-Internal-Auth': adminToken },
  });
  const body = await readJson(response);
  assert(response.status === 200, `Expected 200 for approve, got ${response.status}`);
  assertSuccessEnvelope(body);
  assert(body.data.status === 'APPROVED', 'Approve response must return APPROVED status');
  console.log(`PASS: approved document ${documentId}`);
}

function makeDocumentForm(fileName, mimeType) {
  const form = new FormData();
  form.set('file', new Blob(['RAG Phase 3 smoke test content'], { type: mimeType }), fileName);
  form.set('title', `RAG Phase 3 smoke test document ${Date.now()}`);
  form.set('description', 'Created by RAG Phase 3 smoke script');
  form.set('accessLevel', 'PUBLIC');
  form.set('category', 'CUSTOMER_SUPPORT');
  form.set('documentType', 'FAQ');
  form.set('audienceRoles', 'PASSENGER');
  form.set('language', 'vi');
  return form;
}

async function signInternalJwt(sub, role) {
  const { SignJWT } = await import('jose');
  const token = await new SignJWT({ sub, role, reqId: 'rag-phase3-smoke' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(INTERNAL_JWT_ISSUER)
    .setAudience(INTERNAL_JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(internalJwtSecret));

  return `Bearer ${token}`;
}

async function readJson(response) {
  const text = await response.text();
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`Response is not JSON: ${text}`);
  }
}

function assertSuccessEnvelope(body) {
  assert(body && body.success === true, 'Expected success envelope');
  assert(typeof body.statusCode === 'number', 'Envelope must include statusCode');
  assert(body.meta && typeof body.meta.timestamp === 'string', 'Envelope must include meta.timestamp');
}

function assertErrorEnvelope(body) {
  assert(body && body.success === false, 'Expected error envelope');
  assert(typeof body.statusCode === 'number', 'Error envelope must include statusCode');
  assert(body.error && typeof body.error.code === 'string', 'Error envelope must include error.code');
  assert(body.meta && typeof body.meta.timestamp === 'string', 'Envelope must include meta.timestamp');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

main().catch((error) => {
  console.error(`FAIL: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
