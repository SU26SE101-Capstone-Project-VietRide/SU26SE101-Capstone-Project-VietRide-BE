import { BadRequestException, ForbiddenException, Injectable, NotFoundException, OnModuleInit } from '@nestjs/common';
import pino from 'pino';
import {
  RAG_RUNTIME_CONFIG_DEFINITION_BY_KEY,
  RAG_RUNTIME_CONFIG_DEFINITIONS,
  type RuntimeConfigDefinition,
  type RuntimeConfigKey,
  type RuntimeConfigValue,
} from './runtime-config.registry';
import {
  RuntimeConfigRepository,
  type RuntimeConfigHistoryRow,
  type RuntimeConfigRow,
} from './runtime-config.repository';

export interface RuntimeConfigItem {
  key: string;
  value: RuntimeConfigValue;
  valueType: string;
  editableGroup: string;
  riskLevel: string;
  requiresRestart: boolean;
  description: string;
  updatedByUserId: string | null;
  updatedAt: Date | null;
}

export interface RuntimeConfigDetail extends RuntimeConfigItem {
  history: RuntimeConfigHistoryRow[];
}

const logger = pino({ name: 'RuntimeConfigService' });

@Injectable()
export class RuntimeConfigService implements OnModuleInit {
  // Capstone deployment is single-replica, so update() refreshes this in-process cache immediately.
  // In multi-replica production, publish a Redis pub/sub event here and call reload() on subscribers.
  private cache = new Map<RuntimeConfigKey, RuntimeConfigValue>();
  private loaded = false;

  constructor(private readonly repository: RuntimeConfigRepository) {}

  async onModuleInit(): Promise<void> {
    await this.reload();
  }

  async reload(): Promise<void> {
    const rows = await this.repository.findAll();
    const next = new Map<RuntimeConfigKey, RuntimeConfigValue>();
    for (const definition of RAG_RUNTIME_CONFIG_DEFINITIONS) {
      const row = rows.find((item) => item.key === definition.key);
      next.set(definition.key, this.valueFromRowOrDefault(definition, row));
    }
    this.cache = next;
    this.loaded = true;
    logger.info({ keys: next.size }, 'RAG runtime config loaded');
  }

  async getSnapshot(): Promise<RuntimeConfigSnapshot> {
    await this.ensureLoaded();
    return new RuntimeConfigSnapshot(new Map(this.cache));
  }

  async list(): Promise<RuntimeConfigItem[]> {
    const rows = await this.repository.findAll();
    return RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => {
      const row = rows.find((item) => item.key === definition.key);
      return this.toItem(definition, row);
    });
  }

  async getDetail(key: string): Promise<RuntimeConfigDetail> {
    const definition = this.assertKnownKey(key);
    const row = await this.repository.findByKey(key);
    const history = await this.repository.listHistory(key);
    return { ...this.toItem(definition, row), history };
  }

  async update(input: {
    key: string;
    value: unknown;
    updatedByUserId: string;
    reason?: string;
  }): Promise<RuntimeConfigItem> {
    const definition = this.assertKnownKey(input.key);
    this.assertEditable(definition);
    const value = this.validateValue(definition, input.value);
    const row = await this.repository.updateValue({
      key: definition.key,
      value,
      valueType: definition.valueType,
      editableGroup: definition.editableGroup,
      riskLevel: definition.riskLevel,
      requiresRestart: definition.requiresRestart,
      description: definition.description,
      updatedByUserId: input.updatedByUserId,
      ...(input.reason ? { reason: input.reason } : {}),
    });
    this.cache.set(definition.key, row.value);
    return this.toItem(definition, row);
  }

  async rollback(input: {
    key: string;
    historyId: string;
    updatedByUserId: string;
  }): Promise<RuntimeConfigItem> {
    const definition = this.assertKnownKey(input.key);
    this.assertEditable(definition);
    const history = await this.repository.listHistory(input.key);
    const target = history.find((item) => item.id === input.historyId);
    if (!target) {
      throw new NotFoundException({
        errorCode: 'RUNTIME_CONFIG_HISTORY_NOT_FOUND',
        detail: `Runtime config history ${input.historyId} not found`,
      });
    }
    return this.update({
      key: input.key,
      value: target.oldValue ?? definition.defaultValue,
      updatedByUserId: input.updatedByUserId,
      reason: `Rollback to history ${input.historyId}`,
    });
  }

  private async ensureLoaded(): Promise<void> {
    if (!this.loaded) {
      await this.reload();
    }
  }

  private valueFromRowOrDefault(definition: RuntimeConfigDefinition, row: RuntimeConfigRow | undefined): RuntimeConfigValue {
    if (!row) return definition.defaultValue;
    try {
      return this.validateValue(definition, row.value);
    } catch (error) {
      logger.warn({ key: definition.key, error: this.toSafeError(error) }, 'Invalid runtime config value; using default');
      return definition.defaultValue;
    }
  }

  private toItem(definition: RuntimeConfigDefinition, row: RuntimeConfigRow | null | undefined): RuntimeConfigItem {
    return {
      key: definition.key,
      value: this.valueFromRowOrDefault(definition, row ?? undefined),
      valueType: definition.valueType,
      editableGroup: definition.editableGroup,
      riskLevel: definition.riskLevel,
      requiresRestart: definition.requiresRestart,
      description: definition.description,
      updatedByUserId: row?.updatedByUserId ?? null,
      updatedAt: row?.updatedAt ?? null,
    };
  }

  private assertKnownKey(key: string): RuntimeConfigDefinition {
    const definition = RAG_RUNTIME_CONFIG_DEFINITION_BY_KEY.get(key as RuntimeConfigKey);
    if (!definition) {
      throw new NotFoundException({
        errorCode: 'RUNTIME_CONFIG_NOT_FOUND',
        detail: `Runtime config ${key} not found`,
      });
    }
    return definition;
  }

  private assertEditable(definition: RuntimeConfigDefinition): void {
    if (definition.editableGroup === 'readonly') {
      throw new ForbiddenException({
        errorCode: 'RUNTIME_CONFIG_READONLY',
        detail: `Runtime config ${definition.key} is read-only`,
      });
    }
  }

  private validateValue(definition: RuntimeConfigDefinition, value: unknown): RuntimeConfigValue {
    try {
      return definition.validate(value);
    } catch (error) {
      throw new BadRequestException({
        errorCode: 'RUNTIME_CONFIG_INVALID_VALUE',
        detail: error instanceof Error ? error.message : `Runtime config ${definition.key} is invalid`,
      });
    }
  }

  private toSafeError(error: unknown): { name: string } {
    return { name: error instanceof Error ? error.name : 'UnknownError' };
  }
}

export class RuntimeConfigSnapshot {
  constructor(private readonly values: Map<RuntimeConfigKey, RuntimeConfigValue>) {}

  getString(key: RuntimeConfigKey): string {
    const value = this.values.get(key);
    if (typeof value !== 'string') {
      throw new Error(`${key} must be a string`);
    }
    return value;
  }

  getNumber(key: RuntimeConfigKey): number {
    const value = this.values.get(key);
    if (typeof value !== 'number') {
      throw new Error(`${key} must be a number`);
    }
    return value;
  }

  getStringList(key: RuntimeConfigKey): string[] {
    const value = this.values.get(key);
    if (!Array.isArray(value) || value.some((item) => typeof item !== 'string')) {
      throw new Error(`${key} must be a string list`);
    }
    return value;
  }
}
