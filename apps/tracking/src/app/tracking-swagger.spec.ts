import type { INestApplication } from '@nestjs/common';
import { SwaggerModule, type OpenAPIObject } from '@nestjs/swagger';
import { setupTrackingSwagger } from './tracking-swagger';

describe('setupTrackingSwagger', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('keeps the raw document available when TRACKING_SWAGGER_ENABLED is false', () => {
    const document = {} as OpenAPIObject;
    const createDocument = jest.spyOn(SwaggerModule, 'createDocument');
    const setup = jest.spyOn(SwaggerModule, 'setup');
    createDocument.mockReturnValue(document);
    setup.mockImplementation(() => undefined);
    const app = {} as INestApplication;

    setupTrackingSwagger(app, false);

    expect(createDocument).toHaveBeenCalledWith(app, expect.any(Object));
    expect(setup).toHaveBeenCalledWith('docs', app, document, {
      ui: false,
      raw: ['json'],
    });
  });

  it('sets up the Tracking Swagger document when enabled', () => {
    const document = {} as OpenAPIObject;
    const createDocument = jest.spyOn(SwaggerModule, 'createDocument').mockReturnValue(document);
    const setup = jest.spyOn(SwaggerModule, 'setup').mockImplementation(() => undefined);
    const app = {} as INestApplication;

    setupTrackingSwagger(app, true);

    expect(createDocument).toHaveBeenCalledWith(app, expect.any(Object));
    expect(setup).toHaveBeenCalledWith('docs', app, document, {
      ui: true,
      raw: ['json'],
    });
  });
});
