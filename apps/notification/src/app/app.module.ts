import { Module } from '@nestjs/common';
import { NestRabbitMqModule } from '@vietride/nest-rabbitmq';
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { HealthController } from './health.controller';
import { loadEnv } from '../config/env.schema';
import { IdentityEventsModule } from '../identity-events/identity-events.module';

const env = loadEnv();

@Module({
  imports: [
    NestRabbitMqModule.forRoot({
      url: env.RABBITMQ_URL,
      exchange: env.RABBITMQ_EXCHANGE,
      exchangeType: 'topic',
    }),
    IdentityEventsModule,
  ],
  controllers: [AppController, HealthController],
  providers: [AppService],
})
export class AppModule {}
