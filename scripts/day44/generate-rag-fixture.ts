/// <reference types="node" />

const crypto = require('node:crypto') as typeof import('node:crypto');
const fs = require('node:fs') as typeof import('node:fs');
const path = require('node:path') as typeof import('node:path');

export const RAG_FIXTURE_MODEL = 'nvidia/llama-nemotron-embed-vl-1b-v2:free';
export const RAG_FIXTURE_DIMENSION = 2_048;
export const RAG_FIXTURE_ENDPOINT = 'https://openrouter.ai/api/v1/embeddings';
export const RAG_FIXTURE_DOCUMENT_PATHS = [
  'docs/rag/vietride-public-demo-knowledge-base.txt',
  'docs/rag/vietride-operator-demo-knowledge-base.txt',
  'docs/rag/vietride-admin-demo-knowledge-base.txt',
] as const;

interface FixtureDocument {
  path: string;
  chunks: Array<{ index: 0; embedding: number[] }>;
}

interface RagFixture {
  schemaVersion: 1;
  generatorVersion: 1;
  model: typeof RAG_FIXTURE_MODEL;
  dimension: typeof RAG_FIXTURE_DIMENSION;
  documents: FixtureDocument[];
}

interface ProvenanceDocument {
  path: string;
  contentSha256: string;
}

interface RagFixtureProvenance {
  schemaVersion: 1;
  generatorVersion: 1;
  provider: 'openrouter';
  endpoint: typeof RAG_FIXTURE_ENDPOINT;
  model: typeof RAG_FIXTURE_MODEL;
  dimension: typeof RAG_FIXTURE_DIMENSION;
  documents: ProvenanceDocument[];
  fixtureSha256: string;
}

interface GenerateOptions {
  apiKey: string | undefined;
  baseUrl: string;
  model: string;
  fixturePath: string;
  provenancePath: string;
  documentPaths: string[];
  fetchImpl?: typeof fetch;
}

interface VerifyOptions {
  fixturePath: string;
  provenancePath: string;
  documentPaths: string[];
}

function sha256(value: string | Buffer): string {
  return crypto.createHash('sha256').update(value).digest('hex');
}

function serialize(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

function formatEmbedding(embedding: number[]): string {
  const indent = ' '.repeat(12);
  const lines: string[] = [];
  let line = indent;
  for (let index = 0; index < embedding.length; index += 1) {
    const token = `${JSON.stringify(embedding[index])}${index + 1 < embedding.length ? ',' : ''}`;
    const candidate = line === indent ? `${line}${token}` : `${line} ${token}`;
    if (candidate.length > 100 && line !== indent) {
      lines.push(line);
      line = `${indent}${token}`;
    } else {
      line = candidate;
    }
  }
  lines.push(line);
  return `[\n${lines.join('\n')}\n          ]`;
}

function serializeFixture(fixture: RagFixture): string {
  const markers = fixture.documents.map((_, index) => `__RAG_EMBEDDING_${index}__`);
  const serializable = {
    ...fixture,
    documents: fixture.documents.map((document, index) => ({
      ...document,
      chunks: [{ ...document.chunks[0], embedding: markers[index] }],
    })),
  };
  let text = JSON.stringify(serializable, null, 2);
  fixture.documents.forEach((document, index) => {
    text = text.replace(
      JSON.stringify(markers[index]),
      formatEmbedding(document.chunks[0].embedding),
    );
  });
  return `${text}\n`;
}

function assertCanonicalDocuments(documentPaths: string[]): void {
  if (documentPaths.length !== 3 || new Set(documentPaths).size !== 3) {
    throw new Error('RAG fixture requires exactly three distinct documents');
  }
}

function assertCanonicalCliDocuments(documentPaths: string[]): void {
  if (
    documentPaths.length !== RAG_FIXTURE_DOCUMENT_PATHS.length ||
    documentPaths.some((documentPath, index) => documentPath !== RAG_FIXTURE_DOCUMENT_PATHS[index])
  ) {
    throw new Error('RAG fixture CLI requires the ordered canonical document list');
  }
}

function assertEmbedding(value: unknown): asserts value is number[] {
  if (
    !Array.isArray(value) ||
    value.length !== RAG_FIXTURE_DIMENSION ||
    value.some((item) => typeof item !== 'number' || !Number.isFinite(item))
  ) {
    throw new Error('RAG fixture provider returned an invalid embedding');
  }
}

function assertFixture(value: unknown): asserts value is RagFixture {
  const fixture = value as Partial<RagFixture>;
  if (
    fixture.schemaVersion !== 1 ||
    fixture.generatorVersion !== 1 ||
    fixture.model !== RAG_FIXTURE_MODEL ||
    fixture.dimension !== RAG_FIXTURE_DIMENSION ||
    !Array.isArray(fixture.documents) ||
    fixture.documents.length !== 3
  ) {
    throw new Error('RAG fixture metadata mismatch');
  }

  for (const document of fixture.documents) {
    if (
      typeof document.path !== 'string' ||
      document.chunks?.length !== 1 ||
      document.chunks[0]?.index !== 0
    ) {
      throw new Error('RAG fixture document shape mismatch');
    }
    assertEmbedding(document.chunks[0].embedding);
  }
}

function assertProvenance(value: unknown): asserts value is RagFixtureProvenance {
  const provenance = value as Partial<RagFixtureProvenance>;
  const hashPattern = /^[0-9a-f]{64}$/;
  if (
    provenance.schemaVersion !== 1 ||
    provenance.generatorVersion !== 1 ||
    provenance.provider !== 'openrouter' ||
    provenance.endpoint !== RAG_FIXTURE_ENDPOINT ||
    provenance.model !== RAG_FIXTURE_MODEL ||
    provenance.dimension !== RAG_FIXTURE_DIMENSION ||
    !Array.isArray(provenance.documents) ||
    provenance.documents.length !== 3 ||
    !hashPattern.test(provenance.fixtureSha256 ?? '') ||
    provenance.documents.some(
      (document) =>
        typeof document.path !== 'string' || !hashPattern.test(document.contentSha256 ?? ''),
    )
  ) {
    throw new Error('RAG fixture provenance mismatch');
  }
}

async function requestEmbedding(
  input: string,
  apiKey: string,
  endpoint: string,
  model: string,
  fetchImpl: typeof fetch,
): Promise<number[]> {
  try {
    const response = await fetchImpl(endpoint, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${apiKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ model, input }),
    });
    if (!response.ok) throw new Error('provider request failed');
    const body = (await response.json()) as { data?: Array<{ embedding?: unknown }> };
    const embedding = body.data?.[0]?.embedding;
    assertEmbedding(embedding);
    return embedding;
  } catch {
    throw new Error('RAG fixture generation failed');
  }
}

function writePairAtomically(
  fixturePath: string,
  fixtureText: string,
  provenancePath: string,
  provenanceText: string,
): void {
  const fixtureTemp = `${fixturePath}.${process.pid}.tmp`;
  const provenanceTemp = `${provenancePath}.${process.pid}.tmp`;
  fs.mkdirSync(path.dirname(fixturePath), { recursive: true });
  fs.mkdirSync(path.dirname(provenancePath), { recursive: true });

  try {
    fs.writeFileSync(fixtureTemp, fixtureText, { encoding: 'utf8', flag: 'wx' });
    fs.writeFileSync(provenanceTemp, provenanceText, { encoding: 'utf8', flag: 'wx' });
    fs.renameSync(fixtureTemp, fixturePath);
    try {
      fs.renameSync(provenanceTemp, provenancePath);
    } catch (error) {
      fs.rmSync(fixturePath, { force: true });
      throw error;
    }
  } finally {
    fs.rmSync(fixtureTemp, { force: true });
    fs.rmSync(provenanceTemp, { force: true });
  }
}

export async function generateRagFixture(options: GenerateOptions): Promise<void> {
  if (!options.apiKey?.trim()) throw new Error('RAG fixture generation requires an API key');
  if (fs.existsSync(options.fixturePath) || fs.existsSync(options.provenancePath)) {
    throw new Error('RAG fixture generation refuses existing output');
  }
  if (options.model !== RAG_FIXTURE_MODEL) throw new Error('RAG fixture model mismatch');
  const endpoint = `${options.baseUrl.replace(/\/$/u, '')}/embeddings`;
  if (endpoint !== RAG_FIXTURE_ENDPOINT) throw new Error('RAG fixture endpoint mismatch');
  assertCanonicalDocuments(options.documentPaths);

  const contents = options.documentPaths.map((documentPath) =>
    fs.readFileSync(documentPath, 'utf8'),
  );
  const embeddings: number[][] = [];
  for (const content of contents) {
    embeddings.push(
      await requestEmbedding(
        content,
        options.apiKey,
        endpoint,
        options.model,
        options.fetchImpl ?? fetch,
      ),
    );
  }

  const fixture: RagFixture = {
    schemaVersion: 1,
    generatorVersion: 1,
    model: RAG_FIXTURE_MODEL,
    dimension: RAG_FIXTURE_DIMENSION,
    documents: options.documentPaths.map((documentPath, index) => ({
      path: documentPath,
      chunks: [{ index: 0, embedding: embeddings[index] }],
    })),
  };
  const fixtureText = serializeFixture(fixture);
  const provenance: RagFixtureProvenance = {
    schemaVersion: 1,
    generatorVersion: 1,
    provider: 'openrouter',
    endpoint: RAG_FIXTURE_ENDPOINT,
    model: RAG_FIXTURE_MODEL,
    dimension: RAG_FIXTURE_DIMENSION,
    documents: options.documentPaths.map((documentPath, index) => ({
      path: documentPath,
      contentSha256: sha256(contents[index]),
    })),
    fixtureSha256: sha256(fixtureText),
  };

  writePairAtomically(
    options.fixturePath,
    fixtureText,
    options.provenancePath,
    serialize(provenance),
  );
}

export function verifyRagFixture(options: VerifyOptions): void {
  assertCanonicalDocuments(options.documentPaths);
  const fixtureText = fs.readFileSync(options.fixturePath, 'utf8');
  const fixture = JSON.parse(fixtureText) as unknown;
  const provenance = JSON.parse(fs.readFileSync(options.provenancePath, 'utf8')) as unknown;
  assertFixture(fixture);
  assertProvenance(provenance);

  if (
    serializeFixture(fixture) !== fixtureText ||
    provenance.fixtureSha256 !== sha256(fixtureText)
  ) {
    throw new Error('RAG fixture hash mismatch');
  }
  if (
    fixture.documents.some(
      (document, index) =>
        document.path !== options.documentPaths[index] ||
        provenance.documents[index]?.path !== options.documentPaths[index] ||
        provenance.documents[index]?.contentSha256 !==
          sha256(fs.readFileSync(options.documentPaths[index], 'utf8')),
    )
  ) {
    throw new Error('RAG fixture document provenance mismatch');
  }
}

function parseArguments(arguments_: string[]): Record<string, string | boolean> {
  const parsed: Record<string, string | boolean> = {};
  for (const argument of arguments_) {
    if (argument === '--generate') parsed.generate = true;
    else if (argument === '--verify') parsed.verify = true;
    else if (argument.startsWith('--')) {
      const separator = argument.indexOf('=');
      if (separator > 2) parsed[argument.slice(2, separator)] = argument.slice(separator + 1);
    }
  }
  return parsed;
}

function requiredArgument(args: Record<string, string | boolean>, name: string): string {
  const value = args[name];
  if (typeof value !== 'string' || value.length === 0) throw new Error(`Missing --${name}`);
  return value;
}

export async function main(arguments_: string[] = process.argv.slice(2)): Promise<void> {
  const args = parseArguments(arguments_);
  if (args.generate === args.verify) throw new Error('Select exactly one RAG fixture mode');
  const documentPaths = requiredArgument(args, 'documents').split(',');
  assertCanonicalCliDocuments(documentPaths);
  const fixturePath = requiredArgument(args, 'fixture');
  const provenancePath = requiredArgument(args, 'provenance');

  if (args.generate) {
    await generateRagFixture({
      apiKey: process.env.OPENROUTER_API_KEY,
      baseUrl: requiredArgument(args, 'base-url'),
      model: requiredArgument(args, 'model'),
      fixturePath,
      provenancePath,
      documentPaths,
    });
    process.stdout.write('RAG_FIXTURE_GENERATION=PASS\n');
    return;
  }

  verifyRagFixture({ fixturePath, provenancePath, documentPaths });
  process.stdout.write('RAG_FIXTURE_PROVENANCE=PASS\n');
}

if (require.main === module) {
  void main().catch(() => {
    process.stderr.write('RAG fixture command failed\n');
    process.exitCode = 1;
  });
}
