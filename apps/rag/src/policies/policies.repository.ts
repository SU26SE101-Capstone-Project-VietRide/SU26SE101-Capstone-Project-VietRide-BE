import { Injectable } from '@nestjs/common';
import type { Policy, Prisma } from '../generated/rag-prisma-client';
import { Prisma as PrismaRuntime } from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import type { ListPoliciesQueryDto } from './dto/list-policies.dto';
import type {
  CreatePolicyPersistenceInput,
  DeletePolicyPersistenceInput,
  DeletePolicyPersistenceResult,
  PolicyActor,
  PolicyResponse,
  UpdatePolicyPersistenceInput,
  UpdatePolicyPersistenceResult,
} from './policies.types';
import { toPolicyResponse } from './policies.types';

@Injectable()
export class PoliciesRepository {
  constructor(private readonly prisma: RagPrismaService) {}

  async list(
    operatorId: string | null,
    query: ListPoliciesQueryDto,
  ): Promise<{ items: Policy[]; totalItems: number }> {
    const where = this.toListWhere(operatorId, query);
    const orderBy = [
      { [query.sortBy]: query.sortDir },
      { id: query.sortDir },
    ] as Prisma.PolicyOrderByWithRelationInput[];
    const skip = (query.page - 1) * query.pageSize;
    const [items, totalItems] = await this.prisma.$transaction([
      this.prisma.policy.findMany({ where, orderBy, skip, take: query.pageSize }),
      this.prisma.policy.count({ where }),
    ]);
    return { items, totalItems };
  }

  async findById(policyId: string, operatorId: string | null): Promise<Policy | null> {
    return this.prisma.policy.findFirst({
      where: { id: policyId, operatorId, deletedAt: null },
    });
  }

  async createWithAudit(input: CreatePolicyPersistenceInput): Promise<Policy> {
    return this.prisma.$transaction(async (tx) => {
      const policy = await tx.policy.create({
        data: {
          operatorId: input.operatorId,
          title: input.title,
          description: input.description,
          content: input.content,
          policyType: input.policyType,
          category: input.category,
          active: input.active,
          createdByUserId: input.actor.userId,
          createdByDisplayName: input.actor.displayName,
          createdByEmail: input.actor.email,
        },
      });
      await tx.policyAuditLog.create({
        data: {
          policyId: policy.id,
          action: 'CREATE',
          beforeSnapshot: PrismaRuntime.DbNull,
          afterSnapshot: this.toJson(toPolicyResponse(policy)),
          actorSnapshot: this.toActorJson(input.actor),
        },
      });
      return policy;
    });
  }

  async updateWithAudit(
    input: UpdatePolicyPersistenceInput,
  ): Promise<UpdatePolicyPersistenceResult> {
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.policy.updateMany({
        where: {
          id: input.policyId,
          operatorId: input.operatorId,
          deletedAt: null,
          rowVersion: input.expectedRowVersion,
        },
        data: {
          ...input.changes,
          version: input.nextVersion,
          rowVersion: { increment: 1 },
        },
      });
      if (updated.count !== 1) return { state: 'concurrency_conflict' };

      const policy = await tx.policy.findUniqueOrThrow({ where: { id: input.policyId } });
      await tx.policyAuditLog.create({
        data: {
          policyId: policy.id,
          action: input.action,
          beforeSnapshot: this.toJson(input.before),
          afterSnapshot: this.toJson(toPolicyResponse(policy)),
          actorSnapshot: this.toActorJson(input.actor),
        },
      });
      return { state: 'updated', policy };
    });
  }

  async softDeleteWithAudit(
    input: DeletePolicyPersistenceInput,
  ): Promise<DeletePolicyPersistenceResult> {
    return this.prisma.$transaction(async (tx) => {
      const deleted = await tx.policy.updateMany({
        where: {
          id: input.policyId,
          operatorId: input.operatorId,
          deletedAt: null,
          rowVersion: input.expectedRowVersion,
        },
        data: {
          deletedAt: new Date(),
          rowVersion: { increment: 1 },
        },
      });
      if (deleted.count !== 1) return { state: 'concurrency_conflict' };

      const policy = await tx.policy.findUniqueOrThrow({ where: { id: input.policyId } });
      await tx.policyAuditLog.create({
        data: {
          policyId: policy.id,
          action: input.action,
          beforeSnapshot: this.toJson(input.before),
          afterSnapshot: PrismaRuntime.DbNull,
          actorSnapshot: this.toActorJson(input.actor),
        },
      });
      return { state: 'deleted', policy };
    });
  }

  private toListWhere(
    operatorId: string | null,
    query: ListPoliciesQueryDto,
  ): Prisma.PolicyWhereInput {
    return {
      operatorId,
      deletedAt: null,
      ...(query.policyType ? { policyType: query.policyType } : {}),
      ...(query.category ? { category: query.category } : {}),
      ...(query.active !== undefined ? { active: query.active } : {}),
      ...(query.search
        ? {
            OR: [
              { title: { contains: query.search, mode: 'insensitive' } },
              { description: { contains: query.search, mode: 'insensitive' } },
              { content: { contains: query.search, mode: 'insensitive' } },
              { category: { contains: query.search, mode: 'insensitive' } },
            ],
          }
        : {}),
    };
  }

  private toJson(value: PolicyResponse): Prisma.InputJsonValue {
    return value as unknown as Prisma.InputJsonValue;
  }

  private toActorJson(actor: PolicyActor): Prisma.InputJsonValue {
    return actor as unknown as Prisma.InputJsonValue;
  }
}
