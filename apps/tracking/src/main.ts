/**
 * This is not a production server yet!
 * This is only a minimal backend to get started.
 */

import './bootstrap-env';

import { Logger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { AppModule } from './app/app.module';
import { loadEnv } from './config/env.schema';

async function bootstrap(): Promise<void> {
  const env = loadEnv();
  const app = await NestFactory.create(AppModule);
  const globalPrefix = 'api';
  // Exclude probes so docker-compose/Nginx can reach them without the API prefix.
  app.setGlobalPrefix(globalPrefix, { exclude: ['health', 'ready'] });

  const swaggerConfig = new DocumentBuilder()
    .setTitle('VietRide Tracking API')
    .setVersion('v1')
    .addBearerAuth()
    .build();
  const document = SwaggerModule.createDocument(app, swaggerConfig);
  SwaggerModule.setup('api', app, document);
  const port = env.PORT;
  await app.listen(env.PORT, '0.0.0.0');
  Logger.log(`🚀 Application is running on: http://localhost:${port}/${globalPrefix}`);
}

bootstrap();
