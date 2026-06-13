import { Module } from '@nestjs/common';
import { ProvidersModule } from '../providers/providers.module';
import { DocumentsController } from './documents.controller';
import { DocumentsRepository } from './documents.repository';
import { DocumentsService } from './documents.service';

@Module({
  imports: [ProvidersModule],
  controllers: [DocumentsController],
  providers: [DocumentsService, DocumentsRepository],
})
export class DocumentsModule {}
