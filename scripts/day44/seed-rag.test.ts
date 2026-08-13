/// <reference types="node" />

const assert: typeof import('node:assert/strict') =
  require('node:assert/strict') as typeof import('node:assert/strict');
const fs = require('node:fs') as typeof import('node:fs');
const os = require('node:os') as typeof import('node:os');
const path = require('node:path') as typeof import('node:path');
const { describe, test } = require('node:test') as typeof import('node:test');

import { day44IdentityFixtureIds } from './seed-identity';
import {
  DAY44_RAG_FIXTURE_PATH,
  DAY44_RAG_PROVENANCE_PATH,
  Day44RagDocumentRow,
  RagAudienceRole,
  canAccessDay44RagDocument,
  planDay44RagFixture,
} from './seed-rag';

const SYSTEM_ADMIN_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_B_ID = day44IdentityFixtureIds.operators.B;
const ALL_ROLES: RagAudienceRole[] = [
  'PASSENGER',
  'DRIVER',
  'ASSISTANT',
  'OPERATOR_STAFF',
  'OPERATOR_ADMIN',
  'SYSTEM_ADMIN',
];
const OPERATOR_ROLES: RagAudienceRole[] = [
  'DRIVER',
  'ASSISTANT',
  'OPERATOR_STAFF',
  'OPERATOR_ADMIN',
];

function plan() {
  return planDay44RagFixture({
    startDate: '2026-08-25',
    bootstrapSystemAdminId: SYSTEM_ADMIN_ID,
    operatorAId: day44IdentityFixtureIds.operators.A,
  });
}

function documentByAccess(
  documents: ReadonlyArray<Day44RagDocumentRow>,
  accessLevel: Day44RagDocumentRow['accessLevel'],
): Day44RagDocumentRow {
  const document = documents.find((candidate) => candidate.accessLevel === accessLevel);
  assert.ok(document);
  return document;
}

describe('Day 44 offline RAG seed planner', () => {
  test('plans exactly three approved, completed, searchable one-chunk documents', () => {
    const result = plan();

    assert.equal(result.documents.length, 3);
    assert.equal(result.chunks.length, 3);
    assert.equal(result.embeddingModel, 'gemini-embedding-2-preview');
    assert.equal(result.embeddingDimensions, 2_048);
    assert.ok(
      result.documents.every(
        (document) =>
          document.status === 'APPROVED' &&
          document.ingestStatus === 'COMPLETED' &&
          document.chunkCount === 1 &&
          document.approvedAt === '2026-08-23T03:00:00.000Z' &&
          document.ingestedAt === '2026-08-23T03:05:00.000Z' &&
          document.embeddingModel === result.embeddingModel &&
          document.embeddingDimensions === 2_048,
      ),
    );

    assert.equal(new Set(result.chunks.map(({ id }) => id)).size, 3);
    assert.equal(
      new Set(result.chunks.map(({ documentId, chunkIndex }) => `${documentId}:${chunkIndex}`))
        .size,
      3,
    );
    result.chunks.forEach((chunk) => {
      const document = result.documents.find(({ id }) => id === chunk.documentId);
      assert.ok(document);
      assert.equal(chunk.chunkIndex, 0);
      assert.ok(chunk.content.length > 0);
      assert.ok(chunk.tokenCount > 0);
      assert.equal(chunk.embedding.length, 2_048);
      assert.ok(chunk.embedding.every(Number.isFinite));
      assert.deepEqual(chunk.searchVector, {
        function: 'to_tsvector',
        configuration: 'simple',
        content: chunk.content,
      });
      assert.equal(chunk.documentTitle, document.title);
      assert.equal(chunk.documentType, document.documentType);
      assert.equal(chunk.operatorId, document.operatorId);
      assert.equal(document.fileSize, Buffer.byteLength(chunk.content, 'utf8'));
    });
  });

  test('enforces exact PUBLIC, Operator A, and ADMIN role and tenant access', () => {
    const result = plan();
    const publicDocument = documentByAccess(result.documents, 'PUBLIC');
    const operatorDocument = documentByAccess(result.documents, 'OPERATOR');
    const adminDocument = documentByAccess(result.documents, 'ADMIN');

    assert.deepEqual(publicDocument.audienceRoles, ALL_ROLES);
    assert.ok(ALL_ROLES.every((role) => canAccessDay44RagDocument(publicDocument, role, null)));

    assert.deepEqual(operatorDocument.audienceRoles, [...OPERATOR_ROLES, 'SYSTEM_ADMIN']);
    assert.equal(operatorDocument.operatorId, day44IdentityFixtureIds.operators.A);
    assert.ok(
      OPERATOR_ROLES.every((role) =>
        canAccessDay44RagDocument(operatorDocument, role, day44IdentityFixtureIds.operators.A),
      ),
    );
    assert.equal(canAccessDay44RagDocument(operatorDocument, 'PASSENGER', null), false);
    assert.equal(canAccessDay44RagDocument(operatorDocument, 'SYSTEM_ADMIN', null), true);
    assert.equal(canAccessDay44RagDocument(operatorDocument, 'SYSTEM_ADMIN', OPERATOR_B_ID), true);
    assert.ok(
      OPERATOR_ROLES.every(
        (role) => !canAccessDay44RagDocument(operatorDocument, role, OPERATOR_B_ID),
      ),
    );
    assert.ok(
      OPERATOR_ROLES.every((role) => !canAccessDay44RagDocument(operatorDocument, role, null)),
    );

    assert.deepEqual(adminDocument.audienceRoles, ['SYSTEM_ADMIN']);
    assert.equal(canAccessDay44RagDocument(adminDocument, 'SYSTEM_ADMIN', null), true);
    assert.ok(
      ALL_ROLES.filter((role) => role !== 'SYSTEM_ADMIN').every(
        (role) => !canAccessDay44RagDocument(adminDocument, role, null),
      ),
    );
  });

  test('rejects attestation drift before logical references can produce a write plan', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'day44-rag-seed-'));
    const fixturePath = path.join(root, 'fixture.json');
    try {
      const driftedFixture = JSON.parse(fs.readFileSync(DAY44_RAG_FIXTURE_PATH, 'utf8')) as {
        dimension: number;
      };
      driftedFixture.dimension = 1_536;
      fs.writeFileSync(fixturePath, `${JSON.stringify(driftedFixture)}\n`, 'utf8');

      assert.throws(
        () =>
          planDay44RagFixture({
            startDate: 'invalid-date',
            bootstrapSystemAdminId: 'invalid-id',
            operatorAId: OPERATOR_B_ID,
            fixturePath,
            provenancePath: DAY44_RAG_PROVENANCE_PATH,
          }),
        /RAG fixture metadata mismatch/,
      );
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
