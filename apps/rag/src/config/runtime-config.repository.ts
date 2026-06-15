import { Injectable } from '@nestjs/common';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import type { RuntimeConfigValue } from './runtime-config.registry';

export interface RuntimeConfigRow {
  key: string;
  value: RuntimeConfigValue;
  valueType: string;
  editableGroup: string;
  riskLevel: string;
  requiresRestart: boolean;
  description: string | null;
  updatedByUserId: string | null;
  createdAt: Date;
  updatedAt: Date;
}

export interface RuntimeConfigHistoryRow {
  id: string;
  key: string;
  oldValue: RuntimeConfigValue | null;
  newValue: RuntimeConfigValue;
  changedByUserId: string | null;
  reason: string | null;
  changedAt: Date;
}

@Injectable()
export class RuntimeConfigRepository {
  constructor(private readonly prisma: RagPrismaService) {}

  async findAll(): Promise<RuntimeConfigRow[]> {
    return this.prisma.$queryRaw<RuntimeConfigRow[]>`
      SELECT
        key,
        value,
        value_type AS "valueType",
        editable_group AS "editableGroup",
        risk_level AS "riskLevel",
        requires_restart AS "requiresRestart",
        description,
        updated_by_user_id::text AS "updatedByUserId",
        created_at AS "createdAt",
        updated_at AS "updatedAt"
      FROM vietride_rag.runtime_configs
      ORDER BY key ASC
    `;
  }

  async findByKey(key: string): Promise<RuntimeConfigRow | null> {
    const rows = await this.prisma.$queryRaw<RuntimeConfigRow[]>`
      SELECT
        key,
        value,
        value_type AS "valueType",
        editable_group AS "editableGroup",
        risk_level AS "riskLevel",
        requires_restart AS "requiresRestart",
        description,
        updated_by_user_id::text AS "updatedByUserId",
        created_at AS "createdAt",
        updated_at AS "updatedAt"
      FROM vietride_rag.runtime_configs
      WHERE key = ${key}
      LIMIT 1
    `;
    return rows[0] ?? null;
  }

  async listHistory(key: string): Promise<RuntimeConfigHistoryRow[]> {
    return this.prisma.$queryRaw<RuntimeConfigHistoryRow[]>`
      SELECT
        id::text,
        key,
        old_value AS "oldValue",
        new_value AS "newValue",
        changed_by_user_id::text AS "changedByUserId",
        reason,
        changed_at AS "changedAt"
      FROM vietride_rag.runtime_config_history
      WHERE key = ${key}
      ORDER BY changed_at DESC
    `;
  }

  async updateValue(input: {
    key: string;
    value: RuntimeConfigValue;
    valueType: string;
    editableGroup: string;
    riskLevel: string;
    requiresRestart: boolean;
    description: string;
    updatedByUserId: string;
    reason?: string;
  }): Promise<RuntimeConfigRow> {
    const old = await this.findByKey(input.key);
    const valueJson = JSON.stringify(input.value);
    const oldValueJson = old ? JSON.stringify(old.value) : null;

    await this.prisma.$transaction(async (tx) => {
      await tx.$executeRaw`
        INSERT INTO vietride_rag.runtime_configs (
          key,
          value,
          value_type,
          editable_group,
          risk_level,
          requires_restart,
          description,
          updated_by_user_id
        )
        VALUES (
          ${input.key},
          ${valueJson}::jsonb,
          ${input.valueType},
          ${input.editableGroup},
          ${input.riskLevel},
          ${input.requiresRestart},
          ${input.description},
          ${input.updatedByUserId}::uuid
        )
        ON CONFLICT (key) DO UPDATE SET
          value = EXCLUDED.value,
          value_type = EXCLUDED.value_type,
          editable_group = EXCLUDED.editable_group,
          risk_level = EXCLUDED.risk_level,
          requires_restart = EXCLUDED.requires_restart,
          description = EXCLUDED.description,
          updated_by_user_id = EXCLUDED.updated_by_user_id
      `;

      await tx.$executeRaw`
        INSERT INTO vietride_rag.runtime_config_history (
          key,
          old_value,
          new_value,
          changed_by_user_id,
          reason
        )
        VALUES (
          ${input.key},
          ${oldValueJson}::jsonb,
          ${valueJson}::jsonb,
          ${input.updatedByUserId}::uuid,
          ${input.reason ?? null}
        )
      `;
    });

    return (await this.findByKey(input.key)) as RuntimeConfigRow;
  }
}
