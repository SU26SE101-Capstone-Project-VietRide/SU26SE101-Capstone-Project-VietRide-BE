import { Module } from '@nestjs/common';
import { IdentitySubscriptionEntitlementClient } from './identity-subscription-entitlement.client';
import { RagSubscriptionEntitlementGuard } from './rag-subscription-entitlement.guard';

@Module({
  providers: [IdentitySubscriptionEntitlementClient, RagSubscriptionEntitlementGuard],
  exports: [IdentitySubscriptionEntitlementClient, RagSubscriptionEntitlementGuard],
})
export class RagSubscriptionEntitlementModule {}
