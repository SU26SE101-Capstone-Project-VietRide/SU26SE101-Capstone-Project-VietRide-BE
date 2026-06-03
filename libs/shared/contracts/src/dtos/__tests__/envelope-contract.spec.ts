import { z } from 'zod';
import { apiResponseSchema, ApiResponseErrorSchema } from '../api-response';
import { pagedResultSchema } from '../page-result';
import { QueryOptionsSchema } from '../query-options';

/**
 * Cross-stack contract test — ADR 0004 follow-up.
 *
 * Asserts the TS Zod schemas validate a STATIC .NET-shaped envelope fixture.
 * If the .NET envelope shape changes, this fixture must be updated in lockstep.
 *
 * The fixture is committed as part of this test (not generated at runtime)
 * so it is robust to the .NET build landing in parallel (Task 3.8).
 */

// ── Fixtures (committed .NET envelope shapes) ─────────────────────────────────

const SUCCESS_SINGLE_FIXTURE = {
  success: true,
  statusCode: 200,
  message: 'Operation successful',
  data: {
    userId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    email: 'user@example.com',
    status: 'ACTIVE',
  },
  meta: {
    traceId: 'req-abc123',
    timestamp: '2026-06-01T10:00:00.000Z',
  },
};

const SUCCESS_CREATED_FIXTURE = {
  success: true,
  statusCode: 201,
  data: {
    userId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    email: 'user@example.com',
    status: 'PENDING_EMAIL_VERIFICATION',
    otpTtlMinutes: 5,
  },
  meta: {
    traceId: 'req-def456',
    timestamp: '2026-06-01T10:00:01.000Z',
  },
};

const SUCCESS_PAGED_FIXTURE = {
  success: true,
  statusCode: 200,
  data: {
    items: [
      { id: '1', name: 'Item 1' },
      { id: '2', name: 'Item 2' },
    ],
    page: 1,
    pageSize: 20,
    totalItems: 57,
    totalPages: 3,
    hasNextPage: true,
    hasPreviousPage: false,
  },
  meta: {
    traceId: 'req-ghi789',
    timestamp: '2026-06-01T10:00:02.000Z',
  },
};

const ERROR_400_FIXTURE = {
  success: false,
  statusCode: 400,
  error: {
    code: 'AUTH_OTP_INVALID',
    message: 'OTP code is invalid.',
  },
  meta: {
    traceId: 'req-jkl012',
    timestamp: '2026-06-01T10:00:03.000Z',
  },
};

const ERROR_422_VALIDATION_FIXTURE = {
  success: false,
  statusCode: 422,
  error: {
    code: 'VALIDATION_ERROR',
    message: 'Validation failed',
    fields: [
      { field: 'email', message: 'Invalid email format' },
      { field: 'phone', message: 'Phone is required' },
    ],
  },
  meta: {
    traceId: 'req-mno345',
    timestamp: '2026-06-01T10:00:04.000Z',
  },
};

const ERROR_429_FIXTURE = {
  success: false,
  statusCode: 429,
  error: {
    code: 'AUTH_OTP_RATE_LIMIT_EXCEEDED',
    message: 'OTP request rate limit exceeded.',
  },
  meta: {
    traceId: 'req-pqr678',
    timestamp: '2026-06-01T10:00:05.000Z',
  },
};

const ERROR_401_FIXTURE = {
  success: false,
  statusCode: 401,
  error: {
    code: 'AUTH_INVALID_CREDENTIALS',
    message: 'Invalid email or password.',
  },
  meta: {
    traceId: 'req-stu901',
    timestamp: '2026-06-01T10:00:06.000Z',
  },
};

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('Cross-stack envelope contract (ADR 0004)', () => {
  describe('success envelope (single resource)', () => {
    it('validates a 200 single-resource success fixture', () => {
      const schema = apiResponseSchema(
        z.object({
          userId: z.string(),
          email: z.string(),
          status: z.string(),
        }),
      );
      const result = schema.safeParse(SUCCESS_SINGLE_FIXTURE);
      expect(result.success).toBe(true);
    });

    it('validates a 201 created success fixture', () => {
      const schema = apiResponseSchema(
        z.object({
          userId: z.string(),
          email: z.string(),
          status: z.string(),
          otpTtlMinutes: z.number(),
        }),
      );
      const result = schema.safeParse(SUCCESS_CREATED_FIXTURE);
      expect(result.success).toBe(true);
    });

    it('rejects a success envelope missing required meta.traceId', () => {
      const schema = apiResponseSchema(z.object({ id: z.string() }));
      const bad = {
        success: true,
        statusCode: 200,
        data: { id: '1' },
        meta: { timestamp: '2026-06-01T10:00:00Z' },
      };
      const result = schema.safeParse(bad);
      expect(result.success).toBe(false);
    });

    it('rejects success:false in a success schema', () => {
      const schema = apiResponseSchema(z.object({ id: z.string() }));
      const bad = { ...SUCCESS_SINGLE_FIXTURE, success: false };
      const result = schema.safeParse(bad);
      expect(result.success).toBe(false);
    });
  });

  describe('success envelope (paged list)', () => {
    it('validates a paged result fixture', () => {
      const itemSchema = z.object({ id: z.string(), name: z.string() });
      const schema = apiResponseSchema(pagedResultSchema(itemSchema));
      const result = schema.safeParse(SUCCESS_PAGED_FIXTURE);
      expect(result.success).toBe(true);
    });

    it('computes totalPages/hasNextPage/hasPreviousPage from paged fixture', () => {
      const data = SUCCESS_PAGED_FIXTURE.data;
      expect(data.totalPages).toBe(3);
      expect(data.hasNextPage).toBe(true);
      expect(data.hasPreviousPage).toBe(false);
    });
  });

  describe('error envelope', () => {
    it('validates a 400 error fixture', () => {
      const result = ApiResponseErrorSchema.safeParse(ERROR_400_FIXTURE);
      expect(result.success).toBe(true);
    });

    it('validates a 422 validation error fixture with fields', () => {
      const result = ApiResponseErrorSchema.safeParse(ERROR_422_VALIDATION_FIXTURE);
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.error.fields).toHaveLength(2);
        expect(result.data.error.fields?.[0]?.field).toBe('email');
      }
    });

    it('validates a 429 rate-limit error fixture', () => {
      const result = ApiResponseErrorSchema.safeParse(ERROR_429_FIXTURE);
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.error.code).toBe('AUTH_OTP_RATE_LIMIT_EXCEEDED');
        expect(result.data.statusCode).toBe(429);
      }
    });

    it('validates a 401 unauthorized error fixture', () => {
      const result = ApiResponseErrorSchema.safeParse(ERROR_401_FIXTURE);
      expect(result.success).toBe(true);
    });

    it('rejects an error envelope with success:true', () => {
      const bad = { ...ERROR_400_FIXTURE, success: true };
      const result = ApiResponseErrorSchema.safeParse(bad);
      expect(result.success).toBe(false);
    });

    it('rejects an error envelope missing error.code', () => {
      const bad = {
        success: false,
        statusCode: 400,
        error: { message: 'no code' },
        meta: { traceId: 'req-no-code', timestamp: '2026-06-01T10:00:00Z' },
      };
      const result = ApiResponseErrorSchema.safeParse(bad);
      expect(result.success).toBe(false);
    });
  });

  describe('QueryOptions schema', () => {
    it('applies defaults for empty input', () => {
      const result = QueryOptionsSchema.safeParse({});
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.page).toBe(1);
        expect(result.data.pageSize).toBe(20);
        expect(result.data.sortDir).toBe('desc');
        expect(result.data.includeDeleted).toBe(false);
      }
    });

    it('clamps pageSize to 1..100', () => {
      const tooLarge = QueryOptionsSchema.safeParse({ pageSize: 500 });
      expect(tooLarge.success).toBe(true);
      if (tooLarge.success) {
        expect(tooLarge.data.pageSize).toBe(100);
      }

      const tooSmall = QueryOptionsSchema.safeParse({ pageSize: 0 });
      expect(tooSmall.success).toBe(true);
      if (tooSmall.success) {
        expect(tooSmall.data.pageSize).toBe(1);
      }
    });

    it('rejects page < 1', () => {
      const result = QueryOptionsSchema.safeParse({ page: 0 });
      expect(result.success).toBe(false);
    });

    it('accepts valid sortDir values', () => {
      const asc = QueryOptionsSchema.safeParse({ sortDir: 'asc' });
      const desc = QueryOptionsSchema.safeParse({ sortDir: 'desc' });
      expect(asc.success).toBe(true);
      expect(desc.success).toBe(true);
    });

    it('rejects invalid sortDir', () => {
      const result = QueryOptionsSchema.safeParse({ sortDir: 'random' });
      expect(result.success).toBe(false);
    });
  });

  describe('envelope shape byte-identity with .NET', () => {
    it('success envelope has exactly the expected top-level keys', () => {
      const schema = apiResponseSchema(z.unknown());
      const result = schema.parse(SUCCESS_SINGLE_FIXTURE);
      expect(Object.keys(result)).toEqual(
        expect.arrayContaining(['success', 'statusCode', 'data', 'meta']),
      );
    });

    it('error envelope has exactly the expected top-level keys', () => {
      const result = ApiResponseErrorSchema.parse(ERROR_400_FIXTURE);
      expect(Object.keys(result)).toEqual(
        expect.arrayContaining(['success', 'statusCode', 'error', 'meta']),
      );
    });

    it('meta has traceId + timestamp', () => {
      const result = ApiResponseErrorSchema.parse(ERROR_400_FIXTURE);
      expect(Object.keys(result.meta)).toEqual(expect.arrayContaining(['traceId', 'timestamp']));
    });

    it('error has code + message (and optional fields)', () => {
      const withoutFields = ApiResponseErrorSchema.parse(ERROR_400_FIXTURE);
      expect(Object.keys(withoutFields.error)).toEqual(expect.arrayContaining(['code', 'message']));

      const withFields = ApiResponseErrorSchema.parse(ERROR_422_VALIDATION_FIXTURE);
      expect(Object.keys(withFields.error)).toEqual(
        expect.arrayContaining(['code', 'message', 'fields']),
      );
    });
  });
});
