import { Global, Module } from '@nestjs/common';
import { RagPrismaService } from './rag-prisma.service';

@Global()
@Module({
  providers: [RagPrismaService],
  exports: [RagPrismaService],
})
export class RagPrismaModule {}
