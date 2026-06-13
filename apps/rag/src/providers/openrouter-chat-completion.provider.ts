import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { ChatCompletionProvider, ChatCompletionRequest } from './chat-completion.provider';

interface OpenRouterChatChoice {
  message?: { content?: string };
  delta?: { content?: string };
}

interface OpenRouterChatResponse {
  choices?: OpenRouterChatChoice[];
}

@Injectable()
export class OpenRouterChatCompletionProvider implements ChatCompletionProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async complete(request: ChatCompletionRequest): Promise<string> {
    const response = await this.requestChatCompletion({ ...request, stream: false });
    const body = (await response.json()) as OpenRouterChatResponse;
    return body.choices?.[0]?.message?.content ?? '';
  }

  async *stream(request: ChatCompletionRequest): AsyncIterable<string> {
    const response = await this.requestChatCompletion({ ...request, stream: true });
    const reader = response.body?.getReader();
    if (!reader) return;

    const decoder = new TextDecoder();
    let buffer = '';
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';
        for (const line of lines) {
          const token = this.readSseToken(line);
          if (token) yield token;
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private async requestChatCompletion(request: ChatCompletionRequest): Promise<Response> {
    const init: RequestInit = {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        model: this.env.OPENROUTER_CHAT_MODEL,
        messages: request.messages,
        stream: request.stream,
      }),
      ...(request.signal ? { signal: request.signal } : {}),
    };
    const response = await fetch(`${this.env.OPENROUTER_BASE_URL}/chat/completions`, init);

    if (!response.ok) {
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
      ...(this.env.OPENROUTER_HTTP_REFERER ? { 'HTTP-Referer': this.env.OPENROUTER_HTTP_REFERER } : {}),
      'X-Title': this.env.OPENROUTER_APP_TITLE,
    };
  }

  private readSseToken(line: string): string | undefined {
    if (!line.startsWith('data: ')) return undefined;
    const payload = line.slice('data: '.length).trim();
    if (!payload || payload === '[DONE]') return undefined;
    try {
      const parsed = JSON.parse(payload) as OpenRouterChatResponse;
      return parsed.choices?.[0]?.delta?.content;
    } catch {
      return undefined;
    }
  }
}
