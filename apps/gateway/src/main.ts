import './bootstrap-env'; // Populate process.env from .env before AppModule loads.

import { Logger, ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { SwaggerModule } from '@nestjs/swagger';
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

  // Swagger Aggregator
  const swaggerOptions = {
    explorer: true,
    swaggerOptions: {
      // All downstream specs share the UserAccessToken scheme. Keep the entered
      // bearer token while Swagger UI switches between service definitions.
      persistAuthorization: true,
      urls: [
        { name: 'Identity Service', url: '/api-specs/identity' },
        { name: 'Trip Service', url: '/api-specs/trip' },
        { name: 'Booking Service', url: '/api-specs/booking' },
        { name: 'Payment Service', url: '/api-specs/payment' },
        { name: 'Parcel Service', url: '/api-specs/parcel' },
        { name: 'Tracking Service', url: '/api-specs/tracking' },
        { name: 'Notification Service', url: '/api-specs/notification' },
        { name: 'RAG Service', url: '/api-specs/rag' },
      ],
    },
  };
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  SwaggerModule.setup('docs', app, null as any, swaggerOptions);

  await app.listen(env.GATEWAY_PORT, '0.0.0.0');
  Logger.log(`🚀 VietRide Gateway listening on http://0.0.0.0:${env.GATEWAY_PORT}`, 'Bootstrap');
}

bootstrap();
