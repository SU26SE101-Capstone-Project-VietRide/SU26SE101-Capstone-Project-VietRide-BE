import { ConflictException, ForbiddenException, ServiceUnavailableException } from '@nestjs/common';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';
import type { CreatePolicyDto } from './dto/create-policy.dto';
import type { UpdatePolicyDto } from './dto/update-policy.dto';
import { PoliciesRepository } from './policies.repository';
import { PoliciesService } from './policies.service';
import type { PersistedPolicy } from './policies.types';

const ADMIN_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ADMIN_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const POLICY_ID = '44444444-4444-4444-8444-444444444444';

describe('PoliciesService', () => {
  let repository: jest.Mocked<PoliciesRepository>;
  let actors: jest.Mocked<IdentityPolicyActorProvider>;
  let service: PoliciesService;

  beforeEach(() => {
    repository = {
      list: jest.fn(),
      findById: jest.fn(),
      createWithAudit: jest.fn(),
      updateWithAudit: jest.fn(),
      softDeleteWithAudit: jest.fn(),
    } as unknown as jest.Mocked<PoliciesRepository>;
    actors = {
      resolve: jest.fn().mockResolvedValue({
        displayName: 'Policy Admin',
        email: 'admin@vietride.vn',
      }),
    } as unknown as jest.Mocked<IdentityPolicyActorProvider>;
    service = new PoliciesService(repository, actors);
  });

  it('creates an operator Policy only in the operator tenant from the verified JWT', async () => {
    const dto = createDto();
    repository.createWithAudit.mockResolvedValue(
      makePolicy({ operatorId: OPERATOR_ID, createdByUserId: OPERATOR_ADMIN_ID }),
    );

    const result = await service.create('OPERATOR', dto, operatorAdmin());

    expect(result.operatorId).toBe(OPERATOR_ID);
    expect(repository.createWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        operatorId: OPERATOR_ID,
        actor: {
          userId: OPERATOR_ADMIN_ID,
          displayName: 'Policy Admin',
          email: 'admin@vietride.vn',
          role: 'OPERATOR_ADMIN',
        },
      }),
    );
  });

  it('rejects OPERATOR_STAFF before reading or writing Policy data', async () => {
    const action = service.list('OPERATOR', listQuery(), {
      sub: OPERATOR_ADMIN_ID,
      role: 'OPERATOR_STAFF',
      operatorId: OPERATOR_ID,
    });

    await expect(action).rejects.toBeInstanceOf(ForbiddenException);
    expect(repository.list).not.toHaveBeenCalled();
  });

  it('increments content version exactly once and records one UPDATE when active also changes', async () => {
    const current = makePolicy();
    repository.findById.mockResolvedValue(current);
    repository.updateWithAudit.mockResolvedValue({
      state: 'updated',
      policy: makePolicy({
        title: 'Updated title',
        active: false,
        version: 2,
        rowVersion: 1,
      }),
    });
    const dto: UpdatePolicyDto = { version: 1, title: 'Updated title', active: false };

    const result = await service.update('ADMIN', POLICY_ID, dto, systemAdmin());

    expect(result.version).toBe(2);
    expect(repository.updateWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        expectedRowVersion: 0,
        nextVersion: 2,
        action: 'UPDATE',
        changes: expect.objectContaining({ title: 'Updated title', active: false }),
      }),
    );
  });

  it('does not increment content version for an activation-only change', async () => {
    const current = makePolicy();
    repository.findById.mockResolvedValue(current);
    repository.updateWithAudit.mockResolvedValue({
      state: 'updated',
      policy: makePolicy({ active: false, rowVersion: 1 }),
    });

    const result = await service.update(
      'ADMIN',
      POLICY_ID,
      { version: 1, active: false },
      systemAdmin(),
    );

    expect(result.version).toBe(1);
    expect(repository.updateWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({ nextVersion: 1, action: 'DEACTIVATE' }),
    );
  });

  it('returns POLICY_VERSION_CONFLICT without resolving an actor when PATCH version is stale', async () => {
    repository.findById.mockResolvedValue(makePolicy({ version: 3 }));

    const action = service.update(
      'ADMIN',
      POLICY_ID,
      { version: 2, title: 'Stale' },
      systemAdmin(),
    );

    await expect(action).rejects.toMatchObject({
      response: expect.objectContaining({ errorCode: 'POLICY_VERSION_CONFLICT' }),
    });
    expect(actors.resolve).not.toHaveBeenCalled();
    expect(repository.updateWithAudit).not.toHaveBeenCalled();
  });

  it('maps a concurrent guarded update to POLICY_VERSION_CONFLICT', async () => {
    repository.findById.mockResolvedValue(makePolicy());
    repository.updateWithAudit.mockResolvedValue({ state: 'concurrency_conflict' });

    const action = service.update(
      'ADMIN',
      POLICY_ID,
      { version: 1, title: 'Concurrent' },
      systemAdmin(),
    );

    await expect(action).rejects.toBeInstanceOf(ConflictException);
  });

  it('fails closed with 503 and no mutation when Identity actor lookup fails', async () => {
    actors.resolve.mockRejectedValue(new Error('identity unavailable'));

    const action = service.create('ADMIN', createDto(), systemAdmin());

    await expect(action).rejects.toBeInstanceOf(ServiceUnavailableException);
    expect(repository.createWithAudit).not.toHaveBeenCalled();
  });

  it('lists only platform Policies for SYSTEM_ADMIN without an Identity lookup', async () => {
    repository.list.mockResolvedValue({ items: [makePolicy()], totalItems: 1 });

    const result = await service.list('ADMIN', listQuery(), systemAdmin());

    expect(result.totalItems).toBe(1);
    expect(repository.list).toHaveBeenCalledWith(null, listQuery());
    expect(actors.resolve).not.toHaveBeenCalled();
  });

  it('soft-deletes with a DELETE audit snapshot and preserves tenant masking', async () => {
    const current = makePolicy({ operatorId: OPERATOR_ID, createdByUserId: OPERATOR_ADMIN_ID });
    repository.findById.mockResolvedValue(current);
    repository.softDeleteWithAudit.mockResolvedValue({ state: 'deleted', policy: current });

    const result = await service.delete('OPERATOR', POLICY_ID, operatorAdmin());

    expect(result.id).toBe(POLICY_ID);
    expect(repository.softDeleteWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        operatorId: OPERATOR_ID,
        expectedRowVersion: 0,
        action: 'DELETE',
        before: expect.objectContaining({ id: POLICY_ID }),
        after: null,
      }),
    );
  });
});

function createDto(): CreatePolicyDto {
  return {
    title: 'Refund policy',
    description: 'Platform refund rules',
    content: 'Canonical content',
    policyType: 'FOR_USER',
    category: 'REFUND',
    active: true,
  };
}

function listQuery() {
  return {
    page: 1,
    pageSize: 20,
    sortBy: 'updatedAt' as const,
    sortDir: 'desc' as const,
  };
}

function systemAdmin(): RagInternalUser {
  return { sub: ADMIN_ID, role: 'SYSTEM_ADMIN' };
}

function operatorAdmin(): RagInternalUser {
  return { sub: OPERATOR_ADMIN_ID, role: 'OPERATOR_ADMIN', operatorId: OPERATOR_ID };
}

function makePolicy(overrides: Partial<PersistedPolicy> = {}): PersistedPolicy {
  return {
    id: POLICY_ID,
    operatorId: null,
    title: 'Refund policy',
    description: 'Platform refund rules',
    content: 'Canonical content',
    policyType: 'FOR_USER',
    category: 'REFUND',
    version: 1,
    active: true,
    createdByUserId: ADMIN_ID,
    createdByDisplayName: 'Policy Admin',
    createdByEmail: 'admin@vietride.vn',
    rowVersion: 0,
    createdAt: new Date('2026-07-29T10:00:00.000Z'),
    updatedAt: new Date('2026-07-29T10:00:00.000Z'),
    deletedAt: null,
    ...overrides,
  };
}
