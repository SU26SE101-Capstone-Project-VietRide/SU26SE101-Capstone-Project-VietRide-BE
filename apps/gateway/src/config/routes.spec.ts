import { buildRouteTable, matchRoute } from './routes';
import { envSchema } from './env.schema';

// Build a fully-defaulted Env via the schema itself so the test stays in sync with the source of truth.
const env = envSchema.parse({
  INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
});

describe('buildRouteTable', () => {
  const routes = buildRouteTable(env);

  it('returns a non-empty route list', () => {
    expect(routes.length).toBeGreaterThan(10);
  });

  it('every Identity route points at IDENTITY_BASE_URL', () => {
    const identityRoutes = routes.filter(
      (r) => r.prefix.startsWith('/v1/auth') || r.prefix.startsWith('/v1/users'),
    );
    expect(identityRoutes.length).toBeGreaterThan(0);
    identityRoutes.forEach((r) => expect(r.target).toBe(env.IDENTITY_BASE_URL));
  });

  it('health passthrough routes use rewriteTo "/health"', () => {
    const healthRoutes = routes.filter((r) => r.prefix.endsWith('/health'));
    expect(healthRoutes.length).toBeGreaterThanOrEqual(5);
    healthRoutes.forEach((r) => {
      expect(r.rewriteTo).toBe('/health');
      expect(r.authRequired).toBe('none');
    });
  });

  it('admin routes require SYSTEM_ADMIN role and point at their owning services', () => {
    const expectedAdminRoutes = [
      ['/v1/admin/operators', env.IDENTITY_BASE_URL],
      ['/v1/admin/users', env.IDENTITY_BASE_URL],
      ['/v1/admin/booking-stats', env.BOOKING_BASE_URL],
      ['/v1/admin/trip-settlements', env.PAYMENT_BASE_URL],
      ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
    ] as const;

    expect(routes.find((r) => r.prefix === '/v1/admin')).toBeUndefined();

    expectedAdminRoutes.forEach(([prefix, target]) => {
      const adminRoute = routes.find((r) => r.prefix === prefix);
      if (!adminRoute) {
        throw new Error(`Expected ${prefix} route to be registered`);
      }

      expect(adminRoute.target).toBe(target);
      expect(adminRoute.authRequired).toBe('user');
      expect(adminRoute.requiredRoles).toEqual(['SYSTEM_ADMIN']);
    });
  });

  it('matches cross-service admin routes to the correct upstream services', () => {
    const cases = [
      ['/v1/admin/operators', env.IDENTITY_BASE_URL],
      ['/v1/admin/operators/11111111-1111-1111-1111-111111111111/approve', env.IDENTITY_BASE_URL],
      ['/v1/admin/users', env.IDENTITY_BASE_URL],
      ['/v1/admin/booking-stats/aggregate', env.BOOKING_BASE_URL],
      ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
      [
        '/v1/admin/trip-settlements/11111111-1111-1111-1111-111111111111/settle',
        env.PAYMENT_BASE_URL,
      ],
    ] as const;

    cases.forEach(([path, target]) => {
      const route = matchRoute(routes, path);

      expect(route?.target).toBe(target);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['SYSTEM_ADMIN']);
    });
  });

  it('public auth routes and well-known have authRequired = none', () => {
    const publicPrefixes = [
      '/v1/auth/register',
      '/v1/auth/verify-email',
      '/v1/auth/login',
      '/v1/auth/google',
      '/v1/auth/refresh',
      '/v1/.well-known',
    ];
    publicPrefixes.forEach((p) => {
      const route = routes.find((r) => r.prefix === p);
      if (!route) {
        throw new Error(`Expected ${p} route to be registered`);
      }

      expect(route.authRequired).toBe('none');
    });
  });

  it('logout and other non-public auth paths require user auth', () => {
    const logout = matchRoute(routes, '/v1/auth/logout');
    const changePassword = matchRoute(routes, '/v1/auth/change-password');

    expect(logout?.authRequired).toBe('user');
    expect(changePassword?.authRequired).toBe('user');
  });

  it('prefixes are unique', () => {
    const prefixes = routes.map((r) => r.prefix);
    const set = new Set(prefixes);
    expect(set.size).toBe(prefixes.length);
  });
});
