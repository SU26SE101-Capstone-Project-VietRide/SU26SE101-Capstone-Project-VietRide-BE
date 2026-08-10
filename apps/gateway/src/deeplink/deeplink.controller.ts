import { Controller, Get, Inject, NotFoundException, Res } from '@nestjs/common';
import type { Response } from 'express';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { renderSetPasswordFallbackPage } from './set-password-fallback.html';

type AssetLinksStatement = {
  relation: string[];
  target: {
    namespace: 'android_app';
    package_name: string;
    sha256_cert_fingerprints: string[];
  };
};

/**
 * Deep-link endpoints served on the apex domain (vietride.online → gateway via
 * Cloudflare Tunnel). Values come from env so fingerprints can be added on the
 * server without rebuilding the image. Per spec
 * docs/superpowers/specs/2026-07-07-deeplink-infra-design.md.
 */
@Controller()
export class DeeplinkController {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  // Android App Links verification. 404 until configured — an empty statement
  // would be cached by Google's verifier as a permanent "no app owns this host".
  // @Res bypasses ApiResponseInterceptor: Google requires the RAW statement
  // array, not the ADR 0004 envelope.
  @Get('.well-known/assetlinks.json')
  assetlinks(@Res() res: Response): void {
    const packageName = this.env.DEEPLINK_ANDROID_PACKAGE ?? this.env.ANDROID_PACKAGE;
    const fingerprints = (this.env.DEEPLINK_ANDROID_SHA256_FINGERPRINTS ?? '')
      .split(',')
      .map((f) => f.trim())
      .filter(Boolean);

    if (!packageName || fingerprints.length === 0) {
      throw new NotFoundException('Deep link configuration is not set.');
    }

    const statements: AssetLinksStatement[] = [
      {
        relation: ['delegate_permission/common.handle_all_urls'],
        target: {
          namespace: 'android_app',
          package_name: packageName,
          sha256_cert_fingerprints: fingerprints,
        },
      },
    ];
    res.json(statements);
  }

  // Fallback for desktop / app-not-installed. @Res bypasses Nest serialization
  // so we can send raw HTML.
  @Get('auth/set-password')
  setPasswordPage(@Res() res: Response): void {
    res.setHeader('Cache-Control', 'no-store');
    res.type('html').send(
      renderSetPasswordFallbackPage({
        scheme: this.env.DEEPLINK_APP_SCHEME,
        androidStoreUrl: this.env.DEEPLINK_ANDROID_STORE_URL,
      }),
    );
  }
}
