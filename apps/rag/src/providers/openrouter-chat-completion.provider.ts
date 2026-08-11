import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import pino from 'pino';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { ChatCompletionProvider, ChatCompletionRequest } from './chat-completion.provider';

interface OpenRouterChatChoice {
  message?: { content?: string };
  delta?: { content?: string };
}

interface OpenRouterChatResponse {
  choices?: OpenRouterChatChoice[];
  error?: { code?: string | number };
}

interface ProviderAbortContext {
  signal: AbortSignal;
  refresh(): void;
  didTimeout(): boolean;
  dispose(): void;
}

const CIRCUIT_BREAKER_FAILURE_THRESHOLD = 3;
const CIRCUIT_BREAKER_OPEN_MS = 60_000;
const PROVIDER_DIAGNOSTIC_MAX_LENGTH = 100;
const logger = pino({ name: 'OpenRouterChatCompletionProvider' });

@Injectable()
export class OpenRouterChatCompletionProvider implements ChatCompletionProvider {
  private consecutiveFailures = 0;
  private circuitOpenUntil = 0;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async complete(request: ChatCompletionRequest): Promise<string> {
    const abortContext = this.createAbortContext(request.signal);
    try {
      const response = await this.requestChatCompletion(
        { ...request, stream: false },
        abortContext.signal,
      );
      abortContext.refresh();
      const body = (await response.json()) as OpenRouterChatResponse;
      const content = body.choices?.[0]?.message?.content;
      if (!content?.trim()) {
        this.recordFailure(502);
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_INVALID_RESPONSE',
          detail: 'OpenRouter chat provider returned an invalid response',
        });
      }
      this.recordSuccess();
      return content;
    } catch (error) {
      throw this.mapUnexpectedFailure(error, 'completion', abortContext.didTimeout());
    } finally {
      abortContext.dispose();
    }
  }

  async *stream(request: ChatCompletionRequest): AsyncIterable<string> {
    const abortContext = this.createAbortContext(request.signal);
    let reader: ReadableStreamDefaultReader<Uint8Array> | undefined;
    const decoder = new TextDecoder();
    let buffer = '';
    try {
      const response = await this.requestChatCompletion(
        { ...request, stream: true },
        abortContext.signal,
      );
      reader = response.body?.getReader();
      if (!reader) {
        this.recordFailure(502);
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_INVALID_RESPONSE',
          detail: 'OpenRouter chat provider returned no stream body',
        });
      }

      while (true) {
        abortContext.refresh();
        const { done, value } = await reader.read();
        if (done) break;
        abortContext.refresh();
        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';
        for (const line of lines) {
          const token = this.readSseToken(line);
          if (token) yield token;
        }
      }
      buffer += decoder.decode();
      if (buffer) {
        const token = this.readSseToken(buffer);
        if (token) yield token;
      }
      this.recordSuccess();
    } catch (error) {
      throw this.mapUnexpectedFailure(error, 'stream', abortContext.didTimeout());
    } finally {
      abortContext.dispose();
      reader?.releaseLock();
    }
  }

  private async requestChatCompletion(
    request: ChatCompletionRequest,
    signal: AbortSignal,
  ): Promise<Response> {
    this.assertCircuitClosed();
    const init: RequestInit = {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        model: this.env.OPENROUTER_CHAT_MODEL,
        messages: request.messages,
        stream: request.stream,
      }),
      signal,
    };
    const response = await fetch(`${this.env.OPENROUTER_BASE_URL}/chat/completions`, init);

    if (!response.ok) {
      this.recordFailure(response.status);
      const diagnostics = await this.readFailureDiagnostics(response);
      logger.warn(
        { model: this.env.OPENROUTER_CHAT_MODEL, ...diagnostics },
        'OpenRouter chat request failed',
      );
      if (response.status === 429) {
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_RATE_LIMITED',
          detail: 'OpenRouter chat provider rate limit reached',
        });
      }
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_UNAVAILABLE',
        detail: 'OpenRouter chat provider is unavailable',
      });
    }

    return response;
  }

  private buildHeaders(): Record<string, string> {
    return {
      Authorization: `Bearer ${this.env.OPENROUTER_API_KEY}`,
      'Content-Type': 'application/json',
      ...(this.env.OPENROUTER_HTTP_REFERER
        ? { 'HTTP-Referer': this.env.OPENROUTER_HTTP_REFERER }
        : {}),
      'X-Title': this.env.OPENROUTER_APP_TITLE,
    };
  }

  private readSseToken(line: string): string | undefined {
    if (!line.startsWith('data: ')) return undefined;
    const payload = line.slice('data: '.length).trim();
    if (!payload || payload === '[DONE]') return undefined;
    try {
      const parsed = JSON.parse(payload) as OpenRouterChatResponse;
      if (parsed.error) {
        const upstreamErrorCode = this.sanitizeDiagnostic(parsed.error.code);
        this.recordFailure(502);
        logger.warn(
          {
            model: this.env.OPENROUTER_CHAT_MODEL,
            ...(upstreamErrorCode ? { upstreamErrorCode } : {}),
          },
          'OpenRouter chat stream returned an error frame',
        );
        throw new ServiceUnavailableException({
          errorCode: 'RAG_PROVIDER_UNAVAILABLE',
          detail: 'OpenRouter chat provider is unavailable',
        });
      }
      return parsed.choices?.[0]?.delta?.content;
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      return undefined;
    }
  }

  private createAbortContext(externalSignal?: AbortSignal): ProviderAbortContext {
    const controller = new AbortController();
    let timeout: NodeJS.Timeout | undefined;
    let timedOut = false;
    const abortFromExternal = () => controller.abort(externalSignal?.reason);
    if (externalSignal?.aborted) abortFromExternal();
    else externalSignal?.addEventListener('abort', abortFromExternal, { once: true });

    const refresh = () => {
      if (controller.signal.aborted) return;
      if (timeout) clearTimeout(timeout);
      timeout = setTimeout(() => {
        timedOut = true;
        controller.abort();
      }, this.env.RAG_PROVIDER_TIMEOUT_MS);
    };
    refresh();

    return {
      signal: controller.signal,
      refresh,
      didTimeout: () => timedOut,
      dispose: () => {
        if (timeout) clearTimeout(timeout);
        externalSignal?.removeEventListener('abort', abortFromExternal);
      },
    };
  }

  private async readFailureDiagnostics(response: Response): Promise<{
    upstreamStatus: number;
    upstreamRequestId?: string;
    upstreamErrorCode?: string;
  }> {
    const upstreamRequestId = this.sanitizeDiagnostic(
      response.headers.get('x-request-id') ?? response.headers.get('x-openrouter-request-id'),
    );
    let upstreamErrorCode: string | undefined;
    try {
      const body = JSON.parse(await response.text()) as OpenRouterChatResponse;
      upstreamErrorCode = this.sanitizeDiagnostic(body.error?.code);
    } catch {
      // Never log an unstructured upstream body because it can echo request content.
    }
    return {
      upstreamStatus: response.status,
      ...(upstreamRequestId ? { upstreamRequestId } : {}),
      ...(upstreamErrorCode ? { upstreamErrorCode } : {}),
    };
  }

  private sanitizeDiagnostic(value: unknown): string | undefined {
    if (typeof value !== 'string' && typeof value !== 'number') return undefined;
    return String(value)
      .replace(/[^A-Za-z0-9_.:-]/g, '_')
      .slice(0, PROVIDER_DIAGNOSTIC_MAX_LENGTH);
  }

  private mapUnexpectedFailure(
    error: unknown,
    stage: 'completion' | 'stream',
    timedOut: boolean,
  ): ServiceUnavailableException {
    if (error instanceof ServiceUnavailableException) return error;
    this.recordFailure(503);
    logger.warn(
      {
        model: this.env.OPENROUTER_CHAT_MODEL,
        stage,
        failureType: timedOut ? 'timeout' : error instanceof Error ? error.name : 'unknown',
      },
      'OpenRouter chat transport failed',
    );
    return new ServiceUnavailableException({
      errorCode: 'RAG_PROVIDER_UNAVAILABLE',
      detail: 'OpenRouter chat provider is unavailable',
    });
  }

  private assertCircuitClosed(): void {
    if (Date.now() < this.circuitOpenUntil) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_PROVIDER_CIRCUIT_OPEN',
        detail: 'OpenRouter chat provider circuit is open',
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
