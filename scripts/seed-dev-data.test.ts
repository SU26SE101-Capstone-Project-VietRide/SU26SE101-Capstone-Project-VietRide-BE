import assert from 'node:assert/strict';
import fs from 'node:fs';
import { describe, test } from 'node:test';
import {
  buildDay44SeedPlan,
  buildChecksumScopePredicate,
  buildBatchedReadSql,
  buildPersistedInsertSql,
  buildRoutePersistenceSql,
  buildStoreScopePredicate,
  DAY44_PSQL_MAX_BUFFER_BYTES,
  decodeBatchedReadOutput,
  executeBatchedReads,
  DAY44_CANONICAL_VEHICLE_TYPES,
  injectPlatformWalletUpdate,
  projectIdentitySubscriptionPlansForStore,
  projectIdentityUserForStore,
  quantizeBinary16,
  readPsqlSpawnResult,
  validateRagPreflightState,
  validateNoDemoOauthIdentities,
  validateProjectedStoreRows,
  validateSeedOptions,
} from './seed-dev-data';
import { planDay44TripFixture } from './day44/seed-trip';

describe('Day 44 seed orchestrator', () => {
  const now = new Date('2026-08-08T00:00:00.000Z');
  const canonicalStarter = {
    id: '00000000-0000-0000-0000-000000000001',
    name: 'Starter (Free Trial)',
    description: 'Free 30-day trial auto-assigned on operator registration.',
    pricePerMonth: 0,
    pricePerYear: 0,
    maxVehicles: 3,
    maxDrivers: 5,
    maxAssistants: 5,
    maxOperatorUsers: 3,
    maxRoutes: 5,
    maxTripsPerMonth: 100,
    enableParcel: false,
    enableShuttle: false,
    enableRag: true,
    isActive: true,
  };
  test('validates every input before producing any database batch', () => {
    assert.throws(
      () =>
        validateSeedOptions({
          environment: 'Production',
          password: 'x',
          startDate: '2026-08-10',
          now,
        }),
      /Production/,
    );
    assert.throws(
      () => validateSeedOptions({ password: '', startDate: '2026-08-10', now }),
      /DEMO_SEED_ACCOUNT_PASSWORD/,
    );
    assert.throws(
      () => validateSeedOptions({ password: 'x', startDate: '2026-08-08', now }),
      /at least one day/,
    );
    assert.throws(
      () => validateSeedOptions({ password: 'x', startDate: '2026-02-31', now }),
      /valid YYYY-MM-DD/,
    );
  });

  test('strictly validates canonical and runtime Starter pg JSON projections', () => {
    const canonicalPgJson = {
      id: canonicalStarter.id,
      name: canonicalStarter.name,
      description: canonicalStarter.description,
      price_per_month: 0,
      price_per_year: 0,
      max_vehicles: 3,
      max_drivers: 5,
      max_assistants: 5,
      max_operator_users: 3,
      max_routes: 5,
      max_trips_per_month: 100,
      enable_parcel: false,
      enable_shuttle: false,
      enable_rag: true,
      is_active: true,
      created_at: '2026-08-08T00:00:00+00:00',
      updated_at: '2026-08-08T00:00:00+00:00',
    };
    assert.deepEqual(
      validateProjectedStoreRows(
        'vietride_identity.subscription_plans',
        [canonicalStarter],
        [canonicalPgJson],
        ['name'],
      ),
      [canonicalStarter],
    );

    const [runtimeStarter] = projectIdentitySubscriptionPlansForStore([canonicalStarter]);
    const runtimePgJson = {
      ...canonicalPgJson,
      description: 'Default onboarding plan seeded by Identity migration.',
    };
    assert.deepEqual(
      validateProjectedStoreRows(
        'vietride_identity.subscription_plans',
        [runtimeStarter],
        [runtimePgJson],
        ['name'],
      ),
      [runtimeStarter],
    );
    assert.throws(
      () =>
        validateProjectedStoreRows(
          'vietride_identity.subscription_plans',
          [runtimeStarter],
          [{ ...runtimePgJson, max_trips_per_month: 101 }],
          ['name'],
        ),
      /exact-ID\/natural-key\/full-state mismatch/,
    );
  });

  test('quotes text-cast numeric and injection-bearing natural-key predicates', () => {
    const predicate = buildStoreScopePredicate(
      [
        {
          routeId: '44000000-0000-4000-8000-000000000001',
          orderIndex: 1,
          label: "O'Brien'); DROP TABLE routes; --",
          optionalCode: null,
        },
      ],
      ['routeId+orderIndex+label+optionalCode'],
    );
    assert.equal(
      predicate,
      "(route_id::text='44000000-0000-4000-8000-000000000001' AND order_index::text='1' AND label::text='O''Brien''); DROP TABLE routes; --' AND optional_code IS NULL)",
    );
    assert.doesNotMatch(predicate ?? '', /order_index::text=1(?:\D|$)/);
  });

  test('accepts only empty or complete exact RAG preflight state', () => {
    const empty = {
      expectedDocuments: 3,
      expectedChunks: 3,
      existingDocuments: 0,
      existingChunks: 0,
      dimensionReady: 0,
      searchVectorReady: 0,
    };
    assert.doesNotThrow(() => validateRagPreflightState(empty));
    assert.doesNotThrow(() =>
      validateRagPreflightState({
        ...empty,
        existingDocuments: 3,
        existingChunks: 3,
        dimensionReady: 3,
        searchVectorReady: 3,
      }),
    );
    for (const invalid of [
      { ...empty, existingDocuments: 1 },
      {
        ...empty,
        existingDocuments: 3,
        existingChunks: 2,
        dimensionReady: 2,
        searchVectorReady: 2,
      },
      {
        ...empty,
        existingDocuments: 3,
        existingChunks: 3,
        dimensionReady: 2,
        searchVectorReady: 3,
      },
      {
        ...empty,
        existingDocuments: 3,
        existingChunks: 3,
        dimensionReady: 3,
        searchVectorReady: 2,
      },
      {
        ...empty,
        existingDocuments: 3,
        existingChunks: 3,
        dimensionReady: 2,
        searchVectorReady: 2,
      },
    ])
      assert.throws(() => validateRagPreflightState(invalid), /RAG vector\/search state mismatch/);
  });

  test('rejects any OAuth identity attached to a fixed demo user before planner mapping', () => {
    assert.doesNotThrow(() => validateNoDemoOauthIdentities(0));
    assert.throws(() => validateNoDemoOauthIdentities(1), /must not have attached OAuth/);
    const source = fs.readFileSync('scripts/seed-dev-data.ts', 'utf8');
    const definition = source.indexOf('validateNoDemoOauthIdentities(');
    const zeroProof = source.indexOf('validateNoDemoOauthIdentities(', definition + 1);
    const plannerMapping = source.indexOf('const identityState = {');
    assert.ok(zeroProof >= 0 && plannerMapping > zeroProof);
    assert.match(source, /FROM vietride_identity\.oauth_identities WHERE user_id IN/);
  });

  test('plans fixed dependency-ordered batches without a provider path', () => {
    const plan = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    assert.deepEqual(
      plan.batches.map((item) => item.database),
      [
        'vietride_identity',
        'vietride_trip',
        'vietride_payment',
        'vietride_booking',
        'vietride_parcel',
        'vietride_rag',
      ],
    );
    assert.equal(plan.expected.trips, 126);
    assert.equal(plan.expected.tripSeats, 3948);
    assert.equal(plan.checksum.length, 64);
    const sql = plan.batches.map((item) => item.sql).join('\n');
    const orchestratorSource = fs.readFileSync('scripts/seed-dev-data.ts', 'utf8');
    assert.doesNotMatch(
      orchestratorSource,
      /\bfetch\s*\(|from ['"]node:https?['"]|https?:\/\/|\/embeddings/i,
    );
    assert.match(sql, /gen_salt\('bf', 12\)/);
    assert.doesNotMatch(sql, /runtime-only/);
  });

  test('encodes quotes, Unicode, and special characters without logging credentials', () => {
    const password = "Vũ's demo $ecret ✓";
    const plan = buildDay44SeedPlan(
      { password, startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    const sql = plan.batches.map((item) => item.sql).join('\n');
    assert.doesNotMatch(sql, new RegExp(password.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
    assert.match(sql, /convert_from\(decode\('[A-Za-z0-9+/=]+'/);
  });

  test('persists only canonical Identity user columns with BCrypt cost 12', () => {
    const plan = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    const identitySql =
      plan.batches.find((item) => item.database === 'vietride_identity')?.sql ?? '';
    const userInserts = identitySql
      .split('\n')
      .filter((line) => line.startsWith('INSERT INTO vietride_identity.users '));
    assert.equal(userInserts.length, 25);
    assert.equal((identitySql.match(/crypt\(/g) ?? []).length, 1);
    assert.equal((identitySql.match(/gen_salt\('bf', 12\)/g) ?? []).length, 1);
    assert.match(
      identitySql,
      /DO \$day44\$ BEGIN PERFORM set_config\('vietride\.day44_password_hash', crypt\(convert_from\(decode\('[A-Za-z0-9+/=]+'[\s\S]*gen_salt\('bf', 12\)\), true\); END \$day44\$;/,
    );
    assert.ok(
      identitySql.indexOf("set_config('vietride.day44_password_hash'") <
        identitySql.indexOf('INSERT INTO vietride_identity.users'),
    );
    for (const statement of userInserts) {
      assert.match(
        statement,
        /^INSERT INTO vietride_identity\.users \(id,email,phone,display_name,avatar_url,role,status,locked_from_status,operator_id,failed_login_attempts,last_failed_login_at,last_login_at,created_at,updated_at,deleted_at,password_hash\)/,
      );
      assert.doesNotMatch(statement, /date_of_birth|gender|oauth_identity|credential_state/);
      assert.match(statement, /current_setting\('vietride\.day44_password_hash'\)/);
      assert.doesNotMatch(statement, /crypt\(|gen_salt\(/);
    }
    assert.equal(
      (identitySql.match(/current_setting\('vietride\.day44_password_hash'\)/g) ?? []).length,
      25,
    );
    assert.deepEqual(Object.keys(projectIdentityUserForStore({ id: 'x' })), [
      'id',
      'email',
      'phone',
      'displayName',
      'avatarUrl',
      'role',
      'status',
      'lockedFromStatus',
      'operatorId',
      'failedLoginAttempts',
      'lastFailedLoginAt',
      'lastLoginAt',
      'createdAt',
      'updatedAt',
      'deletedAt',
    ]);
  });

  test('uses each composite table primary key as its SQL conflict target', () => {
    const plan = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    const sql = plan.batches.map((item) => item.sql).join('\n');
    const targets = [
      ['vietride_trip.operator_stations', 'operator_id,station_id'],
      ['vietride_trip.route_stops', 'route_id,stop_id'],
      ['vietride_trip.alternative_route_stops', 'alternative_route_id,stop_id'],
      ['vietride_trip.trip_stops', 'trip_id,stop_id'],
      ['vietride_payment.wallets', 'user_id'],
      ['vietride_parcel.parcel_route_fares', 'route_id,size_category'],
    ];
    for (const [table, columns] of targets) {
      assert.match(
        sql,
        new RegExp(
          `INSERT INTO ${table.replaceAll('.', '\\.')}[\\s\\S]*?ON CONFLICT \\(${columns}\\) DO NOTHING`,
        ),
      );
    }
    const walletInserts = sql
      .split('\n')
      .filter((line) => line.startsWith('INSERT INTO vietride_payment.wallets '));
    assert.equal(walletInserts.length, 10);
    for (const statement of walletInserts)
      assert.match(statement, /ON CONFLICT \(user_id\) DO NOTHING;$/);
  });

  test('writes all final Route self-FKs in one trigger-free multi-row insert', () => {
    const plan = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    const routes = (plan.fixtures as { trip: { routes: ReadonlyArray<Record<string, unknown>> } })
      .trip.routes;
    const sql = buildRoutePersistenceSql(routes);
    assert.equal((sql.match(/INSERT INTO vietride_trip\.routes/g) ?? []).length, 1);
    assert.equal((sql.match(/^\('/gm) ?? []).length, 9);
    assert.match(sql, /ON CONFLICT \(id\) DO NOTHING;/);
    assert.doesNotMatch(sql, /UPDATE vietride_trip\.routes/);
    for (const route of routes) {
      assert.ok(sql.includes(`('${route.id}',`));
      if (route.returnRouteId == null)
        assert.match(sql, new RegExp(`\\('${route.id}',[^\\n]+,NULL,`));
      else {
        assert.ok(sql.includes(`'${route.returnRouteId}'`));
        assert.match(sql, new RegExp(`\\('${route.id}',[^\\n]+,'${route.returnRouteId}',`));
      }
      assert.ok(sql.includes(`'${route.createdAt}'`));
      assert.ok(sql.includes(`'${route.updatedAt}'`));
    }
    const tripSql = plan.batches.find((batch) => batch.database === 'vietride_trip')?.sql ?? '';
    assert.ok(
      tripSql.indexOf('INSERT INTO vietride_trip.routes') <
        tripSql.indexOf('INSERT INTO vietride_trip.route_stops'),
    );
  });

  test('serializes every Day 44 JSON column and preserves canonical PostgreSQL arrays', () => {
    const nested = { message: "Chuyến đi của O'Brien", nested: { quote: "'", values: [1, 2] } };
    const jsonColumns = [
      ['vietride_identity.operators', 'cancellationPolicy'],
      ['vietride_identity.operators', 'parcelNoShowPolicy'],
      ['vietride_identity.operators', 'luggagePolicy'],
      ['vietride_trip.stations', 'operatingHours'],
      ['vietride_trip.stations', 'facilities'],
      ['vietride_trip.vehicles', 'seatLayoutJson'],
      ['vietride_trip.vehicles', 'imageUrls'],
      ['vietride_trip.driver_schedules', 'dayOfWeek'],
      ['vietride_trip.trips', 'seatLayoutSnapshotJson'],
      ['vietride_payment.payments', 'context'],
      ['vietride_payment.outbox_events', 'payload'],
      ['vietride_payment.invoices', 'metadata'],
    ] as const;
    for (const [table, column] of jsonColumns) {
      const value = table === 'vietride_payment.outbox_events' ? JSON.stringify(nested) : nested;
      const sql = buildPersistedInsertSql(table, { [column]: value });
      const match = sql.match(/'((?:''|[^'])*)'::jsonb/);
      assert.ok(match, `${table}.${column} must use an explicit jsonb literal`);
      assert.deepEqual(JSON.parse(match[1].replaceAll("''", "'")), nested);
      assert.doesNotMatch(sql, /'\{1,2(?:,|\})/);
    }

    const postgresArrays = [
      ['vietride_booking.vouchers', 'applicablePaymentMethods', ['WALLET', "VN'PAY"], 'text'],
      ['vietride_booking.vouchers', 'applicableServices', ['BOOKING', 'PARCEL'], 'text'],
      [
        'vietride_booking.vouchers',
        'applicableOperatorIds',
        ['6276b48c-3984-582b-9c35-0c2fbe20baa7'],
        'uuid',
      ],
      [
        'vietride_booking.vouchers',
        'applicableRouteIds',
        ['316ba0dc-6bea-5173-858d-4c9c3cde50de'],
        'uuid',
      ],
      ['vietride_rag.knowledge_documents', 'audienceRoles', ['SYSTEM_ADMIN'], 'text'],
    ] as const;
    for (const [table, column, value, cast] of postgresArrays) {
      const sql = buildPersistedInsertSql(table, { [column]: value });
      assert.match(sql, new RegExp(`ARRAY\\[[^\\]]+\\]::${cast}\\[\\]`));
      assert.doesNotMatch(sql, /'\{[^']*\}'/);
    }

    const ragChunkSql = buildPersistedInsertSql('vietride_rag.knowledge_chunks', {
      embedding: [0.25, -0.5],
      searchVector: { configuration: 'simple', content: "Xe O'Brien" },
    });
    assert.match(ragChunkSql, /'\[0\.25,-0\.5\]'::halfvec/);
    assert.match(ragChunkSql, /to_tsvector\('simple', 'Xe O''Brien'\)/);

    const planSql = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    )
      .batches.map((batch) => batch.sql)
      .join('\n');
    assert.match(
      planSql,
      /INSERT INTO vietride_trip\.driver_schedules \([^\n]*day_of_week[^\n]*\) VALUES \([^\n]*'\[1,2,3,4,5,6,7\]'::jsonb/,
    );
    assert.doesNotMatch(
      planSql,
      /INSERT INTO vietride_trip\.driver_schedules \([^\n]*day_of_week[^\n]*\) VALUES \([^\n]*'\{1,2,3,4,5,6,7\}'/,
    );
  });

  test('compares JSONB store values canonically while rejecting field drift', () => {
    const expected = [
      {
        id: '40000000-0000-4000-8000-000000000099',
        payload: JSON.stringify({ message: "Chuyến O'Brien", nested: { b: 2, a: 1 } }),
      },
    ];
    const actual = [
      {
        id: expected[0].id,
        payload: { nested: { a: 1, b: 2 }, message: "Chuyến O'Brien" },
      },
    ];
    const validated = validateProjectedStoreRows(
      'vietride_payment.outbox_events',
      expected,
      actual,
    );
    assert.equal(validated[0].payload, expected[0].payload);
    assert.equal(validated[0].payload, '{"message":"Chuyến O\'Brien","nested":{"b":2,"a":1}}');
    assert.throws(
      () =>
        validateProjectedStoreRows('vietride_payment.outbox_events', expected, [
          { ...actual[0], payload: { nested: { a: 1, b: 3 }, message: "Chuyến O'Brien" } },
        ]),
      /full-state mismatch/,
    );
  });

  test('compares RAG embeddings in exact persisted binary16 representation', () => {
    assert.equal(quantizeBinary16(1), 1);
    assert.equal(quantizeBinary16(-2), -2);
    assert.equal(quantizeBinary16(1 / 3), 0.333251953125);
    assert.equal(quantizeBinary16(65504), 65504);
    assert.equal(quantizeBinary16(2 ** -14), 2 ** -14);
    assert.equal(quantizeBinary16(2 ** -24), 2 ** -24);
    assert.throws(() => quantizeBinary16(Number.NaN), /must be finite/);

    const sourceEmbedding = Array.from({ length: 2048 }, (_, index) =>
      index === 0 ? 1 / 3 : (index % 17) / 19 - 0.4,
    );
    const persistedEmbedding = sourceEmbedding.map(quantizeBinary16);
    const expected = [
      {
        id: '40000000-0000-4000-8000-000000000077',
        documentId: '40000000-0000-4000-8000-000000000078',
        chunkIndex: 0,
        embedding: sourceEmbedding,
      },
    ];
    const actual = [
      {
        id: expected[0].id,
        document_id: expected[0].documentId,
        chunk_index: 0,
        embedding: JSON.stringify(persistedEmbedding),
      },
    ];
    assert.doesNotThrow(() =>
      validateProjectedStoreRows('vietride_rag.knowledge_chunks', expected, actual),
    );
    const drifted = [...persistedEmbedding];
    drifted[0] = 0.33349609375;
    assert.throws(
      () =>
        validateProjectedStoreRows('vietride_rag.knowledge_chunks', expected, [
          { ...actual[0], embedding: JSON.stringify(drifted) },
        ]),
      /full-state mismatch/,
    );
  });

  test('matches idless composite rows only by their complete natural key', () => {
    const expected = [
      {
        routeId: '40000000-0000-4000-8000-000000000001',
        stopId: '40000000-0000-4000-8000-000000000011',
        orderIndex: 1,
        allowPickup: true,
      },
      {
        routeId: '40000000-0000-4000-8000-000000000001',
        stopId: '40000000-0000-4000-8000-000000000012',
        orderIndex: 2,
        allowPickup: false,
      },
    ];
    const rawExact = expected.map((row) => ({
      route_id: row.routeId,
      stop_id: row.stopId,
      order_index: row.orderIndex,
      allow_pickup: row.allowPickup,
    }));
    assert.deepEqual(
      validateProjectedStoreRows('vietride_trip.route_stops', expected, rawExact, [
        'routeId+stopId',
      ]),
      expected,
    );
    assert.throws(
      () =>
        validateProjectedStoreRows(
          'vietride_trip.route_stops',
          expected,
          [{ ...rawExact[0], stop_id: '40000000-0000-4000-8000-000000000099' }],
          ['routeId+stopId'],
        ),
      /foreign natural-key collision/,
    );
    assert.throws(
      () =>
        validateProjectedStoreRows(
          'vietride_trip.route_stops',
          expected,
          [{ ...rawExact[1], allow_pickup: true }],
          ['routeId+stopId'],
        ),
      /full-state mismatch/,
    );
  });

  test('injects the guarded platform wallet block without interpreting dollar tokens', () => {
    const paymentBatch = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    ).batches.find((batch) => batch.database === 'vietride_payment');
    assert.ok(paymentBatch);

    for (const [hasFinancialState, expectedBody] of [
      [false, 'DO $$ DECLARE changed int;'],
      [true, 'DO $$ BEGIN IF NOT EXISTS'],
    ] as const) {
      const sql = injectPlatformWalletUpdate(paymentBatch.sql, 7, hasFinancialState);
      assert.doesNotMatch(sql, /DAY44_PLATFORM_WALLET_UPDATE/);
      assert.ok(sql.includes(expectedBody));
      assert.match(sql, /END \$\$;/);
      assert.doesNotMatch(sql, /DO \$ DECLARE/);
      const guard = sql.indexOf('DO $$');
      assert.ok(sql.indexOf('BEGIN;') < guard);
      assert.ok(guard < sql.lastIndexOf('COMMIT;'));
      assert.match(sql, /row_version=7/);
    }
  });

  test('bounds complete DB output while keeping failure diagnostics non-sensitive', () => {
    assert.equal(DAY44_PSQL_MAX_BUFFER_BYTES, 64 * 1024 * 1024);
    assert.throws(
      () =>
        readPsqlSpawnResult({
          status: null,
          stdout: '[{"secretRow":"must-not-leak"}]',
          error: Object.assign(new Error('spawnSync docker ENOBUFS'), { code: 'ENOBUFS' }),
        }),
      (error: unknown) => {
        assert.match(String(error), /execution failed \(ENOBUFS\)/);
        assert.doesNotMatch(String(error), /secretRow|spawnSync docker/);
        return true;
      },
    );

    const hugeStdout = `RAW_ROW_${'x'.repeat(2 * 1024 * 1024)}`;
    assert.throws(
      () =>
        readPsqlSpawnResult({
          status: 1,
          stdout: hugeStdout,
          stderr:
            "ERROR:  exact-state validation failed\nLINE 1: SELECT 'credential-bearing raw SQL'\nDETAIL:  bounded detail\n",
        }),
      (error: unknown) => {
        const message = String(error);
        assert.match(message, /ERROR: {2}exact-state validation failed/);
        assert.match(message, /DETAIL: {2}bounded detail/);
        assert.doesNotMatch(message, /RAW_ROW_|credential-bearing raw SQL/);
        assert.ok(message.length < 2100);
        return true;
      },
    );

    const rows = Array.from({ length: 30_000 }, (_, index) => ({
      id: String(index).padStart(8, '0'),
      value: `Ghế ${index} O'Brien ${'ữ'.repeat(40)}`,
    }));
    const multiMegabyteJson = JSON.stringify(rows);
    assert.ok(Buffer.byteLength(multiMegabyteJson, 'utf8') > 2 * 1024 * 1024);
    const parsed = JSON.parse(
      readPsqlSpawnResult({ status: 0, stdout: `${multiMegabyteJson}\n`, stderr: '' }),
    ) as typeof rows;
    assert.equal(parsed.length, rows.length);
    assert.deepEqual(parsed.at(-1), rows.at(-1));
  });

  test('scopes wallet checksum by its natural user key without an empty id predicate', () => {
    const userId = '79dc29ea-c982-5117-b688-7bb88b7bb04e';
    const where = buildChecksumScopePredicate('vietride_payment.wallets', [{ userId }]);
    assert.equal(where, `(user_id::text='${userId}')`);
    assert.doesNotMatch(where ?? '', /\bIN \(\)/);
    assert.doesNotMatch(where ?? '', /\bid::text/);
    assert.throws(
      () => buildChecksumScopePredicate('vietride_payment.unknown_natural_key', [{ userId }]),
      /checksum key projection is missing/,
    );
  });

  test('batches every read scope into at most one runner call per database', () => {
    const databases = [
      'vietride_identity',
      'vietride_trip',
      'vietride_payment',
      'vietride_booking',
      'vietride_parcel',
      'vietride_rag',
    ] as const;
    const requests = databases.flatMap((database) => [
      { database, key: `${database}.owned`, sql: `SELECT '[{"scope":"${database}"}]'::text` },
      { database, key: `${database}.empty`, sql: `SELECT '[]'::text` },
    ]);
    const calls: Array<{ database: string; sql: string }> = [];
    const results = executeBatchedReads(requests, (database, sql) => {
      calls.push({ database, sql });
      const keys = [...sql.matchAll(/json_build_object\('key','([^']+)'/g)].map(
        (match) => match[1],
      );
      return keys
        .map((key) =>
          JSON.stringify({
            key,
            value: key.endsWith('.empty') ? '[]' : JSON.stringify([{ scope: database }]),
          }),
        )
        .join('\n');
    });
    assert.equal(calls.length, databases.length);
    for (const database of databases) {
      assert.equal(calls.filter((call) => call.database === database).length, 1);
      assert.match(calls.find((call) => call.database === database)?.sql ?? '', /\.owned/);
      assert.match(calls.find((call) => call.database === database)?.sql ?? '', /\.empty/);
      assert.deepEqual(JSON.parse(String(results.get(`${database}.owned`))), [{ scope: database }]);
      assert.deepEqual(JSON.parse(String(results.get(`${database}.empty`))), []);
    }
    assert.throws(
      () => decodeBatchedReadOutput('{"key":"duplicate","value":1}\n{"key":"duplicate","value":2}'),
      /Invalid Day 44 batched read output/,
    );
    assert.match(buildBatchedReadSql(requests.slice(0, 2)), /SELECT '\[\]'::text/);

    const orchestratorSource = fs.readFileSync('scripts/seed-dev-data.ts', 'utf8');
    const preflight = orchestratorSource.slice(
      orchestratorSource.indexOf('function preflightRealStore('),
      orchestratorSource.indexOf('export function buildChecksumScopePredicate('),
    );
    const checksum = orchestratorSource.slice(
      orchestratorSource.indexOf('function realStoreChecksum('),
      orchestratorSource.indexOf('export const DAY44_PSQL_MAX_BUFFER_BYTES'),
    );
    assert.equal((preflight.match(/executeBatchedReads\(/g) ?? []).length, 1);
    assert.equal((checksum.match(/executeBatchedReads\(/g) ?? []).length, 1);
    assert.doesNotMatch(preflight, /\bpsql\(/);
    assert.doesNotMatch(checksum, /\bpsql\(/);
  });

  test('uses the canonical operator voucher consent table identifier everywhere', () => {
    const plan = buildDay44SeedPlan(
      { password: 'runtime-only', startDate: '2026-08-10', now },
      '40000000-0000-4000-8000-000000000001',
    );
    const sql = plan.batches.map((item) => item.sql).join('\n');
    const orchestratorSource = fs.readFileSync('scripts/seed-dev-data.ts', 'utf8');
    const harnessSource = fs.readFileSync('scripts/run-day44-seed-e2e.mjs', 'utf8');
    assert.match(sql, /INSERT INTO vietride_booking\.operator_voucher_consents/);
    assert.match(orchestratorSource, /vietride_booking\.operator_voucher_consents/);
    assert.match(harnessSource, /vietride_booking\.operator_voucher_consents/);
    assert.doesNotMatch(`${orchestratorSource}\n${harnessSource}`, /voucher_operator_consents/);
  });

  test('bootstraps the full canonical VehicleType catalog and rejects an incomplete one', () => {
    assert.equal(DAY44_CANONICAL_VEHICLE_TYPES.length, 3);
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Development',
          startDate: '2026-08-10',
          currentInstant: now,
          existingState: {
            vehicleTypes: DAY44_CANONICAL_VEHICLE_TYPES.slice(0, 2),
            stations: [],
            operatorStations: [],
            stops: [],
            routes: [],
            routeStops: [],
            alternativeRoutes: [],
            alternativeRouteStops: [],
            vehicles: [],
            driverSchedules: [],
            trips: [],
            tripSeats: [],
            tripStops: [],
            tripStopFares: [],
          },
        }),
      /complete canonical VehicleType catalog/,
    );
    assert.doesNotThrow(() =>
      buildDay44SeedPlan(
        { password: 'runtime-only', startDate: '2026-08-10', now },
        '40000000-0000-4000-8000-000000000001',
      ),
    );
  });
});
