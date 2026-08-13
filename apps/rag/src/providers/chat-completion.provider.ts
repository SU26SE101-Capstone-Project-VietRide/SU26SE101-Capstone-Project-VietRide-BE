export interface ChatMessage {
  role: 'system' | 'user' | 'assistant';
  content: string;
}

export interface ChatCompletionRequest {
  messages: ChatMessage[];
  stream: boolean;
  signal?: AbortSignal;
  temperature?: number;
  reasoning?: {
    enabled?: boolean;
    effort?: 'low' | 'medium' | 'high';
  };
}

export interface ChatCompletionProvider {
  complete(request: ChatCompletionRequest): Promise<string>;
  stream(request: ChatCompletionRequest): AsyncIterable<string>;
}
