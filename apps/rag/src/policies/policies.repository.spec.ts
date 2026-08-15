import type { Policy } from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { PoliciesRepository } from './policies.repository';

const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const POLICY_ID = '55555555-5555-4555-8555-555555555555';

describe('PoliciesRepository published reads', () => {
  let prisma: {
    $transaction: jest.Mock;
    policy: {
      findMany: jest.Mock;
      count: jest.Mock;
      findFirst: jest.Mock;
    };
  };
  let repository: PoliciesRepository;

  beforeEach(() => {
    prisma = {
      $transaction: jest.fn().mockResolvedValue([[makePolicy()], 1]),
      policy: {
        findMany: jest.fn().mockResolvedValue([makePolicy()]),
        count: jest.fn().mockResolvedValue(1),
        findFirst: jest.fn().mockResolvedValue(makePolicy()),
      },
    };
    repository = new PoliciesRepository(prisma as unknown as RagPrismaService);
  });

  it('limits a consumer list to active FOR_USER platform Policies by default', async () => {
    await repository.listPublished(null, listQuery());

    expect(prisma.policy.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: expect.objectContaining({
          operatorId: null,
          policyType: 'FOR_USER',
          active: true,
          deletedAt: null,
        }),
      }),
    );
  });

  it('combines platform and one requested operator without broadening tenant scope', async () => {
    await repository.listPublished(OPERATOR_ID, listQuery());

    expect(prisma.policy.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: expect.objectContaining({
          OR: [{ operatorId: null }, { operatorId: OPERATOR_ID }],
          policyType: 'FOR_USER',
          active: true,
          deletedAt: null,
        }),
      }),
    );
  });

  it('finds detail only when it is published for users', async () => {
    await repository.findPublishedById(POLICY_ID);

    expect(prisma.policy.findFirst).toHaveBeenCalledWith({
      where: {
        id: POLICY_ID,
        policyType: 'FOR_USER',
        active: true,
        deletedAt: null,
      },
    });
  });
});

function listQuery() {
  return {
    page: 1,
    pageSize: 20,
    sortBy: 'updatedAt' as const,
    sortDir: 'desc' as const,
  };
}

function makePolicy(): Policy {
  return {
    id: POLICY_ID,
    operatorId: null,
    title: 'Refund Policy',
    description: 'Refund rules',
    content: 'Canonical content',
    policyType: 'FOR_USER',
    category: 'REFUND',
    version: 1,
    active: true,
    createdByUserId: '11111111-1111-4111-8111-111111111111',
    createdByDisplayName: 'Policy Admin',
    createdByEmail: 'admin@vietride.vn',
    rowVersion: 0,
    createdAt: new Date('2026-07-29T10:00:00.000Z'),
    updatedAt: new Date('2026-07-29T10:00:00.000Z'),
    deletedAt: null,
  };
}
