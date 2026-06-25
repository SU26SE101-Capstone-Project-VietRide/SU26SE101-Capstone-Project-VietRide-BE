describe('instrument.ts', () => {
  const SENTRY_DSN = 'https://key@o0.ingest.us.sentry.io/0';

  beforeEach(() => {
    jest.resetModules();
    jest.restoreAllMocks();
  });

  it('does not call Sentry.init when SENTRY_DSN is empty', () => {
    process.env.SENTRY_DSN = '';
    const sentry = require('@sentry/nestjs');
    const initSpy = jest.spyOn(sentry, 'init');

    require('./instrument');

    expect(initSpy).not.toHaveBeenCalled();
  });

  it('does not call Sentry.init when SENTRY_DSN is undefined', () => {
    delete process.env.SENTRY_DSN;
    const sentry = require('@sentry/nestjs');
    const initSpy = jest.spyOn(sentry, 'init');

    require('./instrument');

    expect(initSpy).not.toHaveBeenCalled();
  });

  it('calls Sentry.init with safe config when SENTRY_DSN is set', () => {
    process.env.SENTRY_DSN = SENTRY_DSN;
    process.env.NODE_ENV = 'test';
    const sentry = require('@sentry/nestjs');
    const initSpy = jest.spyOn(sentry, 'init');

    require('./instrument');

    expect(initSpy).toHaveBeenCalledWith({
      dsn: SENTRY_DSN,
      environment: 'test',
      sendDefaultPii: false,
      tracesSampleRate: 0,
    });
  });
});
