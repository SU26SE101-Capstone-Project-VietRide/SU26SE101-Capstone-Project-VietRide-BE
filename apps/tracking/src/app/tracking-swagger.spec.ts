import type { INestApplication } from '@nestjs/common';
import { SwaggerModule, type OpenAPIObject } from '@nestjs/swagger';
import { setupTrackingSwagger } from './tracking-swagger';

describe('setupTrackingSwagger', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('does not expose Swagger when TRACKING_SWAGGER_ENABLED is false', () => {
    const createDocument = jest.spyOn(SwaggerModule, 'createDocument');
    const setup = jest.spyOn(SwaggerModule, 'setup');

    setupTrackingSwagger({} as INestApplication, false);

    expect(createDocument).not.toHaveBeenCalled();
    expect(setup).not.toHaveBeenCalled();
  });

  it('sets up the Tracking Swagger document when enabled', () => {
    const document = {} as OpenAPIObject;
    const createDocument = jest.spyOn(SwaggerModule, 'createDocument').mockReturnValue(document);
    const setup = jest.spyOn(SwaggerModule, 'setup').mockImplementation(() => undefined);
    const app = {} as INestApplication;

    setupTrackingSwagger(app, true);

    expect(createDocument).toHaveBeenCalledWith(app, expect.any(Object));
    expect(setup).toHaveBeenCalledWith('docs', app, document);
  });
});
