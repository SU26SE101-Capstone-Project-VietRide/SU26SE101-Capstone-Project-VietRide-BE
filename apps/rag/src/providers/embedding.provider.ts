export interface EmbeddingRequest {
  input: string;
  signal?: AbortSignal;
}

export interface EmbeddingProvider {
  embed(request: EmbeddingRequest): Promise<number[]>;
}
