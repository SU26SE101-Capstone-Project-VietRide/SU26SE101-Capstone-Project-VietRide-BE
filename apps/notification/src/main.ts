import './bootstrap-env';

import { Logger, RequestMethod } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { AppModule } from './app/app.module';
import { loadEnv } from './config/env.schema';

async function bootstrap(): Promise<void> {
  const env = loadEnv();
  const app = await NestFactory.create(AppModule);
  const globalPrefix = 'api';
  // Exclude probes so docker-compose/Nginx can reach them without the API prefix.
  app.setGlobalPrefix(globalPrefix, {
    exclude: [
      'health',
      'ready',
      { path: 'internal/(.*)', method: RequestMethod.ALL },
    ],
  });

  const swaggerConfig = new DocumentBuilder()
    .setTitle('VietRide Notification API')
    .setVersion('v1')
    .addBearerAuth()
    .build();
  const document = SwaggerModule.createDocument(app, swaggerConfig);
  SwaggerModule.setup('api', app, document);
  const port = env.PORT;
  await app.listen(port, '0.0.0.0');
  Logger.log(`Application is running on: http://localhost:${port}/${globalPrefix}`);
}

bootstrap().catch((error) => {
  Logger.error(error, 'NotificationBootstrap');
  process.exit(1);
});
