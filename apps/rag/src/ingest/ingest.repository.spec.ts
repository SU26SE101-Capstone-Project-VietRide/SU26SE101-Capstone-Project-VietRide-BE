import { RAG_DOCUMENT_INGEST_REQUESTED_EVENT } from '../documents/documents.constants';
import { RAG_INGEST_MAX_RETRY } from './ingest.constants';
import { IngestRepository } from './ingest.repository';

describe('IngestRepository', () => {
  let prisma: {
    outboxEvent: {
      findMany: jest.Mock;
      update: jest.Mock;
      updateMany: jest.Mock;
    };
    knowledgeDocument: {
      updateMany: jest.Mock;
    };
  };
  let repository: IngestRepository;

  beforeEach(() => {
    prisma = {
      outboxEvent: {
        findMany: jest.fn(),
        update: jest.fn(),
        updateMany: jest.fn(),
      },
      knowledgeDocument: {
        updateMany: jest.fn(),
      },
    };
    repository = new IngestRepository(prisma as never);
  });

  it('polls only retryable ingest outbox events', async () => {
    prisma.outboxEvent.findMany.mockResolvedValue([]);

    await repository.findPendingEvents(5);

    expect(prisma.outboxEvent.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: expect.objectContaining({
          eventType: RAG_DOCUMENT_INGEST_REQUESTED_EVENT,
          status: { in: ['PENDING', 'FAILED', 'PUBLISHING'] },
          retryCount: { lt: RAG_INGEST_MAX_RETRY },
        }),
        take: 5,
      }),
    );
  });

  it('marks an outbox event discarded with final retry count increment', async () => {
    prisma.outboxEvent.update.mockResolvedValue({});
    const error = new Error('malformed payload');

    await repository.markEventDiscarded('event-1', error);

    expect(prisma.outboxEvent.update).toHaveBeenCalledWith({
      where: { id: 'event-1' },
      data: {
        status: 'DISCARDED',
        retryCount: { increment: 1 },
        lastError: 'malformed payload',
      },
    });
  });

  it('reclaims a document left processing after its Redis lease expired', async () => {
    prisma.knowledgeDocument.updateMany.mockResolvedValue({ count: 1 });

    await expect(repository.markDocumentProcessing('document-1')).resolves.toBe(true);

    expect(prisma.knowledgeDocument.updateMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: expect.objectContaining({
          id: 'document-1',
          ingestStatus: { in: ['PENDING', 'FAILED', 'PROCESSING'] },
        }),
      }),
    );
  });
});
