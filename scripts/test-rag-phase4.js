const DEFAULT_RAG_URL = 'http://localhost:3003';
require('dotenv').config();

const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const ADMIN_USER_ID = process.env.USER_ID || '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = '22222222-2222-2222-2222-222222222222';
const INGEST_TIMEOUT_MS = 120_000;
const INGEST_POLL_INTERVAL_MS = 2_000;

const baseUrl = (process.env.BASE_URL || process.env.RAG_URL || DEFAULT_RAG_URL).replace(/\/$/, '');
const internalJwtSecret = process.env.INTERNAL_JWT_SECRET;

async function main() {
  if (!internalJwtSecret) {
    throw new Error('INTERNAL_JWT_SECRET is required to sign internal test JWTs');
  }
  if (!process.env.DATABASE_URL) {
    throw new Error('DATABASE_URL is required to verify RAG ingest database state');
  }

  const { PrismaClient } = require('../apps/rag/src/generated/rag-prisma-client');
  const prisma = new PrismaClient();

  try {
    const adminToken = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');
    const passengerToken = await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER');

    await assertMissingAuth();
    await assertPermissionFail(passengerToken);
    await assertValidationFail(adminToken);
    const documentId = await assertCreateDocument(adminToken);
    await assertApproveDocument(adminToken, documentId);
    const chunkCount = await waitForIngestComplete(prisma, documentId);
    await assertDuplicateWorkerPassDoesNotDuplicate(prisma, documentId, chunkCount);

    console.log('PASS: RAG Phase 4 TXT/MARKDOWN ingest smoke test completed');
  } finally {
    await prisma.$disconnect();
  }
}

async function assertMissingAuth() {
  const response = await fetch(`${baseUrl}/v1/rag/documents`, {
    method: 'POST',
    body: makeDocumentForm('faq-phase4.txt', 'text/plain'),
  });
  const body = await readJson(response);
  assert(response.status === 401, `Expected 401 for missing auth, got ${response.status}`);
  assertErrorEnvelope(body);
  console.log('PASS: missing internal JWT returns 401 envelope');
}

async function assertPermissionFail(passengerToken) {
  const response = await fetch(`${baseUrl}/v1/rag/documents`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': passengerToken },
    body: makeDocumentForm('faq-phase4.txt', 'text/plain'),
  });
  const body = await readJson(response);
  assert(response.status === 403, `Expected 403 for non-admin, got ${response.status}`);
  assertErrorEnvelope(body);
  console.log('PASS: non-admin caller returns 403 envelope');
}

async function assertValidationFail(adminToken) {
  const response = await fetch(`${baseUrl}/v1/rag/documents`, {
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
  const response = await fetch(`${baseUrl}/v1/rag/documents`, {
    method: 'POST',
    headers: { 'X-Internal-Auth': adminToken },
    body: makeDocumentForm(`faq-phase4-${Date.now()}.md`, 'text/markdown'),
  });
  const body = await readJson(response);
  assert(response.status === 201, `Expected 201 for document upload, got ${response.status}`);
  assertSuccessEnvelope(body);
  assert(body.data && typeof body.data.id === 'string', 'Create response must include data.id');
  console.log(`PASS: created document ${body.data.id}`);
  return body.data.id;
}

async function assertApproveDocument(adminToken, documentId) {
  const response = await fetch(`${baseUrl}/v1/rag/documents/${documentId}/approve`, {
    method: 'PUT',
    headers: { 'X-Internal-Auth': adminToken },
  });
  const body = await readJson(response);
  assert(response.status === 200, `Expected 200 for approve, got ${response.status}`);
  assertSuccessEnvelope(body);
  assert(body.data.status === 'APPROVED', 'Approve response must return APPROVED status');
  console.log(`PASS: approved document ${documentId}`);
}

async function waitForIngestComplete(prisma, documentId) {
  const deadline = Date.now() + INGEST_TIMEOUT_MS;
  let lastStatus = 'UNKNOWN';
  let lastError = null;

  while (Date.now() < deadline) {
    const document = await prisma.knowledgeDocument.findUnique({
      where: { id: documentId },
    });
    assert(document, `Document ${documentId} must exist`);
    lastStatus = document.ingestStatus;
    lastError = document.ingestError;

    if (document.ingestStatus === 'COMPLETED') {
      const chunkCount = await prisma.knowledgeChunk.count({ where: { documentId } });
      assert(chunkCount > 0, 'Completed ingest must create at least one chunk');
      assert(document.chunkCount === chunkCount, 'Document chunkCount must match chunks table');
      console.log(`PASS: ingest completed with ${chunkCount} chunks`);
      return chunkCount;
    }

    if (document.ingestStatus === 'FAILED') {
      throw new Error(`Ingest failed for ${documentId}: ${lastError || 'unknown error'}`);
    }

    await sleep(INGEST_POLL_INTERVAL_MS);
  }

  throw new Error(
    `Timed out waiting for ingest completion; last status=${lastStatus}, error=${lastError || 'none'}`,
  );
}

async function assertDuplicateWorkerPassDoesNotDuplicate(prisma, documentId, expectedChunkCount) {
  await sleep(INGEST_POLL_INTERVAL_MS * 2);
  const chunkCount = await prisma.knowledgeChunk.count({ where: { documentId } });
  assert(
    chunkCount === expectedChunkCount,
    `Duplicate ingest guard expected ${expectedChunkCount} chunks, got ${chunkCount}`,
  );
  console.log('PASS: duplicate worker pass did not duplicate chunks');
}

function makeDocumentForm(fileName, mimeType) {
  const form = new FormData();
  form.set(
    'file',
    new Blob(
      [
        '# Hỗ trợ hành khách\n',
        'VietRide hỗ trợ đặt vé, hủy vé và hoàn tiền theo chính sách được công bố.\n\n',
        '# Hành lý\n',
        'Hành khách cần kiểm tra quy định hành lý trước khi khởi hành.',
      ],
      { type: mimeType },
    ),
    fileName,
  );
  form.set('title', `RAG Phase 4 smoke test document ${Date.now()}`);
  form.set('description', 'Created by RAG Phase 4 smoke script');
  form.set('accessLevel', 'PUBLIC');
  form.set('category', 'CUSTOMER_SUPPORT');
  form.set('documentType', 'FAQ');
  form.set('audienceRoles', 'PASSENGER');
  form.set('language', 'vi');
  return form;
}

async function signInternalJwt(sub, role) {
  const { SignJWT } = await import('jose');
  const token = await new SignJWT({ sub, role, reqId: 'rag-phase4-smoke' })
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

function sleep(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

main().catch((error) => {
  console.error(`FAIL: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
