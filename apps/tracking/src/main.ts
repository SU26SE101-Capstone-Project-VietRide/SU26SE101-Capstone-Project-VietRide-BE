import './bootstrap-env';

import { Logger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { AppModule } from './app/app.module';
import { CorsIoAdapter } from './app/cors-io.adapter';
import { loadEnv } from './config/env.schema';

async function bootstrap(): Promise<void> {
  const env = loadEnv();

  if (env.NODE_ENV === 'production' && env.TRACKING_CORS_ORIGIN === '*') {
    throw new Error('TRACKING_CORS_ORIGIN must be restricted in production');
  }

  const app = await NestFactory.create(AppModule);
  app.enableShutdownHooks();
  app.useWebSocketAdapter(new CorsIoAdapter(app, env.TRACKING_CORS_ORIGIN));

  if (env.TRACKING_SWAGGER_ENABLED) {
    const swaggerConfig = new DocumentBuilder()
      .setTitle('VietRide Tracking API')
      .setVersion('v1')
      .addBearerAuth()
      .build();
    const document = SwaggerModule.createDocument(app, swaggerConfig);
    SwaggerModule.setup('docs', app, document);
  }
  const port = env.PORT;
  await app.listen(env.PORT, '0.0.0.0');
  Logger.log(`🚀 Application is running on: http://localhost:${port}`);
}

bootstrap();
