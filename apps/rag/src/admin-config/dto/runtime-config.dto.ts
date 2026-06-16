import { z } from 'zod';

export const RuntimeConfigKeyParamSchema = z.object({
  key: z.string().min(1).max(120),
});

export const UpdateRuntimeConfigSchema = z.object({
  value: z.unknown(),
  reason: z.string().trim().min(1).max(500).optional(),
});

export const RollbackRuntimeConfigSchema = z.object({
  historyId: z.string().uuid(),
});

export type RuntimeConfigKeyParamDto = z.infer<typeof RuntimeConfigKeyParamSchema>;
export type UpdateRuntimeConfigDto = z.infer<typeof UpdateRuntimeConfigSchema>;
export type RollbackRuntimeConfigDto = z.infer<typeof RollbackRuntimeConfigSchema>;
