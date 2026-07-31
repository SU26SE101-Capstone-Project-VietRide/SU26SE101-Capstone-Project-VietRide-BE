import {
  ConflictException,
  ForbiddenException,
  Injectable,
  NotFoundException,
  ServiceUnavailableException,
  UnprocessableEntityException,
} from '@nestjs/common';
import pino from 'pino';
import { z } from 'zod';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import type { CreatePolicyDto } from './dto/create-policy.dto';
import type { ListPoliciesQueryDto } from './dto/list-policies.dto';
import type { UpdatePolicyDto } from './dto/update-policy.dto';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';
import { PoliciesRepository } from './policies.repository';
import type {
  PersistedPolicy,
  PolicyActor,
  PolicyPage,
  PolicyResponse,
  PolicyTenantKind,
} from './policies.types';
import { toPolicyResponse } from './policies.types';

const SYSTEM_ADMIN_ROLE = 'SYSTEM_ADMIN';
const OPERATOR_ADMIN_ROLE = 'OPERATOR_ADMIN';
const operatorIdSchema = z.string().uuid();
const logger = pino({ name: 'PoliciesService' });

@Injectable()
export class PoliciesService {
  constructor(
    private readonly repository: PoliciesRepository,
    private readonly actors: IdentityPolicyActorProvider,
  ) {}

  async list(
    tenantKind: PolicyTenantKind,
    query: ListPoliciesQueryDto,
    user: RagInternalUser | undefined,
  ): Promise<PolicyPage> {
    const operatorId = this.resolveTenant(tenantKind, user);
    const result = await this.repository.list(operatorId, query);
    const totalPages = Math.ceil(result.totalItems / query.pageSize);
    return {
      items: result.items.map(toPolicyResponse),
      page: query.page,
      pageSize: query.pageSize,
      totalItems: result.totalItems,
      totalPages,
      hasNextPage: query.page < totalPages,
      hasPreviousPage: query.page > 1,
    };
  }

  async get(
    tenantKind: PolicyTenantKind,
    policyId: string,
    user: RagInternalUser | undefined,
  ): Promise<PolicyResponse> {
    const operatorId = this.resolveTenant(tenantKind, user);
    return toPolicyResponse(await this.requirePolicy(policyId, operatorId));
  }

  async create(
    tenantKind: PolicyTenantKind,
    dto: CreatePolicyDto,
    user: RagInternalUser | undefined,
  ): Promise<PolicyResponse> {
    const operatorId = this.resolveTenant(tenantKind, user);
    const actor = await this.resolveActor(user);
    const policy = await this.repository.createWithAudit({ operatorId, ...dto, actor });
    logger.info({ policyId: policy.id }, 'Policy created');
    return toPolicyResponse(policy);
  }

  async update(
    tenantKind: PolicyTenantKind,
    policyId: string,
    dto: UpdatePolicyDto,
    user: RagInternalUser | undefined,
  ): Promise<PolicyResponse> {
    const operatorId = this.resolveTenant(tenantKind, user);
    const current = await this.requirePolicy(policyId, operatorId);
    if (current.version !== dto.version) this.throwVersionConflict();

    const mutation = this.buildMutation(current, dto);
    const actor = await this.resolveActor(user);
    const result = await this.repository.updateWithAudit({
      policyId,
      operatorId,
      expectedRowVersion: current.rowVersion,
      nextVersion: mutation.nextVersion,
      action: mutation.action,
      changes: mutation.changes,
      before: toPolicyResponse(current),
      actor,
    });
    if (result.state === 'concurrency_conflict') this.throwVersionConflict();
    logger.info({ policyId, action: mutation.action }, 'Policy updated');
    return toPolicyResponse(result.policy);
  }

  async delete(
    tenantKind: PolicyTenantKind,
    policyId: string,
    user: RagInternalUser | undefined,
  ): Promise<PolicyResponse> {
    const operatorId = this.resolveTenant(tenantKind, user);
    const current = await this.requirePolicy(policyId, operatorId);
    const actor = await this.resolveActor(user);
    const result = await this.repository.softDeleteWithAudit({
      policyId,
      operatorId,
      expectedRowVersion: current.rowVersion,
      action: 'DELETE',
      before: toPolicyResponse(current),
      after: null,
      actor,
    });
    if (result.state === 'concurrency_conflict') this.throwVersionConflict();
    logger.info({ policyId }, 'Policy soft-deleted');
    return toPolicyResponse(result.policy);
  }

  private resolveTenant(
    tenantKind: PolicyTenantKind,
    user: RagInternalUser | undefined,
  ): string | null {
    if (tenantKind === 'ADMIN') {
      if (user?.role !== SYSTEM_ADMIN_ROLE) this.throwForbidden();
      return null;
    }
    if (
      user?.role !== OPERATOR_ADMIN_ROLE ||
      !user.operatorId ||
      !operatorIdSchema.safeParse(user.operatorId).success
    ) {
      this.throwForbidden();
    }
    return user.operatorId;
  }

  private async requirePolicy(
    policyId: string,
    operatorId: string | null,
  ): Promise<PersistedPolicy> {
    const policy = await this.repository.findById(policyId, operatorId);
    if (!policy) {
      throw new NotFoundException({
        errorCode: 'POLICY_NOT_FOUND',
        detail: 'Policy not found',
      });
    }
    return policy;
  }

  private async resolveActor(user: RagInternalUser | undefined): Promise<PolicyActor> {
    if (!user?.role) this.throwForbidden();
    try {
      const profile = await this.actors.resolve(user.sub);
      return {
        userId: user.sub,
        displayName: profile.displayName,
        email: profile.email,
        role: user.role,
      };
    } catch {
      throw new ServiceUnavailableException({
        errorCode: 'UPSTREAM_UNAVAILABLE',
        detail: 'Identity actor profile is temporarily unavailable',
      });
    }
  }

  private buildMutation(current: PersistedPolicy, dto: UpdatePolicyDto) {
    const changes: Partial<
      Pick<
        PersistedPolicy,
        'title' | 'description' | 'content' | 'policyType' | 'category' | 'active'
      >
    > = {};
    let contentChanged = false;
    if (dto.title !== undefined && dto.title !== current.title) changes.title = dto.title;
    if (dto.description !== undefined && dto.description !== current.description) {
      changes.description = dto.description;
    }
    if (dto.content !== undefined && dto.content !== current.content) changes.content = dto.content;
    if (dto.policyType !== undefined && dto.policyType !== current.policyType) {
      changes.policyType = dto.policyType;
    }
    if (dto.category !== undefined && dto.category !== current.category) {
      changes.category = dto.category;
    }
    contentChanged =
      changes.title !== undefined ||
      changes.description !== undefined ||
      changes.content !== undefined ||
      changes.policyType !== undefined ||
      changes.category !== undefined;
    if (dto.active !== undefined && dto.active !== current.active) changes.active = dto.active;
    if (Object.keys(changes).length === 0) {
      throw new UnprocessableEntityException({
        errorCode: 'VALIDATION_ERROR',
        detail: 'PATCH must change at least one Policy field',
      });
    }
    return {
      changes,
      nextVersion: contentChanged ? current.version + 1 : current.version,
      action: contentChanged
        ? ('UPDATE' as const)
        : changes.active
          ? ('ACTIVATE' as const)
          : ('DEACTIVATE' as const),
    };
  }

  private throwVersionConflict(): never {
    throw new ConflictException({
      errorCode: 'POLICY_VERSION_CONFLICT',
      detail: 'Policy version does not match the current version',
    });
  }

  private throwForbidden(): never {
    throw new ForbiddenException({
      errorCode: 'FORBIDDEN',
      detail: 'Caller is not allowed to manage this Policy scope',
    });
  }
}
