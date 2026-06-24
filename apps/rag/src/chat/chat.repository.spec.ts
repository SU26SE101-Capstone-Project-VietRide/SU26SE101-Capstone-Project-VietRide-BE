import type { RagPrismaService } from '../prisma/rag-prisma.service';
import { ChatRepository } from './chat.repository';



describe('ChatRepository', () => {
  const prisma = {
    $queryRaw: jest.fn(),
  } as unknown as jest.Mocked<RagPrismaService>;

  beforeEach(() => {
    jest.clearAllMocks();
    prisma.$queryRaw.mockResolvedValue([]);
  });

  it('uses vector-only retrieval when hybrid search is disabled', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'hành lý ký gửi',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC'],
      callerRole: 'PASSENGER',
      limit: 5,
      hybridSearchEnabled: false,
    });

    const sql = resolveSql();
    expect(sql).toContain('ORDER BY c.embedding <=>');
    expect(sql).not.toContain('fts_candidates');
    expect(sql).toContain('d.access_level = ANY(');
  });

  it('includes audience_roles filter for non-admin callers (vector)', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'hành lý ký gửi',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC'],
      callerRole: 'PASSENGER',
      limit: 5,
      hybridSearchEnabled: false,
    });

    const sql = resolveSql();
    expect(sql).toContain('audience_roles');
    expect(sql).toContain('@>');
  });

  it('omits audience_roles filter for SYSTEM_ADMIN callers (vector)', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'admin query',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC', 'OPERATOR', 'ADMIN'],
      callerRole: 'SYSTEM_ADMIN',
      limit: 5,
      hybridSearchEnabled: false,
    });

    const sql = resolveSql();
    expect(sql).not.toContain('audience_roles');
  });

  it('includes audience_roles filter for non-admin callers (hybrid)', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'hành lý ký gửi',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC', 'OPERATOR'],
      operatorId: '22222222-2222-2222-2222-222222222222',
      callerRole: 'DRIVER',
      limit: 5,
      hybridSearchEnabled: true,
    });

    const sql = resolveSql();
    expect(sql).toContain('audience_roles');
    expect(sql).toContain('@>');
  });

  it('uses FTS and vector candidates with RRF when hybrid search is enabled', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'hành lý ký gửi',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC', 'OPERATOR'],
      operatorId: '22222222-2222-2222-2222-222222222222',
      callerRole: 'DRIVER',
      limit: 5,
      hybridSearchEnabled: true,
    });

    const sql = resolveSql();
    expect(sql).toContain('fts_candidates AS');
    expect(sql).toContain('vector_candidates AS');
    expect(sql).toContain('plainto_tsquery');
    expect(sql).toContain('1.0 / (');
    expect(sql).toContain('FULL OUTER JOIN vector_candidates');
    expect(sql).toContain('d.access_level = ANY(');
    expect(sql).toContain('c.operator_id IS NULL');
  });

  /** Resolve full SQL text from $queryRaw mock call, including Prisma.Sql fragment content. */
  function resolveSql(): string {
    const call = prisma.$queryRaw.mock.calls[0];
    if (!call) return '';
    const strings = call[0] as TemplateStringsArray;
    const values = call.slice(1);
    let sql = '';
    for (let i = 0; i < strings.length; i++) {
      sql += strings[i];
      if (i < values.length) {
        const val = values[i];
        if (val && typeof val === 'object' && 'text' in (val as Record<string, unknown>)) {
          sql += (val as { text: string }).text;
        } else {
          sql += '?';
        }
      }
    }
    return sql;
  }
});
