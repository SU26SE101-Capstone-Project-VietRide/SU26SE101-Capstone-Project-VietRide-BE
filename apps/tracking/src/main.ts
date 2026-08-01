import './bootstrap-env';

import { Logger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app/app.module';
import { CorsIoAdapter } from './app/cors-io.adapter';
import { setupTrackingSwagger } from './app/tracking-swagger';
import { loadEnv } from './config/env.schema';

async function bootstrap(): Promise<void> {
  const env = loadEnv();

  if (env.NODE_ENV === 'production' && env.TRACKING_CORS_ORIGIN === '*') {
    throw new Error('TRACKING_CORS_ORIGIN must be restricted in production');
  }

  const app = await NestFactory.create(AppModule);
  app.enableShutdownHooks();
  app.useWebSocketAdapter(new CorsIoAdapter(app, env.TRACKING_CORS_ORIGIN));

  setupTrackingSwagger(app, env.TRACKING_SWAGGER_ENABLED);

  const port = env.PORT;
  await app.listen(env.PORT, '0.0.0.0');
  Logger.log(`🚀 Application is running on: http://localhost:${port}`);
}

bootstrap();
