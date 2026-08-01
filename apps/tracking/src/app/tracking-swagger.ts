import type { INestApplication } from '@nestjs/common';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';

export function setupTrackingSwagger(app: INestApplication, enabled: boolean): void {
  if (!enabled) return;

  const config = new DocumentBuilder()
    .setTitle('VietRide Tracking API')
    .setVersion('v1')
    .addBearerAuth()
    .build();
  const document = SwaggerModule.createDocument(app, config);
  SwaggerModule.setup('docs', app, document);
}
