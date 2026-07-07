/**
 * Boots the real AppModule over HTTP to verify Express 5 routing of the
 * deep-link endpoints (literal dot in `.well-known/assetlinks.json`) and the
 * UserJwtMiddleware public-whitelist syntax. The proxy middleware from main.ts
 * is not attached here — its passthrough is covered in proxy.middleware.spec.
 */
import type { INestApplication } from '@nestjs/common';
import type { AddressInfo } from 'node:net';

// AppModule calls loadEnv() at import time — env must be set before the
// dynamic import in beforeAll.
process.env['THROTTLER_STORAGE_DISABLE_REDIS'] = '1';
process.env['INTERNAL_JWT_SECRET'] = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
process.env['DEEPLINK_ANDROID_PACKAGE'] = 'online.vietride.driver';
process.env['DEEPLINK_ANDROID_SHA256_FINGERPRINTS'] = 'AA:BB,CC:DD';

describe('deep-link routes over HTTP', () => {
  let app: INestApplication;
  let baseUrl: string;

  beforeAll(async () => {
    const { NestFactory } = await import('@nestjs/core');
    const { AppModule } = await import('../app/app.module');
    app = await NestFactory.create(AppModule, { logger: false });
    await app.listen(0, '127.0.0.1');
    const { port } = app.getHttpServer().address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${port}`;
  });

  afterAll(async () => {
    await app?.close();
  });

  it('serves assetlinks.json as application/json without auth', async () => {
    const res = await fetch(`${baseUrl}/.well-known/assetlinks.json`);
    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = (await res.json()) as Array<{ target: { package_name: string } }>;
    expect(body[0]?.target.package_name).toBe('online.vietride.driver');
  });

  it('serves the fallback page without auth and never reflects the token', async () => {
    const res = await fetch(`${baseUrl}/auth/set-password?token=secret-token-value-xyz`);
    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('text/html');
    expect(res.headers.get('cache-control')).toBe('no-store');
    const html = await res.text();
    expect(html).toContain('vietride://auth/set-password');
    expect(html).not.toContain('secret-token-value-xyz');
  });
});
