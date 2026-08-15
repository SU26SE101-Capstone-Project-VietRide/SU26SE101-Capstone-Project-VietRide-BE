import { Module } from '@nestjs/common';
import { RagSubscriptionEntitlementModule } from '../subscriptions/rag-subscription-entitlement.module';
import { AdminPoliciesController } from './admin-policies.controller';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';
import { OperatorPoliciesController } from './operator-policies.controller';
import { PoliciesRepository } from './policies.repository';
import { PoliciesService } from './policies.service';
import { PublishedPoliciesController } from './published-policies.controller';

@Module({
  imports: [RagSubscriptionEntitlementModule],
  controllers: [AdminPoliciesController, OperatorPoliciesController, PublishedPoliciesController],
  providers: [PoliciesService, PoliciesRepository, IdentityPolicyActorProvider],
})
export class PoliciesModule {}
