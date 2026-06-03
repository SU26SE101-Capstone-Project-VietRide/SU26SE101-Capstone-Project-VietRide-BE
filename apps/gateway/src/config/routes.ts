import type { Env } from './env.schema';

/** Auth requirement per route. */
export type AuthMode = 'none' | 'user' | 'mixed';

export interface ProxyRoute {
  /** URL prefix matched against incoming request. */
  prefix: string;
  /** Downstream service base URL. */
  target: string;
  /** Auth mode. 'mixed' = some sub-paths public, others require user JWT (handled in middleware whitelist). */
  authRequired: AuthMode;
  /** Optional RBAC requirement. */
  requiredRoles?: string[];
  /** Strip prefix before forwarding? Default false. Cannot be combined with rewriteTo. */
  stripPrefix?: boolean;
  /** Rewrite the matched prefix to a different path before forwarding (e.g., /v1/identity/health → /health). */
  rewriteTo?: string;
}

/**
 * Config-driven route table. Add/edit here when wiring a new endpoint family.
 * Per BACKEND_SOURCE_OF_TRUTH 3.4.2.
 */
export function buildRouteTable(env: Env): ProxyRoute[] {
  return [
    // Identity
    { prefix: '/v1/auth/register', target: env.IDENTITY_BASE_URL, authRequired: 'none' },
    { prefix: '/v1/auth/verify-email', target: env.IDENTITY_BASE_URL, authRequired: 'none' },
    { prefix: '/v1/auth/login', target: env.IDENTITY_BASE_URL, authRequired: 'none' },
    { prefix: '/v1/auth/refresh', target: env.IDENTITY_BASE_URL, authRequired: 'none' },
    { prefix: '/v1/auth/logout', target: env.IDENTITY_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/auth', target: env.IDENTITY_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/users', target: env.IDENTITY_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/operators', target: env.IDENTITY_BASE_URL, authRequired: 'mixed' },
    {
      prefix: '/v1/admin',
      target: env.IDENTITY_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['SYSTEM_ADMIN'],
    },
    { prefix: '/v1/.well-known', target: env.IDENTITY_BASE_URL, authRequired: 'none' },
    // Day 2 placeholder health passthrough — each downstream service has /health.
    // Convention: /v1/<service>/health → service /health (FE tests connectivity).
    {
      prefix: '/v1/identity/health',
      target: env.IDENTITY_BASE_URL,
      authRequired: 'none',
      rewriteTo: '/health',
    },

    // Trip / Vehicle
    { prefix: '/v1/trips', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/routes', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/stations', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/stops', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/vehicles', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/driver', target: env.TRIP_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/assistant', target: env.TRIP_BASE_URL, authRequired: 'user' },
    {
      prefix: '/v1/trip/health',
      target: env.TRIP_BASE_URL,
      authRequired: 'none',
      rewriteTo: '/health',
    },

    // Booking
    { prefix: '/v1/bookings', target: env.BOOKING_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/vouchers', target: env.BOOKING_BASE_URL, authRequired: 'user' },
    {
      prefix: '/v1/booking/health',
      target: env.BOOKING_BASE_URL,
      authRequired: 'none',
      rewriteTo: '/health',
    },

    // Payment
    {
      prefix: '/v1/payments',
      target: env.PAYMENT_BASE_URL,
      authRequired: 'mixed' /* vnpay-ipn public */,
    },
    { prefix: '/v1/wallet', target: env.PAYMENT_BASE_URL, authRequired: 'user' },
    {
      prefix: '/v1/payment/health',
      target: env.PAYMENT_BASE_URL,
      authRequired: 'none',
      rewriteTo: '/health',
    },

    // Parcel
    { prefix: '/v1/parcels', target: env.PARCEL_BASE_URL, authRequired: 'user' },
    {
      prefix: '/v1/parcel/health',
      target: env.PARCEL_BASE_URL,
      authRequired: 'none',
      rewriteTo: '/health',
    },

    // NestJS services
    { prefix: '/v1/notifications', target: env.NOTIFICATION_BASE_URL, authRequired: 'user' },
    { prefix: '/v1/rag', target: env.RAG_BASE_URL, authRequired: 'user' },
    // /tracking/socket.io/* is NOT routed via Gateway (Nginx direct upgrade).
  ];
}

/** Find first prefix-matching route. */
export function matchRoute(table: ProxyRoute[], path: string): ProxyRoute | undefined {
  // Longest prefix wins so /v1/identity/health beats /v1/auth.
  return table
    .filter((r) => path === r.prefix || path.startsWith(r.prefix + '/'))
    .sort((a, b) => b.prefix.length - a.prefix.length)[0];
}
