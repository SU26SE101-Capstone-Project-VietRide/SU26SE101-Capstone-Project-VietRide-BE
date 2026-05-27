import { buildRouteTable } from './routes';
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
    const identityRoutes = routes.filter(r => r.prefix.startsWith('/v1/auth') || r.prefix.startsWith('/v1/users'));
    expect(identityRoutes.length).toBeGreaterThan(0);
    identityRoutes.forEach(r => expect(r.target).toBe(env.IDENTITY_BASE_URL));
  });

  it('health passthrough routes use rewriteTo "/health"', () => {
    const healthRoutes = routes.filter(r => r.prefix.endsWith('/health'));
    expect(healthRoutes.length).toBeGreaterThanOrEqual(5);
    healthRoutes.forEach(r => {
      expect(r.rewriteTo).toBe('/health');
      expect(r.authRequired).toBe('none');
    });
  });

  it('admin routes require SYSTEM_ADMIN role', () => {
    const admin = routes.find(r => r.prefix === '/v1/admin');
    expect(admin).toBeDefined();
    expect(admin!.requiredRoles).toContain('SYSTEM_ADMIN');
  });

  it('public routes (auth, well-known) have authRequired = none', () => {
    const publicPrefixes = ['/v1/auth', '/v1/.well-known'];
    publicPrefixes.forEach(p => {
      const route = routes.find(r => r.prefix === p);
      expect(route).toBeDefined();
      expect(route!.authRequired).toBe('none');
    });
  });

  it('prefixes are unique', () => {
    const prefixes = routes.map(r => r.prefix);
    const set = new Set(prefixes);
    expect(set.size).toBe(prefixes.length);
  });
});
