import type { Policy, PolicyAuditAction, PolicyType } from '../generated/rag-prisma-client';

export type PolicyTenantKind = 'ADMIN' | 'OPERATOR';
export type PersistedPolicy = Policy;

export interface PolicyActorProfile {
  displayName: string;
  email: string;
}

export interface PolicyActor extends PolicyActorProfile {
  userId: string;
  role: string;
}

export interface PolicyResponse {
  id: string;
  operatorId: string | null;
  title: string;
  description: string;
  content: string;
  policyType: PolicyType;
  category: string;
  version: number;
  active: boolean;
  createdBy: {
    userId: string;
    displayName: string;
    email: string;
  };
  createdAt: string;
  updatedAt: string;
}

export interface PolicyPage {
  items: PolicyResponse[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface PublishedPolicyResponse {
  id: string;
  operatorId: string | null;
  title: string;
  description: string;
  content: string;
  category: string;
  version: number;
  createdAt: string;
  updatedAt: string;
}

export interface PublishedPolicyPage {
  items: PublishedPolicyResponse[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface CreatePolicyPersistenceInput {
  operatorId: string | null;
  title: string;
  description: string;
  content: string;
  policyType: PolicyType;
  category: string;
  active: boolean;
  actor: PolicyActor;
}

export interface UpdatePolicyPersistenceInput {
  policyId: string;
  operatorId: string | null;
  expectedRowVersion: number;
  nextVersion: number;
  action: PolicyAuditAction;
  changes: Partial<
    Pick<
      PersistedPolicy,
      'title' | 'description' | 'content' | 'policyType' | 'category' | 'active'
    >
  >;
  before: PolicyResponse;
  actor: PolicyActor;
}

export interface DeletePolicyPersistenceInput {
  policyId: string;
  operatorId: string | null;
  expectedRowVersion: number;
  action: 'DELETE';
  before: PolicyResponse;
  after: null;
  actor: PolicyActor;
}

export type UpdatePolicyPersistenceResult =
  | { state: 'updated'; policy: PersistedPolicy }
  | { state: 'concurrency_conflict' };

export type DeletePolicyPersistenceResult =
  | { state: 'deleted'; policy: PersistedPolicy }
  | { state: 'concurrency_conflict' };

export function toPolicyResponse(policy: PersistedPolicy): PolicyResponse {
  return {
    id: policy.id,
    operatorId: policy.operatorId,
    title: policy.title,
    description: policy.description,
    content: policy.content,
    policyType: policy.policyType,
    category: policy.category,
    version: policy.version,
    active: policy.active,
    createdBy: {
      userId: policy.createdByUserId,
      displayName: policy.createdByDisplayName,
      email: policy.createdByEmail,
    },
    createdAt: policy.createdAt.toISOString(),
    updatedAt: policy.updatedAt.toISOString(),
  };
}

export function toPublishedPolicyResponse(policy: PersistedPolicy): PublishedPolicyResponse {
  return {
    id: policy.id,
    operatorId: policy.operatorId,
    title: policy.title,
    description: policy.description,
    content: policy.content,
    category: policy.category,
    version: policy.version,
    createdAt: policy.createdAt.toISOString(),
    updatedAt: policy.updatedAt.toISOString(),
  };
}
