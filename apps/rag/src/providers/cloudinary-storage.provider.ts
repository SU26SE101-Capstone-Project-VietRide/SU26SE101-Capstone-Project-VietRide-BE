import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { createHash } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { SignedUrlRequest, StorageProvider, UploadObjectRequest } from './storage.provider';

interface CloudinaryUploadResponse {
  public_id?: string;
  secure_url?: string;
}

const CLOUDINARY_RESOURCE_TYPE = 'raw';
const CLOUDINARY_SIGNED_URL_IGNORED_TTL_SECONDS = 0;

@Injectable()
export class CloudinaryStorageProvider implements StorageProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async uploadObject(request: UploadObjectRequest): Promise<void> {
    const form = new FormData();
    form.set('file', new Blob([request.body], { type: request.contentType }), request.storagePath);
    form.set('public_id', request.storagePath);
    form.set('folder', this.env.CLOUDINARY_RAG_FOLDER);
    const timestamp = Math.floor(Date.now() / 1_000).toString();
    const signedParams = {
      folder: this.env.CLOUDINARY_RAG_FOLDER,
      public_id: request.storagePath,
      timestamp,
    };
    form.set('timestamp', timestamp);
    form.set('api_key', this.env.CLOUDINARY_API_KEY);
    form.set('signature', this.signParams(signedParams));

    const response = await fetch(this.resourceUrl('upload'), {
      method: 'POST',
      body: form,
    });

    if (!response.ok) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_STORAGE_UNAVAILABLE',
        detail: 'Cloudinary upload failed',
      });
    }

    const body = (await response.json()) as CloudinaryUploadResponse;
    if (!body.public_id || !body.secure_url) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_STORAGE_INVALID_RESPONSE',
        detail: 'Cloudinary upload returned an invalid response',
      });
    }
  }

  async downloadObject(storagePath: string): Promise<Buffer> {
    const response = await fetch(this.deliveryUrl(storagePath));
    if (!response.ok) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_STORAGE_UNAVAILABLE',
        detail: 'Cloudinary download failed',
      });
    }

    return Buffer.from(await response.arrayBuffer());
  }

  async createSignedReadUrl(request: SignedUrlRequest): Promise<string> {
    void request.expiresInSeconds;
    void CLOUDINARY_SIGNED_URL_IGNORED_TTL_SECONDS;
    return this.deliveryUrl(request.storagePath);
  }

  private resourceUrl(action: 'upload'): string {
    return `https://api.cloudinary.com/v1_1/${this.env.CLOUDINARY_CLOUD_NAME}/${CLOUDINARY_RESOURCE_TYPE}/${action}`;
  }

  private deliveryUrl(storagePath: string): string {
    const encodedPath = storagePath
      .split('/')
      .map((part) => encodeURIComponent(part))
      .join('/');
    return `https://res.cloudinary.com/${this.env.CLOUDINARY_CLOUD_NAME}/${CLOUDINARY_RESOURCE_TYPE}/upload/${this.env.CLOUDINARY_RAG_FOLDER}/${encodedPath}`;
  }

  private signParams(params: Record<string, string>): string {
    const payload = Object.entries(params)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, value]) => `${key}=${value}`)
      .join('&');
    return createHash('sha1')
      .update(`${payload}${this.env.CLOUDINARY_API_SECRET}`)
      .digest('hex');
  }
}
