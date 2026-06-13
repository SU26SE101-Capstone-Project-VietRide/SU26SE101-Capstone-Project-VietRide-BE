import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { EmbeddingProvider, EmbeddingRequest } from './embedding.provider';

interface OpenRouterEmbeddingResponse {
  data?: Array<{ embedding?: number[] }>;
}

const CIRCUIT_BREAKER_FAILURE_THRESHOLD = 3;
const CIRCUIT_BREAKER_OPEN_MS = 60_000;

@Injectable()
export class OpenRouterEmbeddingProvider implements EmbeddingProvider {
  private consecutiveFailures = 0;
  private circuitOpenUntil = 0;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async embed(request: EmbeddingRequest): Promise<number[]> {
    this.assertCircuitClosed();
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.RAG_PROVIDER_TIMEOUT_MS);
    const init: RequestInit = {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        model: this.env.OPENROUTER_EMBEDDING_MODEL,
        input: request.input,
      }),
      signal: request.signal ?? controller.signal,
    };

    try {
      const response = await fetch(`${this.env.OPENROUTER_BASE_URL}/embeddings`, init);

      if (!response.ok) {
        this.recordFailure(response.status);
        if (response.status === 429) {
          throw new ServiceUnavailableException({
            errorCode: 'RAG_PROVIDER_RATE_LIMITED',
            detail: 'OpenRouter embedding provider rate limit reached',
          });
        }
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_UNAVAILABLE',
          detail: 'OpenRouter embedding provider is unavailable',
        });
      }

      this.recordSuccess();
      const body = (await response.json()) as OpenRouterEmbeddingResponse;
      const embedding = body.data?.[0]?.embedding;
      if (!embedding?.length || embedding.some((value) => typeof value !== 'number')) {
        this.recordFailure(502);
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_INVALID_RESPONSE',
          detail: 'OpenRouter embedding provider returned an invalid vector',
        });
      }
      return embedding;
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      this.recordFailure(503);
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_UNAVAILABLE',
        detail: 'OpenRouter embedding provider is unavailable',
      });
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildHeaders(): Record<string, string> {
    return {
      Authorization: `Bearer ${this.env.OPENROUTER_API_KEY}`,
      'Content-Type': 'application/json',
      ...(this.env.OPENROUTER_HTTP_REFERER ? { 'HTTP-Referer': this.env.OPENROUTER_HTTP_REFERER } : {}),
      'X-Title': this.env.OPENROUTER_APP_TITLE,
    };
  }

  private assertCircuitClosed(): void {
    if (Date.now() < this.circuitOpenUntil) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_CIRCUIT_OPEN',
        detail: 'OpenRouter embedding provider circuit is open',
      });
    }
  }

  private recordSuccess(): void {
    this.consecutiveFailures = 0;
    this.circuitOpenUntil = 0;
  }

  private recordFailure(status: number): void {
    if (status !== 429 && status < 500) return;
    this.consecutiveFailures += 1;
    if (this.consecutiveFailures >= CIRCUIT_BREAKER_FAILURE_THRESHOLD) {
      this.circuitOpenUntil = Date.now() + CIRCUIT_BREAKER_OPEN_MS;
    }
  }
}
