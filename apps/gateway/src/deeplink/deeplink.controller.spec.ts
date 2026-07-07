import { NotFoundException } from '@nestjs/common';
import type { Response } from 'express';
import { envSchema, type Env } from '../config/env.schema';
import { DeeplinkController } from './deeplink.controller';

const base = { INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa' };

function makeEnv(extra: Record<string, string> = {}): Env {
  return envSchema.parse({ ...base, ...extra });
}

type CapturedResponse = Response & {
  body?: string;
  jsonBody?: unknown;
  headers: Record<string, string>;
};

function makeRes(): CapturedResponse {
  const res = {
    headers: {} as Record<string, string>,
    setHeader: jest.fn(function (this: CapturedResponse, name: string, value: string) {
      this.headers[name] = value;
      return this;
    }),
    type: jest.fn(function (this: CapturedResponse) {
      return this;
    }),
    send: jest.fn(function (this: CapturedResponse, body: string) {
      this.body = body;
      return this;
    }),
    json: jest.fn(function (this: CapturedResponse, body: unknown) {
      this.jsonBody = body;
      return this;
    }),
  } as unknown as CapturedResponse;
  return res;
}

describe('DeeplinkController', () => {
  describe('assetlinks.json', () => {
    it('throws NotFoundException when android package is not configured', () => {
      const controller = new DeeplinkController(makeEnv());
      expect(() => controller.assetlinks(makeRes())).toThrow(NotFoundException);
    });

    it('throws NotFoundException when fingerprints are missing', () => {
      const controller = new DeeplinkController(
        makeEnv({ DEEPLINK_ANDROID_PACKAGE: 'online.vietride.driver' }),
      );
      expect(() => controller.assetlinks(makeRes())).toThrow(NotFoundException);
    });

    it('sends the RAW Digital Asset Links statement (no ADR 0004 envelope), splitting fingerprints on commas', () => {
      const controller = new DeeplinkController(
        makeEnv({
          DEEPLINK_ANDROID_PACKAGE: 'online.vietride.driver',
          DEEPLINK_ANDROID_SHA256_FINGERPRINTS: 'AA:BB, CC:DD ,',
        }),
      );
      const res = makeRes();

      controller.assetlinks(res);

      expect(res.jsonBody).toEqual([
        {
          relation: ['delegate_permission/common.handle_all_urls'],
          target: {
            namespace: 'android_app',
            package_name: 'online.vietride.driver',
            sha256_cert_fingerprints: ['AA:BB', 'CC:DD'],
          },
        },
      ]);
    });
  });

  describe('GET /auth/set-password fallback page', () => {
    it('serves HTML with the app scheme and Cache-Control no-store', () => {
      const controller = new DeeplinkController(makeEnv());
      const res = makeRes();

      controller.setPasswordPage(res);

      expect(res.headers['Cache-Control']).toBe('no-store');
      expect(res.type).toHaveBeenCalledWith('html');
      expect(res.body).toContain('vietride://auth/set-password');
    });

    it('never embeds a token server-side (page reads it from location.search)', () => {
      const controller = new DeeplinkController(makeEnv());
      const res = makeRes();

      controller.setPasswordPage(res);

      expect(res.body).toContain('location.search');
      expect(res.body).not.toMatch(/token=[A-Za-z0-9-]/);
    });

    it('shows the store link only when DEEPLINK_ANDROID_STORE_URL is set', () => {
      const withStore = makeRes();
      new DeeplinkController(
        makeEnv({ DEEPLINK_ANDROID_STORE_URL: 'https://play.google.com/store/apps/details?id=x' }),
      ).setPasswordPage(withStore);
      expect(withStore.body).toContain('https://play.google.com/store/apps/details?id=x');

      const withoutStore = makeRes();
      new DeeplinkController(makeEnv()).setPasswordPage(withoutStore);
      expect(withoutStore.body).not.toContain('play.google.com');
    });
  });
});
