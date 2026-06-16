import { ForbiddenException, Injectable } from '@nestjs/common';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import { RuntimeConfigService } from '../config/runtime-config.service';
import type { RollbackRuntimeConfigDto, UpdateRuntimeConfigDto } from './dto/runtime-config.dto';

const SYSTEM_ADMIN_ROLE = 'SYSTEM_ADMIN';

@Injectable()
export class RuntimeConfigAdminService {
  constructor(private readonly runtimeConfig: RuntimeConfigService) {}

  async list(user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    return this.runtimeConfig.list();
  }

  async get(key: string, user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    return this.runtimeConfig.getDetail(key);
  }

  async reload(user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    await this.runtimeConfig.reload();
    return { reloaded: true };
  }

  async update(key: string, dto: UpdateRuntimeConfigDto, user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    return this.runtimeConfig.update({
      key,
      value: dto.value,
      updatedByUserId: user.sub,
      ...(dto.reason ? { reason: dto.reason } : {}),
    });
  }

  async history(key: string, user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    return (await this.runtimeConfig.getDetail(key)).history;
  }

  async rollback(key: string, dto: RollbackRuntimeConfigDto, user: RagInternalUser | undefined) {
    this.assertSystemAdmin(user);
    return this.runtimeConfig.rollback({
      key,
      historyId: dto.historyId,
      updatedByUserId: user.sub,
    });
  }

  private assertSystemAdmin(user: RagInternalUser | undefined): asserts user is RagInternalUser {
    if (user?.role !== SYSTEM_ADMIN_ROLE) {
      throw new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'SYSTEM_ADMIN role is required',
      });
    }
  }
}
