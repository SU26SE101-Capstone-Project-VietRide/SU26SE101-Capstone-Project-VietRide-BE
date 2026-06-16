export interface UploadObjectRequest {
  storagePath: string;
  contentType: string;
  body: Buffer;
}

export interface SignedUrlRequest {
  storagePath: string;
  expiresInSeconds: number;
}

export interface StorageProvider {
  uploadObject(request: UploadObjectRequest): Promise<void>;
  downloadObject(storagePath: string): Promise<Buffer>;
  createSignedReadUrl(request: SignedUrlRequest): Promise<string>;
}
