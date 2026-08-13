/// <reference types="node" />

const assert: typeof import('node:assert/strict') = require('node:assert/strict');
const fs = require('node:fs') as typeof import('node:fs');
const os = require('node:os') as typeof import('node:os');
const path = require('node:path') as typeof import('node:path');
const { describe, test } = require('node:test') as typeof import('node:test');
import {
  generateRagFixture,
  RAG_FIXTURE_DIMENSION,
  RAG_FIXTURE_DOCUMENT_PATHS,
  RAG_FIXTURE_MODEL,
  main,
  verifyRagFixture,
} from './generate-rag-fixture';

interface TestPaths {
  root: string;
  documents: string[];
  fixture: string;
  provenance: string;
}

function makePaths(): TestPaths {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'day44-rag-'));
  const documents = ['public.txt', 'operator.txt', 'admin.txt'].map((name, index) => {
    const documentPath = path.join(root, name);
    fs.writeFileSync(documentPath, `canonical document ${index}\n`, 'utf8');
    return documentPath;
  });
  return {
    root,
    documents,
    fixture: path.join(root, 'fixture.json'),
    provenance: path.join(root, 'provenance.json'),
  };
}

function responseFor(embedding: unknown): Response {
  return new Response(JSON.stringify({ data: [{ embedding }] }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function options(paths: TestPaths, fetchImpl: typeof fetch, apiKey = 'runtime-secret') {
  return {
    apiKey,
    baseUrl: 'https://api.shopaikey.com/v1',
    model: RAG_FIXTURE_MODEL,
    fixturePath: paths.fixture,
    provenancePath: paths.provenance,
    documentPaths: paths.documents,
    fetchImpl,
  };
}

describe('Day 44 RAG fixture generator', () => {
  test('missing key fails before provider call and writes nothing', async () => {
    const paths = makePaths();
    let calls = 0;
    try {
      await assert.rejects(
        generateRagFixture({
          ...options(paths, async () => {
            calls += 1;
            return responseFor([]);
          }),
          apiKey: undefined,
        }),
        /requires an API key/u,
      );
      assert.equal(calls, 0);
      assert.equal(fs.existsSync(paths.fixture), false);
      assert.equal(fs.existsSync(paths.provenance), false);
    } finally {
      fs.rmSync(paths.root, { recursive: true, force: true });
    }
  });

  test('malformed dimension and non-finite values write nothing', async () => {
    for (const embedding of [[0], [...Array(RAG_FIXTURE_DIMENSION - 1).fill(0), Number.NaN]]) {
      const paths = makePaths();
      try {
        await assert.rejects(
          generateRagFixture(options(paths, async () => responseFor(embedding))),
          /generation failed/u,
        );
        assert.equal(fs.existsSync(paths.fixture), false);
        assert.equal(fs.existsSync(paths.provenance), false);
      } finally {
        fs.rmSync(paths.root, { recursive: true, force: true });
      }
    }
  });

  test('serialization and provenance are deterministic and offline-verifiable', async () => {
    const paths = makePaths();
    const embedding = Array.from({ length: RAG_FIXTURE_DIMENSION }, (_, index) => index / 10_000);
    const provider = async () => responseFor(embedding);
    try {
      await generateRagFixture(options(paths, provider));
      const firstFixture = fs.readFileSync(paths.fixture);
      const firstProvenance = fs.readFileSync(paths.provenance);
      fs.rmSync(paths.fixture);
      fs.rmSync(paths.provenance);

      await generateRagFixture(options(paths, provider));
      assert.deepEqual(fs.readFileSync(paths.fixture), firstFixture);
      assert.deepEqual(fs.readFileSync(paths.provenance), firstProvenance);
      verifyRagFixture({
        fixturePath: paths.fixture,
        provenancePath: paths.provenance,
        documentPaths: paths.documents,
      });
    } finally {
      fs.rmSync(paths.root, { recursive: true, force: true });
    }
  });

  test('CLI rejects non-canonical document paths before provider call or write', async () => {
    const paths = makePaths();
    const originalFetch = globalThis.fetch;
    const originalKey = process.env.SHOPAIKEY_API_KEY;
    let providerCalls = 0;
    globalThis.fetch = async () => {
      providerCalls += 1;
      return responseFor(Array(RAG_FIXTURE_DIMENSION).fill(0));
    };
    process.env.SHOPAIKEY_API_KEY = 'runtime-only-test-key';

    try {
      await assert.rejects(
        main([
          '--generate',
          '--base-url=https://api.shopaikey.com/v1',
          `--model=${RAG_FIXTURE_MODEL}`,
          `--fixture=${paths.fixture}`,
          `--provenance=${paths.provenance}`,
          `--documents=${[...RAG_FIXTURE_DOCUMENT_PATHS].reverse().join(',')}`,
        ]),
        /ordered canonical document list/u,
      );
      assert.equal(providerCalls, 0);
      assert.equal(fs.existsSync(paths.fixture), false);
      assert.equal(fs.existsSync(paths.provenance), false);
    } finally {
      globalThis.fetch = originalFetch;
      if (originalKey === undefined) delete process.env.SHOPAIKEY_API_KEY;
      else process.env.SHOPAIKEY_API_KEY = originalKey;
      fs.rmSync(paths.root, { recursive: true, force: true });
    }
  });

  test('requests only canonical contents and errors redact keys and headers', async () => {
    const paths = makePaths();
    const secret = 'extremely-sensitive-runtime-key';
    const requests: Array<{ url: string; init: RequestInit | undefined }> = [];
    const embedding = Array(RAG_FIXTURE_DIMENSION).fill(0.25);
    try {
      await generateRagFixture(
        options(
          paths,
          async (input, init) => {
            requests.push({ url: String(input), init });
            return responseFor(embedding);
          },
          secret,
        ),
      );
      assert.equal(requests.length, 3);
      assert.deepEqual(
        requests.map((request) => JSON.parse(String(request.init?.body)).input),
        paths.documents.map((documentPath) => fs.readFileSync(documentPath, 'utf8')),
      );
      assert.ok(
        requests.every(
          (request) =>
            request.url === 'https://api.shopaikey.com/v1/embeddings' &&
            JSON.parse(String(request.init?.body)).model === RAG_FIXTURE_MODEL &&
            JSON.parse(String(request.init?.body)).encoding_format === 'float' &&
            JSON.parse(String(request.init?.body)).dimensions === RAG_FIXTURE_DIMENSION,
        ),
      );

      const failurePaths = makePaths();
      try {
        await assert.rejects(
          generateRagFixture(
            options(
              failurePaths,
              async () => {
                throw new Error(`Authorization: Bearer ${secret}; headers=private`);
              },
              secret,
            ),
          ),
          (error: Error) => {
            assert.equal(error.message, 'RAG fixture generation failed');
            assert.doesNotMatch(error.message, /Authorization|Bearer|headers|sensitive/u);
            return true;
          },
        );
      } finally {
        fs.rmSync(failurePaths.root, { recursive: true, force: true });
      }
    } finally {
      fs.rmSync(paths.root, { recursive: true, force: true });
    }
  });
});
