import { Global, INestApplication, Module } from '@nestjs/common';
import { Test, TestingModule } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import { ChatModule } from '../chat/chat.module';
import type { Env } from '../config/env.schema';
import { RuntimeConfigService } from '../config/runtime-config.service';
import { PoliciesModule } from '../policies/policies.module';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { IdentitySubscriptionEntitlementClient } from './identity-subscription-entitlement.client';
import { RagSubscriptionEntitlementGuard } from './rag-subscription-entitlement.guard';

const testEnv = {
  INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
  INTERNAL_JWT_TTL_SEC: 120,
  IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
} as Env;

@Global()
@Module({
  providers: [
    { provide: ENV_TOKEN, useValue: testEnv },
    { provide: RagPrismaService, useValue: {} },
    {
      provide: RedisService,
      useValue: {
        get: jest.fn(),
        set: jest.fn(),
        getClient: jest.fn(() => ({ eval: jest.fn() })),
      },
    },
    {
      provide: RuntimeConfigService,
      useValue: { getSnapshot: jest.fn() },
    },
  ],
  exports: [ENV_TOKEN, RagPrismaService, RedisService, RuntimeConfigService],
})
class RagModuleWiringTestInfrastructureModule {}

describe('RAG entitlement module wiring', () => {
  let app: INestApplication;
  let moduleRef: TestingModule;

  afterEach(async () => {
    await app?.close();
  });

  it('bootstraps the real ChatModule and PoliciesModule with one shared entitlement client', async () => {
    moduleRef = await Test.createTestingModule({
      imports: [RagModuleWiringTestInfrastructureModule, ChatModule, PoliciesModule],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.init();

    const client = moduleRef.get(IdentitySubscriptionEntitlementClient);
    const chatGuard = moduleRef
      .select(ChatModule)
      .get(RagSubscriptionEntitlementGuard, { strict: true });
    const policiesGuard = moduleRef
      .select(PoliciesModule)
      .get(RagSubscriptionEntitlementGuard, { strict: true });

    expect(entitlementClientOf(chatGuard)).toBe(client);
    expect(entitlementClientOf(policiesGuard)).toBe(client);
  });
});

function entitlementClientOf(
  guard: RagSubscriptionEntitlementGuard,
): IdentitySubscriptionEntitlementClient {
  return (guard as unknown as { subscriptions: IdentitySubscriptionEntitlementClient })
    .subscriptions;
}
