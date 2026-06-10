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
      (r) =>
        r.prefix.startsWith('/v1/auth') ||
        r.prefix.startsWith('/v1/users') ||
        r.prefix.startsWith('/v1/operator/profile') ||
        r.prefix.startsWith('/v1/operator/users'),
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
      '/v1/auth/set-initial-password',
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

  it('matches set-initial-password to the dedicated public auth route', () => {
    const route = matchRoute(routes, '/v1/auth/set-initial-password');

    expect(route?.prefix).toBe('/v1/auth/set-initial-password');
    expect(route?.authRequired).toBe('none');
    expect(route?.target).toBe(env.IDENTITY_BASE_URL);
  });

  it('logout and other non-public auth paths require user auth', () => {
    const logout = matchRoute(routes, '/v1/auth/logout');
    const changePassword = matchRoute(routes, '/v1/auth/change-password');

    expect(logout?.authRequired).toBe('user');
    expect(changePassword?.authRequired).toBe('user');
  });

  it('routes the passenger stub family to Identity requiring user auth', () => {
    const meRoute = matchRoute(routes, '/v1/passenger/me');
    const bookingsRoute = matchRoute(routes, '/v1/passenger/bookings');

    [meRoute, bookingsRoute].forEach((route) => {
      expect(route?.prefix).toBe('/v1/passenger');
      expect(route?.target).toBe(env.IDENTITY_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toBeUndefined();
    });
  });

  it('operator profile routes allow OPERATOR_ADMIN and OPERATOR_STAFF roles', () => {
    const route = matchRoute(routes, '/v1/operator/profile');

    expect(route?.prefix).toBe('/v1/operator/profile');
    expect(route?.target).toBe(env.IDENTITY_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
  });

  it('operator profile and operator users routes match distinctly with no generic operator prefix', () => {
    const profileRoute = matchRoute(routes, '/v1/operator/profile');
    const usersRoute = matchRoute(
      routes,
      '/v1/operator/users/11111111-1111-1111-1111-111111111111/resend-initial-password',
    );

    expect(profileRoute?.prefix).toBe('/v1/operator/profile');
    expect(usersRoute?.prefix).toBe('/v1/operator/users');
    expect(profileRoute).not.toBe(usersRoute);
    expect(routes.find((r) => r.prefix === '/v1/operator')).toBeUndefined();
  });

  it('operator user routes require OPERATOR_ADMIN role', () => {
    const route = matchRoute(
      routes,
      '/v1/operator/users/11111111-1111-1111-1111-111111111111/resend-initial-password',
    );

    expect(route?.prefix).toBe('/v1/operator/users');
    expect(route?.target).toBe(env.IDENTITY_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN']);
  });

  it('routes Day 7 station and operator stop families to Trip with operator role union', () => {
    const cases = [
      ['/v1/stations/search?q=Mien%20Tay', '/v1/stations'],
      ['/v1/operator/stations', '/v1/operator/stations'],
      ['/v1/operator/stops', '/v1/operator/stops'],
      ['/v1/operator/stops/11111111-1111-1111-1111-111111111111', '/v1/operator/stops'],
    ] as const;

    cases.forEach(([path, prefix]) => {
      const [pathname] = path.split('?');
      const route = matchRoute(routes, pathname ?? path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(env.TRIP_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    });
  });

  it('keeps existing Identity operator routes distinct from Trip operator routes', () => {
    const profileRoute = matchRoute(routes, '/v1/operator/profile');
    const usersRoute = matchRoute(routes, '/v1/operator/users');
    const operatorStationsRoute = matchRoute(routes, '/v1/operator/stations');
    const operatorStopsRoute = matchRoute(routes, '/v1/operator/stops');

    expect(profileRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(usersRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(operatorStationsRoute?.target).toBe(env.TRIP_BASE_URL);
    expect(operatorStopsRoute?.target).toBe(env.TRIP_BASE_URL);
    expect(routes.find((r) => r.prefix === '/v1/operator')).toBeUndefined();
  });

  it('does not register a separate station search route or stop delete route', () => {
    expect(routes.find((r) => r.prefix === '/v1/stations/search')).toBeUndefined();
    expect(routes.find((r) => r.prefix === '/v1/operator/stops/delete')).toBeUndefined();
  });

  it('prefixes are unique', () => {
    const prefixes = routes.map((r) => r.prefix);
    const set = new Set(prefixes);
    expect(set.size).toBe(prefixes.length);
  });
});
