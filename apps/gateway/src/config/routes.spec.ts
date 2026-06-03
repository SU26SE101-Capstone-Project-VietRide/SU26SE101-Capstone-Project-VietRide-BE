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

  it('admin routes require SYSTEM_ADMIN role', () => {
    const admin = routes.find((r) => r.prefix === '/v1/admin');
    if (!admin) {
      throw new Error('Expected /v1/admin route to be registered');
    }

    expect(admin.requiredRoles).toContain('SYSTEM_ADMIN');
  });

  it('public auth routes and well-known have authRequired = none', () => {
    const publicPrefixes = [
      '/v1/auth/register',
      '/v1/auth/verify-email',
      '/v1/auth/login',
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
