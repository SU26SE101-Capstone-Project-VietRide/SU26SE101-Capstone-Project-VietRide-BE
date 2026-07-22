import { Controller, Post } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { ApiIdempotencyRequired } from './idempotency.swagger';

@Controller('v1/test')
class IdempotencyFixtureController {
  @Post('required')
  @ApiIdempotencyRequired()
  required(): void {
    return;
  }

  @Post('optional')
  optional(): void {
    return;
  }
}

describe('ApiIdempotencyRequired', () => {
  it('documents the required UUID header and machine-readable extension', async () => {
    const moduleRef = await Test.createTestingModule({
      controllers: [IdempotencyFixtureController],
    }).compile();
    const app = moduleRef.createNestApplication();
    const document = SwaggerModule.createDocument(
      app,
      new DocumentBuilder().setTitle('test').setVersion('v1').build(),
    );

    const required = document.paths['/v1/test/required']?.post;
    expect(required?.parameters).toContainEqual(
      expect.objectContaining({
        name: 'Idempotency-Key',
        in: 'header',
        required: true,
        schema: expect.objectContaining({ type: 'string', format: 'uuid' }),
      }),
    );
    expect((required as unknown as Record<string, unknown>)['x-vietride-idempotency']).toEqual({
      required: true,
      keyFormat: 'uuid-v4',
      ttlSeconds: 86_400,
    });
    expect(document.paths['/v1/test/optional']?.post?.parameters ?? []).toHaveLength(0);

    await app.close();
  });
});
