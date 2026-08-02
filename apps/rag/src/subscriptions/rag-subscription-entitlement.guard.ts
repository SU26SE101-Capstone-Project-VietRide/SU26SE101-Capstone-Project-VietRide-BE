import { CanActivate, ExecutionContext, ForbiddenException, Injectable } from '@nestjs/common';
import { z } from 'zod';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import { IdentitySubscriptionEntitlementClient } from './identity-subscription-entitlement.client';

const OPERATOR_SCOPED_ROLES = new Set(['DRIVER', 'ASSISTANT', 'OPERATOR_STAFF', 'OPERATOR_ADMIN']);
const operatorIdSchema = z.string().uuid();

@Injectable()
export class RagSubscriptionEntitlementGuard implements CanActivate {
  private readonly requestChecks = new WeakMap<RequestWithRagInternalUser, Promise<boolean>>();

  constructor(private readonly subscriptions: IdentitySubscriptionEntitlementClient) {}

  canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<RequestWithRagInternalUser>();
    const cached = this.requestChecks.get(request);
    if (cached) return cached;

    const check = this.checkRequest(request);
    this.requestChecks.set(request, check);
    return check;
  }

  private async checkRequest(request: RequestWithRagInternalUser): Promise<boolean> {
    const user = request.user;
    if (!user?.role || !OPERATOR_SCOPED_ROLES.has(user.role)) return true;
    if (!user.operatorId || !operatorIdSchema.safeParse(user.operatorId).success) {
      throw new ForbiddenException({
        errorCode: 'FORBIDDEN',
        detail: 'A verified operator tenant is required to use RAG',
      });
    }

    const entitlement = await this.subscriptions.get(user.operatorId);
    if (!entitlement.enableRag) {
      throw new ForbiddenException({
        errorCode: 'SUBSCRIPTION_MODULE_DISABLED',
        detail: 'RAG is disabled for the active subscription plan',
      });
    }
    return true;
  }
}
