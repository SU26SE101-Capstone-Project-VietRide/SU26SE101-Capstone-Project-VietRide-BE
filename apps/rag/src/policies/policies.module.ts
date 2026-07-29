import { Module } from '@nestjs/common';
import { AdminPoliciesController } from './admin-policies.controller';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';
import { OperatorPoliciesController } from './operator-policies.controller';
import { PoliciesRepository } from './policies.repository';
import { PoliciesService } from './policies.service';

@Module({
  controllers: [AdminPoliciesController, OperatorPoliciesController],
  providers: [PoliciesService, PoliciesRepository, IdentityPolicyActorProvider],
})
export class PoliciesModule {}
