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
      limit: 5,
      hybridSearchEnabled: false,
    });

    const sql = readSql();
    expect(sql).toContain('ORDER BY c.embedding <=>');
    expect(sql).not.toContain('fts_candidates');
    expect(sql).toContain("d.access_level = ANY(");
  });

  it('uses FTS and vector candidates with RRF when hybrid search is enabled', async () => {
    const repository = new ChatRepository(prisma);

    await repository.searchChunks({
      queryText: 'hành lý ký gửi',
      queryEmbedding: [0.1, 0.2],
      accessLevels: ['PUBLIC', 'OPERATOR'],
      operatorId: '22222222-2222-2222-2222-222222222222',
      limit: 5,
      hybridSearchEnabled: true,
    });

    const sql = readSql();
    expect(sql).toContain('fts_candidates AS');
    expect(sql).toContain('vector_candidates AS');
    expect(sql).toContain('plainto_tsquery');
    expect(sql).toContain('1.0 / (');
    expect(sql).toContain('FULL OUTER JOIN vector_candidates');
    expect(sql).toContain('d.access_level = ANY(');
    expect(sql).toContain('c.operator_id IS NULL');
  });

  function readSql(): string {
    const call = prisma.$queryRaw.mock.calls[0];
    const strings = call?.[0] as TemplateStringsArray | undefined;
    return Array.from(strings ?? []).join(' ');
  }
});
