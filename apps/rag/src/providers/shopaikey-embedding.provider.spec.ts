import type { Env } from '../config/env.schema';
import { RAG_EMBEDDING_DIMENSIONS } from '../embedding/embedding.constants';
import { ShopAiKeyEmbeddingProvider } from './shopaikey-embedding.provider';

describe('ShopAiKeyEmbeddingProvider', () => {
  const originalFetch = global.fetch;
  let fetchMock: jest.Mock;

  beforeEach(() => {
    fetchMock = jest.fn();
    global.fetch = fetchMock;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  it.each([[Number.NaN], [Number.POSITIVE_INFINITY], [Number.NEGATIVE_INFINITY]])(
    'rejects non-finite embedding values',
    async (badValue) => {
      const embedding = makeEmbedding();
      embedding[10] = badValue;
      fetchMock.mockResolvedValue(jsonResponse({ data: [{ embedding }] }));
      const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

      await expect(provider.embed({ input: 'query' })).rejects.toMatchObject({
        response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_INVALID_RESPONSE' }),
      });
    },
  );

  it.each([
    { embedding: [] },
    { embedding: [0.1] },
    { embedding: Array.from({ length: RAG_EMBEDDING_DIMENSIONS - 1 }, () => 0.1) },
  ])(
    'rejects missing or incorrectly sized vectors',
    async ({ embedding }) => {
      fetchMock.mockResolvedValue(jsonResponse({ data: [{ embedding }] }));
      const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

      await expect(provider.embed({ input: 'query' })).rejects.toMatchObject({
        response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_INVALID_RESPONSE' }),
      });
    },
  );

  it('sends the OpenAI-compatible ShopAIKey payload and returns finite values', async () => {
    const embedding = makeEmbedding();
    fetchMock.mockResolvedValue(jsonResponse({ data: [{ embedding }] }));
    const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

    await expect(provider.embed({ input: 'query' })).resolves.toEqual(embedding);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://api.shopaikey.com/v1/embeddings',
      expect.objectContaining({
        headers: {
          Authorization: 'Bearer test-key',
          'Content-Type': 'application/json',
        },
      }),
    );
    expect(JSON.parse(fetchMock.mock.calls[0]?.[1]?.body as string)).toEqual({
      model: 'gemini-embedding-2-preview',
      input: 'query',
      encoding_format: 'float',
    });
  });

  it('opens the circuit after three invalid vectors', async () => {
    fetchMock.mockImplementation(async () =>
      jsonResponse({ data: [{ embedding: [0.1] }] }),
    );
    const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

    await expect(provider.embed({ input: 'query-1' })).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_INVALID_RESPONSE' }),
    });
    await expect(provider.embed({ input: 'query-2' })).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_INVALID_RESPONSE' }),
    });
    await expect(provider.embed({ input: 'query-3' })).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_INVALID_RESPONSE' }),
    });
    await expect(provider.embed({ input: 'query-4' })).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_CIRCUIT_OPEN' }),
    });
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('resets consecutive failures only after a valid vector', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ data: [{ embedding: [0.1] }] }))
      .mockResolvedValueOnce(jsonResponse({ data: [{ embedding: [0.1] }] }))
      .mockResolvedValueOnce(jsonResponse({ data: [{ embedding: makeEmbedding() }] }))
      .mockResolvedValueOnce(jsonResponse({ data: [{ embedding: [0.1] }] }))
      .mockResolvedValueOnce(jsonResponse({ data: [{ embedding: makeEmbedding() }] }));
    const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

    await expect(provider.embed({ input: 'query-1' })).rejects.toBeInstanceOf(Error);
    await expect(provider.embed({ input: 'query-2' })).rejects.toBeInstanceOf(Error);
    await expect(provider.embed({ input: 'query-3' })).resolves.toHaveLength(
      RAG_EMBEDDING_DIMENSIONS,
    );
    await expect(provider.embed({ input: 'query-4' })).rejects.toBeInstanceOf(Error);
    await expect(provider.embed({ input: 'query-5' })).resolves.toHaveLength(
      RAG_EMBEDDING_DIMENSIONS,
    );
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it('preserves the controlled rate-limit error code', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ error: { code: 'rate_limit' } }, 429));
    const provider = new ShopAiKeyEmbeddingProvider(makeEnv());

    await expect(provider.embed({ input: 'query' })).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_RATE_LIMITED' }),
    });
  });
});

function makeEmbedding(): number[] {
  return Array.from({ length: RAG_EMBEDDING_DIMENSIONS }, (_, index) => index / 10_000);
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'x-request-id': 'request-1' },
  });
}

function makeEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3003,
    GATEWAY_URL: 'http://gateway:3000',
    IDENTITY_INTERNAL_BASE_URL: 'http://identity:5001',
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/vietride_rag',
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    LOG_LEVEL: 'info',
    SHOPAIKEY_API_KEY: 'test-key',
    SHOPAIKEY_BASE_URL: 'https://api.shopaikey.com/v1',
    SHOPAIKEY_CHAT_MODEL: 'gemini-3.5-flash',
    SHOPAIKEY_EMBEDDING_MODEL: 'gemini-embedding-2-preview',
    RAG_PROVIDER_TIMEOUT_MS: 10_000,
    RAG_MAX_MESSAGE_CHARS: 500,
    RAG_MAX_CONTEXT_TOKENS: 4_000,
    RAG_MAX_RETRIEVED_CHUNKS: 5,
    RAG_USER_RATE_LIMIT_PER_HOUR: 20,
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: 200,
    RAG_INGEST_WORKER_ENABLED: false,
    RAG_OUTBOX_PUBLISH_ENABLED: false,
    INTENT_FILTER_ENABLED: false,
    QUERY_REWRITE_ENABLED: false,
    HYBRID_SEARCH_ENABLED: false,
    RERANK_ENABLED: false,
    SUMMARIZE_ENABLED: false,
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'cloud-key',
    CLOUDINARY_API_SECRET: 'cloud-secret',
    CLOUDINARY_RAG_FOLDER: 'rag/documents',
  };
}
