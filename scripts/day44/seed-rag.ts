/// <reference types="node" />

const fs = require('node:fs') as typeof import('node:fs');
const path = require('node:path') as typeof import('node:path');

import { day44IdentityFixtureIds } from './seed-identity';
import {
  RAG_FIXTURE_DIMENSION,
  RAG_FIXTURE_DOCUMENT_PATHS,
  RAG_FIXTURE_MODEL,
  verifyRagFixture,
} from './generate-rag-fixture';

const VIETNAM_OFFSET_MILLISECONDS = 7 * 60 * 60 * 1_000;

export const DAY44_RAG_FIXTURE_PATH = 'scripts/day44/fixtures/rag-embeddings.json';
export const DAY44_RAG_PROVENANCE_PATH = 'scripts/day44/fixtures/rag-embeddings.provenance.json';

export type RagAccessLevel = 'PUBLIC' | 'OPERATOR' | 'ADMIN';
export type RagAudienceRole =
  | 'PASSENGER'
  | 'DRIVER'
  | 'ASSISTANT'
  | 'OPERATOR_STAFF'
  | 'OPERATOR_ADMIN'
  | 'SYSTEM_ADMIN';

export interface Day44RagDocumentRow {
  id: string;
  title: string;
  description: null;
  storageProvider: 'CLOUDINARY';
  storagePath: string;
  fileName: string;
  mimeType: 'text/plain';
  fileSize: number;
  fileType: 'TXT';
  accessLevel: RagAccessLevel;
  category: 'CUSTOMER_SUPPORT' | 'OPERATOR_POLICY' | 'PLATFORM_ADMIN';
  documentType: 'GUIDE' | 'POLICY' | 'SOP';
  audienceRoles: ReadonlyArray<RagAudienceRole>;
  language: 'vi';
  operatorId: string | null;
  status: 'APPROVED';
  ingestStatus: 'COMPLETED';
  ingestError: null;
  ingestedAt: string;
  chunkCount: 1;
  embeddingModel: typeof RAG_FIXTURE_MODEL;
  embeddingDimensions: typeof RAG_FIXTURE_DIMENSION;
  uploadedByUserId: string;
  approvedByUserId: string;
  approvedAt: string;
  archivedAt: null;
  createdAt: string;
  updatedAt: string;
}

export interface Day44RagChunkRow {
  id: string;
  documentId: string;
  operatorId: string | null;
  documentTitle: string;
  sectionHeader: null;
  documentType: Day44RagDocumentRow['documentType'];
  chunkIndex: 0;
  content: string;
  tokenCount: number;
  embedding: ReadonlyArray<number>;
  searchVector: {
    function: 'to_tsvector';
    configuration: 'simple';
    content: string;
  };
  createdAt: string;
}

export interface Day44RagPlannerInput {
  startDate: string;
  bootstrapSystemAdminId: string;
  operatorAId: string;
  fixturePath?: string;
  provenancePath?: string;
  documentPaths?: ReadonlyArray<string>;
}

export interface Day44RagFixturePlan {
  schemaVersion: 1;
  namespace: 'day44-v1';
  timezone: 'Asia/Ho_Chi_Minh';
  startDate: string;
  embeddingModel: typeof RAG_FIXTURE_MODEL;
  embeddingDimensions: typeof RAG_FIXTURE_DIMENSION;
  documents: ReadonlyArray<Day44RagDocumentRow>;
  chunks: ReadonlyArray<Day44RagChunkRow>;
}

interface AttestedFixture {
  model: string;
  dimension: number;
  documents: Array<{
    path: string;
    chunks: Array<{ index: number; embedding: number[] }>;
  }>;
}

interface RagDefinition {
  documentId: string;
  chunkId: string;
  title: string;
  storagePath: string;
  accessLevel: RagAccessLevel;
  category: Day44RagDocumentRow['category'];
  documentType: Day44RagDocumentRow['documentType'];
  operatorId: string | null;
  audienceRoles: ReadonlyArray<RagAudienceRole>;
}

const ALL_ROLES: ReadonlyArray<RagAudienceRole> = [
  'PASSENGER',
  'DRIVER',
  'ASSISTANT',
  'OPERATOR_STAFF',
  'OPERATOR_ADMIN',
  'SYSTEM_ADMIN',
];
const OPERATOR_ROLES: ReadonlyArray<RagAudienceRole> = [
  'DRIVER',
  'ASSISTANT',
  'OPERATOR_STAFF',
  'OPERATOR_ADMIN',
];
const OPERATOR_AUDIENCE_ROLES: ReadonlyArray<RagAudienceRole> = [...OPERATOR_ROLES, 'SYSTEM_ADMIN'];

function ictInstant(startDate: string, dayOffset: number, hour = 0, minute = 0): Date {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(startDate);
  if (!match) throw new Error('Day 44 RAG startDate must use YYYY-MM-DD');
  const [, yearText, monthText, dayText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const instant = new Date(
    Date.UTC(year, month - 1, day + dayOffset, hour, minute) - VIETNAM_OFFSET_MILLISECONDS,
  );
  const normalized = new Date(Date.UTC(year, month - 1, day));
  if (
    normalized.getUTCFullYear() !== year ||
    normalized.getUTCMonth() !== month - 1 ||
    normalized.getUTCDate() !== day
  ) {
    throw new Error('Day 44 RAG startDate is invalid');
  }
  return instant;
}

function assertLogicalReferences(input: Day44RagPlannerInput): void {
  if (!/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(input.bootstrapSystemAdminId)) {
    throw new Error('Day 44 RAG fixture requires the bootstrap System Admin ID');
  }
  if (input.operatorAId !== day44IdentityFixtureIds.operators.A) {
    throw new Error('Day 44 RAG Operator A logical-ID mismatch');
  }
}

function definitions(operatorAId: string): ReadonlyArray<RagDefinition> {
  return [
    {
      documentId: '53e1c002-7790-5b42-b460-18abed3e06a7',
      chunkId: '6622f148-dd44-5d52-8e07-9ccceb2d1aa4',
      title: 'Day44 Public Passenger Guide',
      storagePath: 'day44-v1/rag/public-passenger-guide.txt',
      accessLevel: 'PUBLIC',
      category: 'CUSTOMER_SUPPORT',
      documentType: 'GUIDE',
      operatorId: null,
      audienceRoles: ALL_ROLES,
    },
    {
      documentId: 'd71264b6-12ed-5dfe-ada4-bfc36d2d5ff2',
      chunkId: 'f3eba0a6-e2fe-5d57-a57b-780b93d4003d',
      title: 'Day44 Operator A Policy',
      storagePath: 'day44-v1/rag/operator-a-policy.txt',
      accessLevel: 'OPERATOR',
      category: 'OPERATOR_POLICY',
      documentType: 'POLICY',
      operatorId: operatorAId,
      audienceRoles: OPERATOR_AUDIENCE_ROLES,
    },
    {
      documentId: 'd1fc37a4-8c62-5950-9866-acfffb7a8fbd',
      chunkId: '998c813b-aafc-5d33-887f-d2c04364660a',
      title: 'Day44 System Admin Runbook',
      storagePath: 'day44-v1/rag/system-admin-runbook.txt',
      accessLevel: 'ADMIN',
      category: 'PLATFORM_ADMIN',
      documentType: 'SOP',
      operatorId: null,
      audienceRoles: ['SYSTEM_ADMIN'],
    },
  ];
}

export function canAccessDay44RagDocument(
  document: Pick<Day44RagDocumentRow, 'accessLevel' | 'audienceRoles' | 'operatorId'>,
  role: RagAudienceRole,
  operatorId: string | null,
): boolean {
  if (role === 'SYSTEM_ADMIN') return true;
  if (!document.audienceRoles.includes(role)) return false;
  if (document.accessLevel === 'PUBLIC') return document.operatorId === null;
  if (document.accessLevel === 'ADMIN') return false;
  return document.operatorId !== null && document.operatorId === operatorId;
}

export function planDay44RagFixture(input: Day44RagPlannerInput): Day44RagFixturePlan {
  const fixturePath = input.fixturePath ?? DAY44_RAG_FIXTURE_PATH;
  const provenancePath = input.provenancePath ?? DAY44_RAG_PROVENANCE_PATH;
  const documentPaths = [...(input.documentPaths ?? RAG_FIXTURE_DOCUMENT_PATHS)];

  // This must remain the first fixture operation: no plan (and therefore no DB write) is
  // possible until the committed content, fixture and provenance attestations all pass.
  verifyRagFixture({ fixturePath, provenancePath, documentPaths });

  const fixture = JSON.parse(fs.readFileSync(fixturePath, 'utf8')) as AttestedFixture;
  if (fixture.model !== RAG_FIXTURE_MODEL || fixture.dimension !== RAG_FIXTURE_DIMENSION) {
    throw new Error('Day 44 RAG attested model or dimension mismatch');
  }
  assertLogicalReferences(input);

  const createdAt = ictInstant(input.startDate, -2).toISOString();
  const approvedAt = ictInstant(input.startDate, -2, 10).toISOString();
  const ingestedAt = ictInstant(input.startDate, -2, 10, 5).toISOString();
  const rows = definitions(input.operatorAId).map((definition, index) => {
    const documentPath = documentPaths[index];
    const content = fs.readFileSync(documentPath, 'utf8');
    const embedding = fixture.documents[index]?.chunks[0]?.embedding;
    if (
      fixture.documents[index]?.path !== documentPath ||
      fixture.documents[index]?.chunks[0]?.index !== 0 ||
      !Array.isArray(embedding) ||
      embedding.length !== RAG_FIXTURE_DIMENSION ||
      embedding.some((value) => !Number.isFinite(value)) ||
      content.length === 0
    ) {
      throw new Error('Day 44 RAG attested document or chunk mismatch');
    }
    const document: Day44RagDocumentRow = {
      id: definition.documentId,
      title: definition.title,
      description: null,
      storageProvider: 'CLOUDINARY',
      storagePath: definition.storagePath,
      fileName: path.basename(documentPath),
      mimeType: 'text/plain',
      fileSize: Buffer.byteLength(content, 'utf8'),
      fileType: 'TXT',
      accessLevel: definition.accessLevel,
      category: definition.category,
      documentType: definition.documentType,
      audienceRoles: definition.audienceRoles,
      language: 'vi',
      operatorId: definition.operatorId,
      status: 'APPROVED',
      ingestStatus: 'COMPLETED',
      ingestError: null,
      ingestedAt,
      chunkCount: 1,
      embeddingModel: RAG_FIXTURE_MODEL,
      embeddingDimensions: RAG_FIXTURE_DIMENSION,
      uploadedByUserId: input.bootstrapSystemAdminId,
      approvedByUserId: input.bootstrapSystemAdminId,
      approvedAt,
      archivedAt: null,
      createdAt,
      updatedAt: createdAt,
    };
    const chunk: Day44RagChunkRow = {
      id: definition.chunkId,
      documentId: definition.documentId,
      operatorId: definition.operatorId,
      documentTitle: definition.title,
      sectionHeader: null,
      documentType: definition.documentType,
      chunkIndex: 0,
      content,
      tokenCount: content.trim().split(/\s+/u).length,
      embedding,
      searchVector: { function: 'to_tsvector', configuration: 'simple', content },
      createdAt,
    };
    return { document, chunk };
  });

  return {
    schemaVersion: 1,
    namespace: 'day44-v1',
    timezone: 'Asia/Ho_Chi_Minh',
    startDate: input.startDate,
    embeddingModel: RAG_FIXTURE_MODEL,
    embeddingDimensions: RAG_FIXTURE_DIMENSION,
    documents: rows.map(({ document }) => document),
    chunks: rows.map(({ chunk }) => chunk),
  };
}
