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

  it('routes Firebase custom tokens and unified passenger history to their owners', () => {
    const firebaseRoute = matchRoute(routes, '/v1/firebase/custom-token');
    expect(firebaseRoute).toMatchObject({
      target: env.IDENTITY_BASE_URL,
      authRequired: 'user',
    });
    expect(firebaseRoute?.requiredRoles).toBeUndefined();

    const historyRoute = matchRoute(routes, '/v1/passenger/history');
    expect(historyRoute).toMatchObject({
      target: env.PARCEL_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['PASSENGER'],
    });
    expect(matchRoute(routes, '/v1/passenger')).toMatchObject({ target: env.IDENTITY_BASE_URL });
  });

  it('every Identity route points at IDENTITY_BASE_URL', () => {
    const identityRoutes = routes.filter(
      (r) =>
        r.prefix.startsWith('/v1/auth') ||
        r.prefix.startsWith('/v1/users') ||
        r.prefix.startsWith('/v1/operator/profile') ||
        r.prefix.startsWith('/v1/operator/users') ||
        r.prefix.startsWith('/v1/operator/subscription') ||
        r.prefix.startsWith('/v1/admin/operator-users') ||
        r.prefix.startsWith('/v1/admin/subscription-plans'),
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
      ['/v1/admin/operator-users', env.IDENTITY_BASE_URL],
      ['/v1/admin/users', env.IDENTITY_BASE_URL],
      ['/v1/admin/activity-logs', env.IDENTITY_BASE_URL],
      ['/v1/admin/outbox/dlq', env.IDENTITY_BASE_URL],
      ['/v1/admin/subscription-plans', env.IDENTITY_BASE_URL],
      ['/v1/admin/locations', env.TRIP_BASE_URL],
      ['/v1/admin/stations', env.TRIP_BASE_URL],
      ['/v1/admin/booking-stats', env.BOOKING_BASE_URL],
      ['/v1/admin/vouchers', env.BOOKING_BASE_URL],
      ['/v1/admin/trip-settlements', env.PAYMENT_BASE_URL],
      ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
      ['/v1/admin/invoices', env.PAYMENT_BASE_URL],
      ['/v1/admin/reports/platform', env.BOOKING_BASE_URL],
      ['/v1/admin/dashboard/summary', env.BOOKING_BASE_URL],
      ['/v1/admin/revenue/analytics', env.PAYMENT_BASE_URL],
    ] as const;

    expect(routes.find((r) => r.prefix === '/v1/admin')).toBeUndefined();

    expectedAdminRoutes.forEach(([prefix, target]) => {
      const adminRoute = routes.find((r) => r.prefix === prefix && !r.pathPattern);
      if (!adminRoute) {
        throw new Error(`Expected ${prefix} route to be registered`);
      }

      expect(adminRoute.target).toBe(target);
      expect(adminRoute.authRequired).toBe('user');
      expect(adminRoute.requiredRoles).toEqual(['SYSTEM_ADMIN']);
    });
  });

  it('routes Day 38 finance APIs and the dynamic operator adjustment to Payment', () => {
    const expected = [
      ['/v1/operator/wallet', ['OPERATOR_ADMIN', 'OPERATOR_STAFF']],
      ['/v1/operator/wallet/transactions', ['OPERATOR_ADMIN', 'OPERATOR_STAFF']],
      ['/v1/operator/trip-settlements', ['OPERATOR_ADMIN', 'OPERATOR_STAFF']],
      ['/v1/operator/ledger', ['OPERATOR_ADMIN', 'OPERATOR_STAFF']],
      ['/v1/operator/invoices', ['OPERATOR_ADMIN']],
      ['/v1/operator/invoices/11111111-1111-1111-1111-111111111111/download', ['OPERATOR_ADMIN']],
      ['/v1/admin/invoices/11111111-1111-1111-1111-111111111111/retry', ['SYSTEM_ADMIN']],
      ['/v1/admin/operators/11111111-1111-1111-1111-111111111111/wallet/adjust', ['SYSTEM_ADMIN']],
    ] as const;

    expected.forEach(([path, roles]) => {
      const route = matchRoute(routes, path);
      expect(route?.target).toBe(env.PAYMENT_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(roles);
    });

    expect(matchRoute(routes, '/v1/admin/operators')).toMatchObject({
      target: env.IDENTITY_BASE_URL,
    });
    expect(
      matchRoute(routes, '/v1/driver/trips/11111111-1111-1111-1111-111111111111/complete'),
    ).toMatchObject({
      target: env.TRIP_BASE_URL,
      requiredRoles: ['DRIVER', 'ASSISTANT'],
    });
  });

  it('routes booking family to Booking and requires PASSENGER role', () => {
    const bookingRoute = matchRoute(routes, '/v1/bookings');
    const bookingHistoryRoute = matchRoute(routes, '/v1/bookings/history');

    [bookingRoute, bookingHistoryRoute].forEach((route) => {
      expect(route?.prefix).toBe('/v1/bookings');
      expect(route?.target).toBe(env.BOOKING_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['PASSENGER']);
    });
  });

  it('routes generic Policy APIs to RAG with exact admin roles', () => {
    const adminRoute = matchRoute(
      routes,
      '/v1/admin/policies/11111111-1111-4111-8111-111111111111',
    );
    const operatorRoute = matchRoute(
      routes,
      '/v1/operator/policies/22222222-2222-4222-8222-222222222222',
    );

    expect(adminRoute).toMatchObject({
      prefix: '/v1/admin/policies',
      target: env.RAG_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['SYSTEM_ADMIN'],
    });
    expect(matchRoute(routes, '/v1/admin/policies')).toBe(adminRoute);
    expect(adminRoute?.forwardUserAuthorization).not.toBe(true);
    expect(operatorRoute).toMatchObject({
      prefix: '/v1/operator/policies',
      target: env.RAG_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN'],
    });
    expect(matchRoute(routes, '/v1/operator/policies')).toBe(operatorRoute);
    expect(operatorRoute?.forwardUserAuthorization).not.toBe(true);
  });

  it('routes all six operator XLSX reports to their owning services with operator roles', () => {
    const expected = [
      ['/v1/operator/reports/bookings/export', env.BOOKING_BASE_URL],
      ['/v1/operator/reports/cancellation/export', env.BOOKING_BASE_URL],
      ['/v1/operator/reports/parcels/export', env.PARCEL_BASE_URL],
      ['/v1/operator/reports/revenue/export', env.PAYMENT_BASE_URL],
      ['/v1/operator/reports/refunds/export', env.PAYMENT_BASE_URL],
      ['/v1/operator/reports/occupancy/export', env.TRIP_BASE_URL],
    ] as const;

    expected.forEach(([path, target]) => {
      const route = matchRoute(routes, path);
      expect(route?.target).toBe(target);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    });
  });

  it('keeps DLQ and job-health internal routes out of Gateway', () => {
    expect(matchRoute(routes, '/internal/v1/outbox/dlq')).toBeUndefined();
    expect(matchRoute(routes, '/internal/jobs/status')).toBeUndefined();
  });

  it('routes trip operations to Booking instead of the passenger booking route', () => {
    const route = matchRoute(
      routes,
      '/v1/bookings/trips/11111111-1111-1111-1111-111111111111/manifest',
    );

    expect(route?.prefix).toBe('/v1/bookings/trips');
    expect(route?.target).toBe(env.BOOKING_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toEqual(['DRIVER', 'ASSISTANT']);
    expect(route?.prefix).not.toBe('/v1/bookings');
  });

  it('routes substitute-vehicle to Trip with user auth', () => {
    const route = matchRoute(
      routes,
      '/v1/operator/trips/11111111-1111-4111-8111-111111111111/substitute-vehicle',
    );
    const genericRoute = matchRoute(
      routes,
      '/v1/operator/trips/11111111-1111-4111-8111-111111111111',
    );

    expect(route).toMatchObject({
      prefix: '/v1/operator/trips/{tripId}/substitute-vehicle',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN'],
    });
    expect(genericRoute).toMatchObject({
      prefix: '/v1/operator/trips',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN', 'OPERATOR_STAFF'],
    });
  });

  it('routes operator fare-surcharge settings to Trip with operator roles', () => {
    const settings = matchRoute(routes, '/v1/operator/fare-surcharges/settings');
    const periods = matchRoute(routes, '/v1/operator/fare-surcharges/periods');

    expect(settings).toMatchObject({
      prefix: '/v1/operator/fare-surcharges',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN', 'OPERATOR_STAFF'],
    });
    expect(periods).toEqual(settings);
  });

  it('routes passenger transfer confirmation to Booking with user auth', () => {
    const route = matchRoute(
      routes,
      '/v1/bookings/trips/11111111-1111-4111-8111-111111111111/transfers/passengers/22222222-2222-4222-8222-222222222222/confirm',
    );

    expect(route).toMatchObject({
      prefix: '/v1/bookings/trips',
      target: env.BOOKING_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['DRIVER', 'ASSISTANT'],
    });
  });

  it('role-gates driver and assistant route families from PASSENGER users', () => {
    const driverRoute = matchRoute(routes, '/v1/driver/me/schedule');
    const assistantRoute = matchRoute(routes, '/v1/assistant/me');

    [driverRoute, assistantRoute].forEach((route) => {
      expect(route?.target).toBe(env.TRIP_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['DRIVER', 'ASSISTANT']);
      expect(route?.requiredRoles).not.toContain('PASSENGER');
    });
  });

  it('proxies every Day 18 driver endpoint through its owning service', () => {
    const cases = [
      ['/v1/driver/me/schedule', '/v1/driver', env.TRIP_BASE_URL],
      [
        '/v1/driver/trips/11111111-1111-1111-1111-111111111111/route',
        '/v1/driver',
        env.TRIP_BASE_URL,
      ],
      [
        '/v1/bookings/trips/11111111-1111-1111-1111-111111111111/manifest',
        '/v1/bookings/trips',
        env.BOOKING_BASE_URL,
      ],
      [
        '/v1/bookings/trips/11111111-1111-1111-1111-111111111111/boarding/passenger/22222222-2222-2222-2222-222222222222',
        '/v1/bookings/trips',
        env.BOOKING_BASE_URL,
      ],
      [
        '/v1/bookings/trips/11111111-1111-1111-1111-111111111111/boarding/qr-scan',
        '/v1/bookings/trips',
        env.BOOKING_BASE_URL,
      ],
    ] as const;

    cases.forEach(([path, prefix, target]) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(target);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['DRIVER', 'ASSISTANT']);
    });
  });

  it('routes operator booking stats to Booking with operator role union', () => {
    const route = matchRoute(routes, '/v1/operator/booking-stats');

    expect(route?.prefix).toBe('/v1/operator/booking-stats');
    expect(route?.target).toBe(env.BOOKING_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
  });

  it('routes operator booking list and detail to Booking with the exact operator role union', () => {
    const listRoute = matchRoute(routes, '/v1/operator/bookings');
    const detailRoute = matchRoute(
      routes,
      '/v1/operator/bookings/11111111-1111-4111-8111-111111111111',
    );

    [listRoute, detailRoute].forEach((route) => {
      expect(route?.prefix).toBe('/v1/operator/bookings');
      expect(route?.target).toBe(env.BOOKING_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
      expect(route?.requiredRoles).not.toContain('PASSENGER');
      expect(route?.requiredRoles).not.toContain('DRIVER');
      expect(route?.requiredRoles).not.toContain('SYSTEM_ADMIN');
    });
  });

  it('keeps operator booking routes distinct from neighboring Trip and Booking prefixes', () => {
    const cases = [
      ['/v1/operator/bookings', '/v1/operator/bookings', env.BOOKING_BASE_URL],
      ['/v1/operator/trips', '/v1/operator/trips/{list}', env.TRIP_BASE_URL],
      ['/v1/operator/booking-stats', '/v1/operator/booking-stats', env.BOOKING_BASE_URL],
      ['/v1/bookings', '/v1/bookings', env.BOOKING_BASE_URL],
      [
        '/v1/bookings/trips/11111111-1111-4111-8111-111111111111/manifest',
        '/v1/bookings/trips',
        env.BOOKING_BASE_URL,
      ],
    ] as const;

    cases.forEach(([path, prefix, target]) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(target);
    });
  });

  it('matches cross-service admin routes to the correct upstream services', () => {
    const cases = [
      ['/v1/admin/operators', env.IDENTITY_BASE_URL],
      ['/v1/admin/operators/11111111-1111-1111-1111-111111111111/approve', env.IDENTITY_BASE_URL],
      ['/v1/admin/operator-users', env.IDENTITY_BASE_URL],
      ['/v1/admin/users', env.IDENTITY_BASE_URL],
      ['/v1/admin/activity-logs', env.IDENTITY_BASE_URL],
      ['/v1/admin/subscription-plans', env.IDENTITY_BASE_URL],
      ['/v1/admin/locations', env.TRIP_BASE_URL],
      ['/v1/admin/stations/11111111-1111-1111-1111-111111111111', env.TRIP_BASE_URL],
      ['/v1/admin/stations/11111111-1111-1111-1111-111111111111/merge', env.TRIP_BASE_URL],
      ['/v1/admin/booking-stats/aggregate', env.BOOKING_BASE_URL],
      ['/v1/admin/vouchers', env.BOOKING_BASE_URL],
      ['/v1/admin/vouchers/11111111-1111-1111-1111-111111111111/consents', env.BOOKING_BASE_URL],
      ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
      ['/v1/admin/reports/platform', env.BOOKING_BASE_URL],
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
      '/v1/auth/resend-verification-email',
      '/v1/auth/forgot-password',
      '/v1/auth/reset-password',
      '/v1/auth/set-initial-password',
      '/v1/auth/login',
      '/v1/auth/google',
      '/v1/auth/refresh',
      '/v1/.well-known',
      '/v1/locations',
      '/v1/stations/search',
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

  it('matches password reset endpoints to dedicated public auth routes', () => {
    const forgotRoute = matchRoute(routes, '/v1/auth/forgot-password');
    const resetRoute = matchRoute(routes, '/v1/auth/reset-password');

    [forgotRoute, resetRoute].forEach((route) => {
      expect(route?.target).toBe(env.IDENTITY_BASE_URL);
      expect(route?.authRequired).toBe('none');
    });
  });

  it('matches resend verification email to its dedicated public auth route', () => {
    const route = matchRoute(routes, '/v1/auth/resend-verification-email');

    expect(route?.prefix).toBe('/v1/auth/resend-verification-email');
    expect(route?.target).toBe(env.IDENTITY_BASE_URL);
    expect(route?.authRequired).toBe('none');
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

  it('routes shuttle cancellation to Trip for both operator roles', () => {
    const route = matchRoute(
      routes,
      '/v1/operator/shuttle-trips/11111111-1111-4111-8111-111111111111/cancel',
    );

    expect(route?.prefix).toBe('/v1/operator/shuttle-trips/{shuttleTripId}/cancel');
    expect(route?.target).toBe(env.TRIP_BASE_URL);
    expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
  });

  it('routes trip search through the mixed public subpath without a duplicate prefix', () => {
    const route = matchRoute(routes, '/v1/trips/search');

    expect(route?.prefix).toBe('/v1/trips');
    expect(route?.target).toBe(env.TRIP_BASE_URL);
    expect(route?.authRequired).toBe('mixed');
    expect(route?.publicSubpaths).toEqual([{ method: 'GET', path: '/v1/trips/search' }]);
    expect(routes.find((r) => r.prefix === '/v1/trips/search')).toBeUndefined();
  });

  it('keeps trip detail and seat-map protected by the mixed trips route', () => {
    const cases = [
      '/v1/trips/11111111-1111-1111-1111-111111111111',
      '/v1/trips/11111111-1111-1111-1111-111111111111/seat-map',
    ] as const;

    cases.forEach((path) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe('/v1/trips');
      expect(route?.target).toBe(env.TRIP_BASE_URL);
      expect(route?.authRequired).toBe('mixed');
      expect(route?.publicSubpaths).toEqual([{ method: 'GET', path: '/v1/trips/search' }]);
    });
  });

  it('routes only the exact shared-trip context through the dedicated mixed Tracking route', () => {
    const publicRoute = matchRoute(routes, '/v1/tracking/shared-trip/context');
    const protectedSharedRoute = matchRoute(routes, '/v1/tracking/shared-trip/context/extra');
    const ownerRoute = matchRoute(
      routes,
      '/v1/tracking/trips/11111111-1111-4111-8111-111111111111/share-link',
    );

    expect(publicRoute).toMatchObject({
      prefix: '/v1/tracking/shared-trip',
      target: env.TRACKING_BASE_URL,
      authRequired: 'mixed',
      publicSubpaths: [{ method: 'GET', path: '/v1/tracking/shared-trip/context' }],
      forwardUserAuthorization: true,
    });
    expect(protectedSharedRoute).toBe(publicRoute);
    expect(ownerRoute).toMatchObject({
      prefix: '/v1/tracking',
      target: env.TRACKING_BASE_URL,
      authRequired: 'user',
      forwardUserAuthorization: true,
    });
  });

  it('does not expose internal trip endpoints through Gateway', () => {
    expect(matchRoute(routes, '/internal/v1/trips/search')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/reports/platform/bookings')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/reports/platform/trips')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/reports/platform/parcels')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/operators/summaries/batch')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/admin/dashboard/identity-metrics')).toBeUndefined();
    expect(matchRoute(routes, '/internal/v1/operators/vehicle-counts/batch')).toBeUndefined();
    expect(
      matchRoute(
        routes,
        '/internal/v1/operators/11111111-1111-4111-8111-111111111111/route-performance',
      ),
    ).toBeUndefined();
    expect(routes.some((r) => r.prefix.startsWith('/internal'))).toBe(false);
  });

  it('routes Day 7 public station search to Trip without auth', () => {
    const route = matchRoute(routes, '/v1/stations/search');

    expect(route?.prefix).toBe('/v1/stations/search');
    expect(route?.target).toBe(env.TRIP_BASE_URL);
    expect(route?.authRequired).toBe('none');
    expect(route?.requiredRoles).toBeUndefined();
  });

  it('routes Day 7 station and operator stop mutations to Trip with operator role union', () => {
    const cases = [
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

  it('routes Day 8 operator route families to Trip with operator role union', () => {
    const cases = [
      ['/v1/operator/routes', '/v1/operator/routes'],
      ['/v1/operator/routes/11111111-1111-1111-1111-111111111111/stops', '/v1/operator/routes'],
      ['/v1/operator/alternative-routes', '/v1/operator/alternative-routes'],
      [
        '/v1/operator/alternative-routes/11111111-1111-1111-1111-111111111111',
        '/v1/operator/alternative-routes',
      ],
    ] as const;

    cases.forEach(([path, prefix]) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(env.TRIP_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    });
  });

  it('matches operator routes using the dedicated prefix instead of generic routes', () => {
    const operatorRoute = matchRoute(
      routes,
      '/v1/operator/routes/11111111-1111-1111-1111-111111111111',
    );
    const publicRoute = matchRoute(routes, '/v1/routes/11111111-1111-1111-1111-111111111111');

    expect(operatorRoute?.prefix).toBe('/v1/operator/routes');
    expect(publicRoute?.prefix).toBe('/v1/routes');
  });

  it('routes Day 9 vehicle and driver schedule families to Trip with operator role union', () => {
    const cases = [
      ['/v1/operator/vehicles', '/v1/operator/vehicles'],
      ['/v1/operator/vehicles/11111111-1111-1111-1111-111111111111', '/v1/operator/vehicles'],
      ['/v1/operator/driver-schedules', '/v1/operator/driver-schedules'],
      [
        '/v1/operator/driver-schedules/11111111-1111-1111-1111-111111111111',
        '/v1/operator/driver-schedules',
      ],
      ['/v1/vehicle-types', '/v1/vehicle-types'],
    ] as const;

    cases.forEach(([path, prefix]) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(env.TRIP_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    });
  });

  it('routes Day 36 shuttle reads and dispatch mutations to Trip with distinct operator roles', () => {
    const requestsRoute = matchRoute(routes, '/v1/operator/shuttle-requests');
    const dispatchRoute = matchRoute(routes, '/v1/operator/shuttle-trips');

    expect(requestsRoute).toMatchObject({
      prefix: '/v1/operator/shuttle-requests',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN', 'OPERATOR_STAFF'],
    });
    expect(dispatchRoute).toMatchObject({
      prefix: '/v1/operator/shuttle-trips',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['OPERATOR_ADMIN'],
    });
  });

  it('forwards shuttle driver lifecycle routes to Trip with DRIVER-only access', () => {
    const shuttleDriverRoute = matchRoute(
      routes,
      '/v1/driver/shuttle-trips/11111111-1111-4111-8111-111111111111/stops/1/pickup',
    );
    const genericDriverRoute = matchRoute(
      routes,
      '/v1/driver/trips/11111111-1111-4111-8111-111111111111/complete',
    );

    expect(shuttleDriverRoute).toMatchObject({
      prefix: '/v1/driver/shuttle-trips',
      target: env.TRIP_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['DRIVER'],
    });
    expect(genericDriverRoute).toMatchObject({
      prefix: '/v1/driver',
      target: env.TRIP_BASE_URL,
      requiredRoles: ['DRIVER', 'ASSISTANT'],
    });
  });

  it('matches operator vehicles using the dedicated prefix without changing generic vehicles', () => {
    const operatorRoute = matchRoute(
      routes,
      '/v1/operator/vehicles/11111111-1111-1111-1111-111111111111',
    );
    const genericRoute = matchRoute(routes, '/v1/vehicles/11111111-1111-1111-1111-111111111111');

    expect(operatorRoute?.prefix).toBe('/v1/operator/vehicles');
    expect(operatorRoute?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    expect(genericRoute?.prefix).toBe('/v1/vehicles');
    expect(genericRoute?.requiredRoles).toBeUndefined();
  });

  it('keeps existing Identity operator routes distinct from Trip operator routes', () => {
    const profileRoute = matchRoute(routes, '/v1/operator/profile');
    const usersRoute = matchRoute(routes, '/v1/operator/users');
    const subscriptionRoute = matchRoute(routes, '/v1/operator/subscription');
    const adminOperatorUsersRoute = matchRoute(routes, '/v1/admin/operator-users');
    const adminSubscriptionPlansRoute = matchRoute(routes, '/v1/admin/subscription-plans');
    const operatorStationsRoute = matchRoute(routes, '/v1/operator/stations');
    const operatorStopsRoute = matchRoute(routes, '/v1/operator/stops');

    expect(profileRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(usersRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(subscriptionRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(subscriptionRoute?.requiredRoles).toEqual(['OPERATOR_ADMIN']);
    expect(adminOperatorUsersRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(adminSubscriptionPlansRoute?.target).toBe(env.IDENTITY_BASE_URL);
    expect(adminSubscriptionPlansRoute?.requiredRoles).toEqual(['SYSTEM_ADMIN']);
    expect(operatorStationsRoute?.target).toBe(env.TRIP_BASE_URL);
    expect(operatorStopsRoute?.target).toBe(env.TRIP_BASE_URL);
    expect(routes.find((r) => r.prefix === '/v1/operator')).toBeUndefined();
    expect(routes.find((r) => r.prefix === '/v1/admin')).toBeUndefined();
  });

  it('registers public station search separately but no stop delete route', () => {
    expect(routes.find((r) => r.prefix === '/v1/stations/search')).toBeDefined();
    expect(routes.find((r) => r.prefix === '/v1/operator/stops/delete')).toBeUndefined();
  });

  it('routes operator parcel-route-fares to Parcel with OPERATOR_ADMIN/STAFF roles', () => {
    const route = matchRoute(routes, '/v1/operator/parcel-route-fares');
    const routeWithId = matchRoute(
      routes,
      '/v1/operator/parcel-route-fares/22222222-2222-4222-8222-222222222222/SMALL',
    );

    expect(route?.prefix).toBe('/v1/operator/parcel-route-fares');
    expect(route?.target).toBe(env.PARCEL_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);

    expect(routeWithId?.prefix).toBe('/v1/operator/parcel-route-fares');
    expect(routeWithId?.target).toBe(env.PARCEL_BASE_URL);
    expect(routeWithId?.authRequired).toBe('user');
    expect(routeWithId?.requiredRoles).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    expect(routes.find((r) => r.prefix === '/v1/operator/parcel-route-fares')).toBeDefined();
  });

  it('routes assistant and operator parcel actions to Parcel without gateway-level role guards', () => {
    const assistantParcelIndex = routes.findIndex(
      (route) => route.prefix === '/v1/assistant/parcels',
    );
    const assistantIndex = routes.findIndex((route) => route.prefix === '/v1/assistant');

    expect(assistantParcelIndex).toBeGreaterThanOrEqual(0);
    expect(assistantIndex).toBeGreaterThanOrEqual(0);
    expect(assistantParcelIndex).toBeLessThan(assistantIndex);

    const cases = [
      ['/v1/assistant/parcels/11111111-1111-1111-1111-111111111111/load', '/v1/assistant/parcels'],
      [
        '/v1/assistant/parcels/11111111-1111-1111-1111-111111111111/confirm-delivery',
        '/v1/assistant/parcels',
      ],
      [
        '/v1/operator/parcels/11111111-1111-1111-1111-111111111111/confirm-delivery',
        '/v1/operator/parcels',
      ],
    ] as const;

    cases.forEach(([path, prefix]) => {
      const route = matchRoute(routes, path);

      expect(route?.prefix).toBe(prefix);
      expect(route?.target).toBe(env.PARCEL_BASE_URL);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toBeUndefined();
    });
  });

  it('routes crew parcel actions to Parcel with assigned crew roles', () => {
    const crewParcelRouteIndex = routes.findIndex((route) => route.prefix === '/v1/crew/parcels');
    const publicParcelRouteIndex = routes.findIndex((route) => route.prefix === '/v1/parcels');

    expect(crewParcelRouteIndex).toBeGreaterThanOrEqual(0);
    expect(publicParcelRouteIndex).toBeGreaterThanOrEqual(0);
    expect(crewParcelRouteIndex).toBeLessThan(publicParcelRouteIndex);

    const cases = [
      '/v1/crew/parcels/11111111-1111-4111-8111-111111111111/resend-delivery-email',
      '/v1/crew/parcels/11111111-1111-4111-8111-111111111111/manual-confirm',
      '/v1/crew/parcels/11111111-1111-4111-8111-111111111111/confirm-transfer',
    ] as const;

    cases.forEach((path) => {
      expect(matchRoute(routes, path)).toMatchObject({
        prefix: '/v1/crew/parcels',
        target: env.PARCEL_BASE_URL,
        authRequired: 'user',
        requiredRoles: ['DRIVER', 'ASSISTANT'],
      });
    });
  });

  it('routes the Assistant trip parcel list and QR scan to Parcel without capturing other Assistant paths', () => {
    const parcelListRoute = matchRoute(
      routes,
      '/v1/assistant/trips/11111111-1111-4111-8111-111111111111/parcels',
    );
    const parcelQrScanRoute = matchRoute(
      routes,
      '/v1/assistant/trips/11111111-1111-4111-8111-111111111111/parcels/qr-scan',
    );
    const otherAssistantRoute = matchRoute(
      routes,
      '/v1/assistant/trips/11111111-1111-4111-8111-111111111111/manifest',
    );

    expect(parcelListRoute).toMatchObject({
      prefix: '/v1/assistant/trips/{tripId}/parcels',
      target: env.PARCEL_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['ASSISTANT'],
    });
    expect(parcelQrScanRoute).toMatchObject({
      prefix: '/v1/assistant/trips/{tripId}/parcels',
      target: env.PARCEL_BASE_URL,
      authRequired: 'user',
      requiredRoles: ['ASSISTANT'],
    });
    expect(otherAssistantRoute).toMatchObject({
      prefix: '/v1/assistant',
      target: env.TRIP_BASE_URL,
    });
  });

  it('routes parcels to Parcel without a gateway-level role guard', () => {
    const route = matchRoute(routes, '/v1/parcels');
    const routeDetail = matchRoute(routes, '/v1/parcels/11111111-1111-1111-1111-111111111111');

    expect(route?.prefix).toBe('/v1/parcels');
    expect(route?.target).toBe(env.PARCEL_BASE_URL);
    expect(route?.authRequired).toBe('user');
    expect(route?.requiredRoles).toBeUndefined();

    expect(routeDetail?.prefix).toBe('/v1/parcels');
    expect(routeDetail?.target).toBe(env.PARCEL_BASE_URL);
    expect(routeDetail?.authRequired).toBe('user');
    expect(routeDetail?.requiredRoles).toBeUndefined();
  });

  it('allows the signed VNPay return status lookup without a user token', () => {
    const route = matchRoute(routes, '/v1/payments/vnpay-return-status');

    expect(route?.target).toBe(env.PAYMENT_BASE_URL);
    expect(route?.authRequired).toBe('mixed');
    expect(route?.publicSubpaths).toContainEqual({
      method: 'GET',
      path: '/v1/payments/vnpay-return-status',
    });
  });

  it('routes parcel delivery token endpoints through the longer mixed prefix', () => {
    const confirmRoute = matchRoute(routes, '/v1/parcels/delivery/confirm');
    const rejectRoute = matchRoute(routes, '/v1/parcels/delivery/reject');
    const undoRejectRoute = matchRoute(routes, '/v1/parcels/delivery/undo-reject');

    [confirmRoute, rejectRoute, undoRejectRoute].forEach((route) => {
      expect(route?.prefix).toBe('/v1/parcels/delivery');
      expect(route?.target).toBe(env.PARCEL_BASE_URL);
      expect(route?.authRequired).toBe('mixed');
      expect(route?.requiredRoles).toBeUndefined();
      expect(route?.publicSubpaths).toEqual([
        { method: 'POST', path: '/v1/parcels/delivery/confirm' },
        { method: 'POST', path: '/v1/parcels/delivery/reject' },
        { method: 'POST', path: '/v1/parcels/delivery/undo-reject' },
      ]);
    });
  });

  it('keeps operator parcel-route-fares distinct from operator routes', () => {
    const parcelFareRoute = matchRoute(routes, '/v1/operator/parcel-route-fares');
    const operatorRoute = matchRoute(routes, '/v1/operator/routes');

    expect(parcelFareRoute?.target).toBe(env.PARCEL_BASE_URL);
    expect(operatorRoute?.target).toBe(env.TRIP_BASE_URL);
    expect(parcelFareRoute?.prefix).not.toBe(operatorRoute?.prefix);
    expect(routes.find((r) => r.prefix === '/v1/operator')).toBeUndefined();
  });

  it('routes all UI-gap public facades to their owners with exact access gates', () => {
    const expected = [
      ['/v1/admin/dashboard/summary', env.BOOKING_BASE_URL, ['SYSTEM_ADMIN']],
      ['/v1/operator/parcel-stats', env.PARCEL_BASE_URL, ['OPERATOR_ADMIN']],
      ['/v1/admin/revenue/analytics', env.PAYMENT_BASE_URL, ['SYSTEM_ADMIN']],
      ['/v1/operator/revenue/analytics', env.PAYMENT_BASE_URL, ['OPERATOR_ADMIN']],
      ['/v1/operator/trips', env.TRIP_BASE_URL, ['OPERATOR_ADMIN']],
      [
        '/v1/operator/parcel-route-fares/11111111-1111-4111-8111-111111111111/batch',
        env.PARCEL_BASE_URL,
        ['OPERATOR_ADMIN'],
      ],
    ] as const;

    expected.forEach(([path, target, roles]) => {
      const route = matchRoute(routes, path);

      expect(route?.target).toBe(target);
      expect(route?.authRequired).toBe('user');
      expect(route?.requiredRoles).toEqual(roles);
      expect(route?.forwardUserAuthorization).not.toBe(true);
    });
  });

  it('preserves legacy staff reads outside the two new admin-only exact routes', () => {
    expect(
      matchRoute(routes, '/v1/operator/trips/11111111-1111-4111-8111-111111111111/cargo-capacity')
        ?.requiredRoles,
    ).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
    expect(matchRoute(routes, '/v1/operator/parcel-route-fares')?.requiredRoles).toEqual([
      'OPERATOR_ADMIN',
      'OPERATOR_STAFF',
    ]);
    expect(
      matchRoute(
        routes,
        '/v1/operator/parcel-route-fares/11111111-1111-4111-8111-111111111111/SMALL',
      )?.requiredRoles,
    ).toEqual(['OPERATOR_ADMIN', 'OPERATOR_STAFF']);
  });

  it('keeps Swagger specs public and mapped to every UI-gap facade owner', () => {
    const expected = [
      ['/api-specs/booking', env.BOOKING_BASE_URL],
      ['/api-specs/parcel', env.PARCEL_BASE_URL],
      ['/api-specs/payment', env.PAYMENT_BASE_URL],
    ] as const;

    expected.forEach(([path, target]) => {
      expect(matchRoute(routes, path)).toMatchObject({
        target,
        authRequired: 'none',
        rewriteTo: '/swagger/v1/swagger.json',
      });
    });
  });

  it('prefixes are unique', () => {
    const prefixes = routes.map((r) => r.prefix);
    const set = new Set(prefixes);
    expect(set.size).toBe(prefixes.length);
  });
});
