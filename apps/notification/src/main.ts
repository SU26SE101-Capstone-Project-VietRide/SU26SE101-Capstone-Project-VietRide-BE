import './instrument';

import { Logger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import * as Sentry from '@sentry/nestjs';
import { AppModule } from './app/app.module';
import { NotificationCorsIoAdapter } from './app/notification-cors-io.adapter';
import { loadEnv } from './config/env.schema';

async function bootstrap(): Promise<void> {
  const env = loadEnv();
  const app = await NestFactory.create(AppModule);
  app.enableShutdownHooks();
  app.useWebSocketAdapter(new NotificationCorsIoAdapter(app, env.NOTIFICATION_CORS_ORIGIN));

  const swaggerConfig = new DocumentBuilder()
    .setTitle('VietRide Notification API')
    .setVersion('v1')
    .addBearerAuth()
    .build();
  const document = SwaggerModule.createDocument(app, swaggerConfig);
  SwaggerModule.setup('docs', app, document);
  const port = env.PORT;
  await app.listen(port, '0.0.0.0');
  Logger.log(`Application is running on: http://localhost:${port}`);
}

bootstrap().catch(async (error) => {
  Sentry.captureException(error);
  await Sentry.flush(2_000);
  Logger.error('Notification bootstrap failed', undefined, 'NotificationBootstrap');
  process.exit(1);
});
