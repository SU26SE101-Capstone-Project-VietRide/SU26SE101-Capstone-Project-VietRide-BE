import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { RAG_EMBEDDING_DIMENSIONS } from '../embedding/embedding.constants';
import type { EmbeddingProvider, EmbeddingRequest } from './embedding.provider';

interface ShopAiKeyEmbeddingResponse {
  data?: Array<{ embedding?: number[] }>;
}

const CIRCUIT_BREAKER_FAILURE_THRESHOLD = 3;
const CIRCUIT_BREAKER_OPEN_MS = 60_000;

@Injectable()
export class ShopAiKeyEmbeddingProvider implements EmbeddingProvider {
  private consecutiveFailures = 0;
  private circuitOpenUntil = 0;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async embed(request: EmbeddingRequest): Promise<number[]> {
    this.assertCircuitClosed();
    const controller = new AbortController();
    const abortFromExternal = () => controller.abort(request.signal?.reason);
    if (request.signal?.aborted) abortFromExternal();
    else request.signal?.addEventListener('abort', abortFromExternal, { once: true });
    const timeout = setTimeout(() => controller.abort(), this.env.RAG_PROVIDER_TIMEOUT_MS);
    const init: RequestInit = {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        model: this.env.SHOPAIKEY_EMBEDDING_MODEL,
        input: request.input,
        encoding_format: 'float',
        dimensions: RAG_EMBEDDING_DIMENSIONS,
      }),
      signal: controller.signal,
    };

    try {
      const response = await fetch(`${this.env.SHOPAIKEY_BASE_URL}/embeddings`, init);

      if (!response.ok) {
        this.recordFailure(response.status);
        if (response.status === 429) {
          throw new ServiceUnavailableException({
            errorCode: 'RAG_PROVIDER_RATE_LIMITED',
            detail: 'ShopAIKey embedding provider rate limit reached',
          });
        }
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_UNAVAILABLE',
          detail: 'ShopAIKey embedding provider is unavailable',
        });
      }

      this.recordSuccess();
      const body = (await response.json()) as ShopAiKeyEmbeddingResponse;
      const embedding = body.data?.[0]?.embedding;
      if (
        embedding?.length !== RAG_EMBEDDING_DIMENSIONS ||
        embedding.some((value) => !Number.isFinite(value))
      ) {
        this.recordFailure(502);
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_INVALID_RESPONSE',
          detail: 'ShopAIKey embedding provider returned an invalid vector',
        });
      }
      return embedding;
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      this.recordFailure(503);
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_UNAVAILABLE',
        detail: 'ShopAIKey embedding provider is unavailable',
      });
    } finally {
      clearTimeout(timeout);
      request.signal?.removeEventListener('abort', abortFromExternal);
    }
  }

  private buildHeaders(): Record<string, string> {
    return {
      Authorization: `Bearer ${this.env.SHOPAIKEY_API_KEY}`,
      'Content-Type': 'application/json',
    };
  }

  private assertCircuitClosed(): void {
    if (Date.now() < this.circuitOpenUntil) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_CIRCUIT_OPEN',
        detail: 'ShopAIKey embedding provider circuit is open',
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
