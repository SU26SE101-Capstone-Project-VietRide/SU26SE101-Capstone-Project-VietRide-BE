import { Module } from '@nestjs/common';
import { RuntimeConfigAdminController } from './runtime-config-admin.controller';
import { RuntimeConfigAdminService } from './runtime-config-admin.service';

@Module({
  controllers: [RuntimeConfigAdminController],
  providers: [RuntimeConfigAdminService],
})
export class RuntimeConfigAdminModule {}
