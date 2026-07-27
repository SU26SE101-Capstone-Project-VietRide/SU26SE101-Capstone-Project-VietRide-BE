import './bootstrap-env';

import * as Sentry from '@sentry/nestjs';
import { redactSensitiveValue } from './notifications/sensitive-data-redaction';

const dsn = process.env.SENTRY_DSN;
if (dsn && dsn.length > 0) {
  Sentry.init({
    dsn,
    environment: process.env.NODE_ENV ?? 'development',
    sendDefaultPii: false,
    tracesSampleRate: 0,
    beforeSend: (event) => redactSensitiveValue(event) as typeof event,
  });
}
