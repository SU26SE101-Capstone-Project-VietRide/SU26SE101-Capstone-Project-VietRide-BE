import type { AddressInfo } from 'node:net';
import { Test } from '@nestjs/testing';
import { SwaggerModule, type OpenAPIObject } from '@nestjs/swagger';
import { idempotencyParameterMacro, vietRideIdempotencySwaggerPlugin } from './idempotency-swagger';

describe('Gateway idempotency Swagger helpers', () => {
  it('generates UUID v4 only for required Idempotency-Key parameters', () => {
    const requiredOperation = {
      'x-vietride-idempotency': { required: true },
    };

    const value = idempotencyParameterMacro(requiredOperation, {
      name: 'Idempotency-Key',
      in: 'header',
    });

    expect(value).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(
      idempotencyParameterMacro({}, { name: 'Idempotency-Key', in: 'header' }),
    ).toBeUndefined();
    expect(
      idempotencyParameterMacro(requiredOperation, { name: 'Authorization', in: 'header' }),
    ).toBeUndefined();
  });

  it('renders New operation only for required operations and rotates the key on click', () => {
    const changeParamByIdentity = jest.fn();
    const clearResponse = jest.fn();
    const parameter = immutable({ name: 'Idempotency-Key', in: 'header' });
    const operation = immutable({
      'x-vietride-idempotency': immutable({ required: true }),
      parameters: immutableList([parameter]),
    });
    const createElement = jest.fn((type, props, ...children) => ({ type, props, children }));
    const plugin = vietRideIdempotencySwaggerPlugin({ React: { createElement } });
    const original = jest.fn(() => null);
    const wrapped = plugin.wrapComponents.execute(original);

    const rendered = wrapped({
      operation,
      path: '/v1/bookings',
      method: 'post',
      specActions: { changeParamByIdentity, clearResponse },
    }) as { children: Array<{ props: { onClick?: () => void } }> };

    expect(changeParamByIdentity).not.toHaveBeenCalled();
    const newOperationButton = rendered.children[1];
    expect(newOperationButton).toBeDefined();
    newOperationButton?.props.onClick?.();
    expect(changeParamByIdentity).toHaveBeenCalledWith(
      ['/v1/bookings', 'post'],
      parameter,
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
    expect(clearResponse).toHaveBeenCalledWith('/v1/bookings', 'post');
  });

  it('does not render key controls for exempt operations', () => {
    const createElement = jest.fn((type, props, ...children) => ({ type, props, children }));
    const plugin = vietRideIdempotencySwaggerPlugin({ React: { createElement } });
    const original = jest.fn(() => null);
    const wrapped = plugin.wrapComponents.execute(original);
    const props = {
      operation: immutable({}),
      path: '/v1/payments/vnpay-ipn',
      method: 'post',
      specActions: { changeParamByIdentity: jest.fn(), clearResponse: jest.fn() },
    };

    wrapped(props);

    expect(createElement).toHaveBeenCalledTimes(1);
    expect(createElement).toHaveBeenCalledWith(original, props);
  });

  it('serializes the macro and plugin into the canonical docs init script', async () => {
    const moduleRef = await Test.createTestingModule({}).compile();
    const app = moduleRef.createNestApplication();
    SwaggerModule.setup('docs', app, null as unknown as OpenAPIObject, {
      swaggerOptions: {
        parameterMacro: idempotencyParameterMacro,
        plugins: [vietRideIdempotencySwaggerPlugin],
        urls: [{ name: 'Test Service', url: '/api-specs/test' }],
      },
    });

    try {
      await app.listen(0, '127.0.0.1');
      const address = app.getHttpServer().address() as AddressInfo;
      const response = await fetch(`http://127.0.0.1:${address.port}/docs/swagger-ui-init.js`);
      const script = await response.text();

      expect(response.status).toBe(200);
      expect(script).toContain('idempotencyParameterMacro');
      expect(script).toContain('x-vietride-idempotency');
      expect(script).toContain('vietRideIdempotencySwaggerPlugin');
      expect(script).toContain('New operation');
    } finally {
      await app.close();
    }
  });
});

function immutable(values: Record<string, unknown>) {
  return {
    get: (key: string) => values[key],
  };
}

function immutableList(values: unknown[]) {
  return {
    find: (predicate: (value: unknown) => boolean) => values.find(predicate),
  };
}
