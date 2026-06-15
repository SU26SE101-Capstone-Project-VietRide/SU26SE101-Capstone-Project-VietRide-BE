export interface ChatMessage {
  role: 'system' | 'user' | 'assistant';
  content: string;
}

export interface ChatCompletionRequest {
  messages: ChatMessage[];
  stream: boolean;
  signal?: AbortSignal;
}

export interface ChatCompletionProvider {
  complete(request: ChatCompletionRequest): Promise<string>;
  stream(request: ChatCompletionRequest): AsyncIterable<string>;
}
