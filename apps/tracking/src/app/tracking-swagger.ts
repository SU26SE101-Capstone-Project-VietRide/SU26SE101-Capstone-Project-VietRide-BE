import type { INestApplication } from '@nestjs/common';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';

export function setupTrackingSwagger(app: INestApplication, uiEnabled: boolean): void {
  const config = new DocumentBuilder()
    .setTitle('VietRide Tracking API')
    .setVersion('v1')
    .addBearerAuth()
    .build();
  const document = SwaggerModule.createDocument(app, config);
  SwaggerModule.setup('docs', app, document, {
    // The Gateway Swagger aggregator always reads /docs-json. Keep that raw
    // document available in production even when the standalone Tracking UI
    // is disabled.
    ui: uiEnabled,
    raw: ['json'],
  });
}
