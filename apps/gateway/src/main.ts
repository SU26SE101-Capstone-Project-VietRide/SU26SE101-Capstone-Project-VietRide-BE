import './bootstrap-env'; // MUST be first: populate process.env from .env before AppModule loads.

import { Logger, ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app/app.module';
import { InternalJwtSigner } from './auth/internal-jwt.signer';
import { loadEnv } from './config/env.schema';
import { createProxyHandler } from './proxy/proxy.middleware';

async function bootstrap(): Promise<void> {
  const env = loadEnv();
  const app = await NestFactory.create(AppModule, { bufferLogs: false });

  app.enableCors({ origin: true, credentials: true });
  app.useGlobalPipes(new ValidationPipe({ transform: true, whitelist: true }));

  // Attach proxy as raw Express middleware via INestApplication.use().
  // Must be placed AFTER NestFactory.create (so DI is ready) but BEFORE app.init/listen
  // so it precedes Nest's router in the Express middleware chain.
  const signer = app.get(InternalJwtSigner);
  app.use(createProxyHandler(env, signer));

  await app.listen(env.GATEWAY_PORT, '0.0.0.0');
  Logger.log(`🚀 VietRide Gateway listening on http://0.0.0.0:${env.GATEWAY_PORT}`, 'Bootstrap');
}

bootstrap();
