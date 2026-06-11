import { z } from 'zod';

export const DEFAULT_NOTIFICATION_PAGE = 1;
export const DEFAULT_NOTIFICATION_PAGE_SIZE = 20;
export const MAX_NOTIFICATION_PAGE_SIZE = 100;

const SortBySchema = z.enum(['createdAt', 'readAt', 'type']);
const SortDirSchema = z.enum(['asc', 'desc']);

export const ListNotificationsQuerySchema = z.object({
  unreadOnly: z
    .preprocess((value) => {
      if (value === undefined) return false;
      if (value === true || value === 'true') return true;
      if (value === false || value === 'false') return false;
      return value;
    }, z.boolean())
    .default(false),
  page: z.coerce.number().int().min(1).default(DEFAULT_NOTIFICATION_PAGE),
  pageSize: z.coerce
    .number()
    .int()
    .min(1)
    .max(MAX_NOTIFICATION_PAGE_SIZE)
    .default(DEFAULT_NOTIFICATION_PAGE_SIZE),
  sortBy: SortBySchema.default('createdAt'),
  sortDir: SortDirSchema.default('desc'),
});

export type ListNotificationsQueryDto = z.infer<typeof ListNotificationsQuerySchema>;
