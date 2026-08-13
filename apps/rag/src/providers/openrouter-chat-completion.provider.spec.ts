import type { Env } from '../config/env.schema';
import { OpenRouterChatCompletionProvider } from './openrouter-chat-completion.provider';

describe('OpenRouterChatCompletionProvider', () => {
  const originalFetch = global.fetch;
  let fetchMock: jest.Mock;

  beforeEach(() => {
    fetchMock = jest.fn();
    global.fetch = fetchMock;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.useRealTimers();
    jest.clearAllMocks();
  });

  it('returns a non-stream completion', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ choices: [{ message: { content: 'Câu trả lời' } }] }),
    );
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await expect(provider.complete(makeRequest())).resolves.toBe('Câu trả lời');
  });

  it('includes explicitly configured temperature and reasoning controls', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ choices: [{ message: { content: 'Câu trả lời' } }] }),
    );
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await provider.complete({
      ...makeRequest(),
      temperature: 0,
      reasoning: { enabled: false },
    });

    expect(JSON.parse(fetchMock.mock.calls[0]?.[1]?.body as string)).toEqual({
      model: 'nvidia/nemotron-3-ultra-550b-a55b:free',
      messages: [{ role: 'user', content: 'Xin chào' }],
      stream: false,
      temperature: 0,
      reasoning: { enabled: false },
    });
  });

  it('omits temperature and reasoning when they are not configured', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ choices: [{ message: { content: 'Câu trả lời' } }] }),
    );
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await provider.complete(makeRequest());

    expect(JSON.parse(fetchMock.mock.calls[0]?.[1]?.body as string)).toEqual({
      model: 'nvidia/nemotron-3-ultra-550b-a55b:free',
      messages: [{ role: 'user', content: 'Xin chào' }],
      stream: false,
    });
  });

  it('preserves the controlled rate-limit error code', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ error: { code: 'rate_limit_exceeded' } }, 429));
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await expect(provider.complete(makeRequest())).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_RATE_LIMITED' }),
    });
  });

  it('streams token frames until the done frame', async () => {
    fetchMock.mockResolvedValue(
      new Response(
        [
          'data: {"choices":[{"delta":{"content":"Xin "}}]}',
          '',
          'data: {"choices":[{"delta":{"content":"chào"}}]}',
          '',
          'data: [DONE]',
          '',
        ].join('\n'),
        { status: 200 },
      ),
    );
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await expect(collect(provider.stream(makeRequest()))).resolves.toEqual(['Xin ', 'chào']);
  });

  it('rejects an OpenRouter error frame instead of silently persisting an empty answer', async () => {
    fetchMock.mockResolvedValue(
      new Response('data: {"error":{"code":"provider_unavailable","message":"capacity"}}\n\n', {
        status: 200,
      }),
    );
    const provider = new OpenRouterChatCompletionProvider(makeEnv());

    await expect(collect(provider.stream(makeRequest()))).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_UNAVAILABLE' }),
    });
  });

  it('keeps the timeout active while the stream body is stalled', async () => {
    jest.useFakeTimers();
    fetchMock.mockImplementation(async (_url: string, init: RequestInit) => {
      const signal = init.signal as AbortSignal;
      return {
        ok: true,
        status: 200,
        headers: new Headers(),
        body: {
          getReader: () => ({
            read: () =>
              new Promise((_, reject) => {
                signal.addEventListener(
                  'abort',
                  () => reject(new DOMException('Aborted', 'AbortError')),
                  { once: true },
                );
              }),
            releaseLock: jest.fn(),
          }),
        },
      };
    });
    const provider = new OpenRouterChatCompletionProvider(makeEnv({ RAG_PROVIDER_TIMEOUT_MS: 25 }));
    const pending = provider.stream(makeRequest())[Symbol.asyncIterator]().next();
    const expectation = expect(pending).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'RAG_PROVIDER_UNAVAILABLE' }),
    });

    await jest.advanceTimersByTimeAsync(26);

    await expectation;
  });
});

function makeRequest() {
  return {
    stream: false,
    messages: [{ role: 'user' as const, content: 'Xin chào' }],
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'x-request-id': 'request-1' },
  });
}

async function collect(stream: AsyncIterable<string>): Promise<string[]> {
  const tokens: string[] = [];
  for await (const token of stream) tokens.push(token);
  return tokens;
}

function makeEnv(overrides: Partial<Env> = {}): Env {
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
    OPENROUTER_API_KEY: 'test-key',
    OPENROUTER_BASE_URL: 'https://openrouter.ai/api/v1',
    OPENROUTER_CHAT_MODEL: 'nvidia/nemotron-3-ultra-550b-a55b:free',
    OPENROUTER_EMBEDDING_MODEL: 'nvidia/llama-nemotron-embed-vl-1b-v2:free',
    OPENROUTER_HTTP_REFERER: undefined,
    OPENROUTER_APP_TITLE: 'VietRide RAG',
    OPENROUTER_ALLOW_PAID_FALLBACK: false,
    RAG_EMBEDDING_DIMENSIONS: 'auto',
    RAG_PROVIDER_TIMEOUT_MS: 10_000,
    RAG_MAX_MESSAGE_CHARS: 500,
    RAG_MAX_CONTEXT_TOKENS: 4_000,
    RAG_MAX_RETRIEVED_CHUNKS: 5,
    RAG_USER_RATE_LIMIT_PER_HOUR: 20,
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: 200,
    RAG_INGEST_WORKER_ENABLED: true,
    RAG_OUTBOX_PUBLISH_ENABLED: false,
    INTENT_FILTER_ENABLED: true,
    QUERY_REWRITE_ENABLED: true,
    HYBRID_SEARCH_ENABLED: true,
    RERANK_ENABLED: true,
    SUMMARIZE_ENABLED: true,
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'cloud-key',
    CLOUDINARY_API_SECRET: 'cloud-secret',
    CLOUDINARY_RAG_FOLDER: 'rag/documents',
    ...overrides,
  };
}
