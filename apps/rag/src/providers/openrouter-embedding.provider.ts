import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { EmbeddingProvider, EmbeddingRequest } from './embedding.provider';

interface OpenRouterEmbeddingResponse {
  data?: Array<{ embedding?: number[] }>;
}

@Injectable()
export class OpenRouterEmbeddingProvider implements EmbeddingProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async embed(request: EmbeddingRequest): Promise<number[]> {
    const init: RequestInit = {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        model: this.env.OPENROUTER_EMBEDDING_MODEL,
        input: request.input,
      }),
      ...(request.signal ? { signal: request.signal } : {}),
    };
    const response = await fetch(`${this.env.OPENROUTER_BASE_URL}/embeddings`, init);

    if (!response.ok) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_UNAVAILABLE',
        detail: 'OpenRouter embedding provider is unavailable',
      });
    }

    const body = (await response.json()) as OpenRouterEmbeddingResponse;
    const embedding = body.data?.[0]?.embedding;
    if (!embedding?.length || embedding.some((value) => typeof value !== 'number')) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_INVALID_RESPONSE',
        detail: 'OpenRouter embedding provider returned an invalid vector',
      });
    }
    return embedding;
  }

  private buildHeaders(): Record<string, string> {
    return {
      Authorization: `Bearer ${this.env.OPENROUTER_API_KEY}`,
      'Content-Type': 'application/json',
      ...(this.env.OPENROUTER_HTTP_REFERER ? { 'HTTP-Referer': this.env.OPENROUTER_HTTP_REFERER } : {}),
      'X-Title': this.env.OPENROUTER_APP_TITLE,
    };
  }
}
