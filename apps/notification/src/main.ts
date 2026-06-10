/**
 * This is not a production server yet!
 * This is only a minimal backend to get started.
 */

import './bootstrap-env';

import { Logger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app/app.module';
import { loadEnv } from './config/env.schema';

async function bootstrap() {
  const env = loadEnv();
  const app = await NestFactory.create(AppModule);
  const globalPrefix = 'api';
  // Exclude the liveness probe so docker-compose/Nginx can reach it at root /health.
  app.setGlobalPrefix(globalPrefix, { exclude: ['health'] });
  const port = env.PORT;
  await app.listen(port, '0.0.0.0');
  Logger.log(`🚀 Application is running on: http://localhost:${port}/${globalPrefix}`);
}

bootstrap();
