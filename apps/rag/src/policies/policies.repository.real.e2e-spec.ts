import { randomUUID } from 'node:crypto';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { PoliciesRepository } from './policies.repository';
import { toPolicyResponse, type PolicyActor } from './policies.types';

const databaseUrl = process.env.RAG_POLICY_TEST_DATABASE_URL;
const describeDatabase = databaseUrl ? describe : describe.skip;

describeDatabase('PoliciesRepository with PostgreSQL (e2e)', () => {
  let prisma: RagPrismaService;
  let repository: PoliciesRepository;

  beforeAll(async () => {
    process.env.RAG_DATABASE_URL = databaseUrl;
    prisma = new RagPrismaService();
    await prisma.$connect();
    repository = new PoliciesRepository(prisma);
  });

  afterAll(async () => prisma.$disconnect());

  it('writes CREATE audit atomically and keeps admin/operator list scopes isolated', async () => {
    const operatorId = randomUUID();
    const admin = await repository.createWithAudit(createInput(null, 'Platform Policy'));
    const operator = await repository.createWithAudit(createInput(operatorId, 'Operator Policy'));

    const adminPage = await repository.list(null, listQuery());
    const operatorPage = await repository.list(operatorId, listQuery());
    const crossTenant = await repository.findById(operator.id, randomUUID());
    const audits = await prisma.policyAuditLog.findMany({
      where: { policyId: { in: [admin.id, operator.id] } },
      orderBy: { occurredAt: 'asc' },
    });

    expect(adminPage.items.map((item) => item.id)).toContain(admin.id);
    expect(adminPage.items.map((item) => item.id)).not.toContain(operator.id);
    expect(operatorPage.items.map((item) => item.id)).toContain(operator.id);
    expect(crossTenant).toBeNull();
    expect(audits).toHaveLength(2);
    expect(audits).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ policyId: admin.id, action: 'CREATE', beforeSnapshot: null }),
        expect.objectContaining({ policyId: operator.id, action: 'CREATE', beforeSnapshot: null }),
      ]),
    );
  });

  it('increments content version once, preserves it for active-only change, and appends exact actions', async () => {
    const current = await repository.createWithAudit(createInput(null, 'Versioned Policy'));
    const contentUpdate = await repository.updateWithAudit({
      policyId: current.id,
      operatorId: null,
      expectedRowVersion: current.rowVersion,
      nextVersion: 2,
      action: 'UPDATE',
      changes: { title: 'Updated Policy', active: false },
      before: toPolicyResponse(current),
      actor: actor(),
    });
    if (contentUpdate.state !== 'updated') throw new Error('Content update lost unexpectedly');
    const activation = await repository.updateWithAudit({
      policyId: current.id,
      operatorId: null,
      expectedRowVersion: contentUpdate.policy.rowVersion,
      nextVersion: 2,
      action: 'ACTIVATE',
      changes: { active: true },
      before: toPolicyResponse(contentUpdate.policy),
      actor: actor(),
    });
    if (activation.state !== 'updated') throw new Error('Activation lost unexpectedly');

    expect(contentUpdate.policy.version).toBe(2);
    expect(activation.policy.version).toBe(2);
    expect(activation.policy.rowVersion).toBe(2);
    const actions = await prisma.policyAuditLog.findMany({
      where: { policyId: current.id },
      orderBy: { occurredAt: 'asc' },
      select: { action: true },
    });
    expect(actions.map((item) => item.action)).toEqual(['CREATE', 'UPDATE', 'ACTIVATE']);
  });

  it('allows exactly one concurrent guarded mutation and never creates an orphan audit', async () => {
    const current = await repository.createWithAudit(createInput(null, 'Concurrent Policy'));
    const before = toPolicyResponse(current);
    const [content, activation] = await Promise.all([
      repository.updateWithAudit({
        policyId: current.id,
        operatorId: null,
        expectedRowVersion: current.rowVersion,
        nextVersion: 2,
        action: 'UPDATE',
        changes: { title: 'Concurrent content winner' },
        before,
        actor: actor(),
      }),
      repository.updateWithAudit({
        policyId: current.id,
        operatorId: null,
        expectedRowVersion: current.rowVersion,
        nextVersion: 1,
        action: 'DEACTIVATE',
        changes: { active: false },
        before,
        actor: actor(),
      }),
    ]);

    expect([content.state, activation.state].sort()).toEqual(['concurrency_conflict', 'updated']);
    expect(await prisma.policyAuditLog.count({ where: { policyId: current.id } })).toBe(2);
  });

  it('soft-deletes without conflating active and DB-enforces immutable audit rows', async () => {
    const current = await repository.createWithAudit(createInput(null, 'Deleted Policy'));
    const deleted = await repository.softDeleteWithAudit({
      policyId: current.id,
      operatorId: null,
      expectedRowVersion: current.rowVersion,
      action: 'DELETE',
      before: toPolicyResponse(current),
      after: null,
      actor: actor(),
    });
    if (deleted.state !== 'deleted') throw new Error('Delete lost unexpectedly');

    expect(deleted.policy.active).toBe(true);
    expect(deleted.policy.version).toBe(1);
    expect(await repository.findById(current.id, null)).toBeNull();
    const audit = await prisma.policyAuditLog.findFirstOrThrow({
      where: { policyId: current.id, action: 'DELETE' },
    });
    expect(audit.afterSnapshot).toBeNull();
    await expect(
      prisma.$executeRaw`UPDATE vietride_rag.policy_audit_logs SET occurred_at = now() WHERE id = ${audit.id}::uuid`,
    ).rejects.toThrow(/immutable/i);
  });
});

function createInput(operatorId: string | null, title: string) {
  return {
    operatorId,
    title,
    description: 'Description',
    content: 'Content',
    policyType: 'FOR_USER' as const,
    category: 'REFUND',
    active: true,
    actor: actor(),
  };
}

function actor(): PolicyActor {
  return {
    userId: randomUUID(),
    displayName: 'Policy Admin',
    email: 'admin@vietride.vn',
    role: 'SYSTEM_ADMIN',
  };
}

function listQuery() {
  return {
    page: 1,
    pageSize: 100,
    sortBy: 'updatedAt' as const,
    sortDir: 'desc' as const,
  };
}
