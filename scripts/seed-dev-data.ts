/// <reference types="node" />

import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import {
  day44IdentityFixtureIds,
  planDay44IdentityFixture,
  type IdentitySubscriptionPlanFixture,
} from './day44/seed-identity';
import { planDay44TripFixture } from './day44/seed-trip';
import { emptyDay44CommerceState, planDay44CommerceFixture } from './day44/seed-commerce';
import { planDay44RagFixture } from './day44/seed-rag';

export const DAY44_NAMESPACE = 'day44-v1';
export const DAY44_TIMEZONE = 'Asia/Ho_Chi_Minh';

type Database =
  | 'vietride_identity'
  | 'vietride_trip'
  | 'vietride_booking'
  | 'vietride_payment'
  | 'vietride_parcel'
  | 'vietride_rag';

export interface SeedOptions {
  environment?: string;
  password?: string;
  startDate?: string;
  now?: Date;
}

export interface SqlBatch {
  database: Database;
  sql: string;
}

export interface Day44SeedPlan {
  startDate: string;
  checksum: string;
  batches: ReadonlyArray<SqlBatch>;
  expected: Readonly<Record<string, number>>;
  /** Internal immutable fixture graph used by the real-store preflight. */
  fixtures?: Readonly<Record<string, unknown>>;
}

const starterPlan: IdentitySubscriptionPlanFixture = {
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

const RUNTIME_STARTER_DESCRIPTION = 'Default onboarding plan seeded by Identity migration.';

const USER_PERSISTED_KEYS = [
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
] as const;

export function projectIdentityUserForStore(row: object): AnyRow {
  const source = row as AnyRow;
  return Object.fromEntries(USER_PERSISTED_KEYS.map((key) => [key, source[key]]));
}

export const DAY44_CANONICAL_VEHICLE_TYPES = Object.freeze([
  Object.freeze({
    id: '00000000-0000-0000-0000-000000000101',
    code: 'STANDARD_BUS',
    displayName: 'Xe ghế ngồi tiêu chuẩn',
    estimatedPassengerLuggageKgPerSeat: 10,
    defaultSeatCount: 45,
    isSystemDefined: true,
    isActive: true,
  }),
  Object.freeze({
    id: '00000000-0000-0000-0000-000000000102',
    code: 'LIMOUSINE',
    displayName: 'Limousine',
    estimatedPassengerLuggageKgPerSeat: 15,
    defaultSeatCount: 9,
    isSystemDefined: true,
    isActive: true,
  }),
  Object.freeze({
    id: '00000000-0000-0000-0000-000000000103',
    code: 'SLEEPER_BUS',
    displayName: 'Xe giường nằm',
    estimatedPassengerLuggageKgPerSeat: 20,
    defaultSeatCount: 40,
    isSystemDefined: true,
    isActive: true,
  }),
]);

function ictDate(now: Date): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: DAY44_TIMEZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
}

function parseCalendarDate(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const normalized = new Date(Date.UTC(year, month - 1, day));
  if (
    normalized.getUTCFullYear() !== year ||
    normalized.getUTCMonth() !== month - 1 ||
    normalized.getUTCDate() !== day
  )
    return null;
  return { year, month, day };
}

export function validateSeedOptions(options: SeedOptions): Required<SeedOptions> {
  const environment = options.environment ?? process.env.NODE_ENV ?? 'Development';
  const password = options.password ?? process.env.DEMO_SEED_ACCOUNT_PASSWORD ?? '';
  const startDate = options.startDate ?? '';
  const now = options.now ?? new Date();
  if (environment.trim().toLowerCase() === 'production')
    throw new Error('Day 44 seed is forbidden in Production');
  if (!password.trim()) throw new Error('DEMO_SEED_ACCOUNT_PASSWORD is required');
  if (!parseCalendarDate(startDate)) {
    throw new Error('--start-date must be a valid YYYY-MM-DD ICT date');
  }
  if (startDate <= ictDate(now))
    throw new Error('--start-date must be at least one day after the current ICT date');
  return { environment, password, startDate, now };
}

function snake(value: string): string {
  return value.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`);
}

type PersistedStructuredType = 'jsonb' | 'text[]' | 'uuid[]' | 'halfvec' | 'tsvector';

const persistedStructuredColumns: Readonly<Record<string, PersistedStructuredType>> = {
  'vietride_identity.operators.cancellationPolicy': 'jsonb',
  'vietride_identity.operators.parcelNoShowPolicy': 'jsonb',
  'vietride_identity.operators.luggagePolicy': 'jsonb',
  'vietride_trip.stations.operatingHours': 'jsonb',
  'vietride_trip.stations.facilities': 'jsonb',
  'vietride_trip.vehicles.seatLayoutJson': 'jsonb',
  'vietride_trip.vehicles.imageUrls': 'jsonb',
  'vietride_trip.driver_schedules.dayOfWeek': 'jsonb',
  'vietride_trip.trips.seatLayoutSnapshotJson': 'jsonb',
  'vietride_payment.payments.context': 'jsonb',
  'vietride_payment.outbox_events.payload': 'jsonb',
  'vietride_payment.invoices.metadata': 'jsonb',
  'vietride_booking.vouchers.applicablePaymentMethods': 'text[]',
  'vietride_booking.vouchers.applicableServices': 'text[]',
  'vietride_booking.vouchers.applicableOperatorIds': 'uuid[]',
  'vietride_booking.vouchers.applicableRouteIds': 'uuid[]',
  'vietride_rag.knowledge_documents.audienceRoles': 'text[]',
  'vietride_rag.knowledge_chunks.embedding': 'halfvec',
  'vietride_rag.knowledge_chunks.searchVector': 'tsvector',
};

function structuredType(
  table: string | undefined,
  key: string,
): PersistedStructuredType | undefined {
  return table ? persistedStructuredColumns[`${table}.${key}`] : undefined;
}

function quoted(text: string): string {
  return `'${text.replaceAll("'", "''")}'`;
}

function jsonText(value: unknown, key: string): string {
  if (typeof value !== 'string') return JSON.stringify(value);
  try {
    return JSON.stringify(JSON.parse(value));
  } catch {
    throw new Error(`Invalid JSON value for ${key}`);
  }
}

function literal(value: unknown, key: string, table?: string): string {
  if (value === null || value === undefined) return 'NULL';
  if (typeof value === 'boolean') return value ? 'TRUE' : 'FALSE';
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new Error(`Non-finite value for ${key}`);
    return String(value);
  }
  const persistedType = structuredType(table, key);
  if (persistedType === 'tsvector') {
    const search = value as { configuration: string; content: string };
    return `to_tsvector(${literal(search.configuration, key)}, ${literal(search.content, key)})`;
  }
  if (persistedType === 'jsonb') return `${quoted(jsonText(value, key))}::jsonb`;
  if (persistedType === 'halfvec') {
    if (!Array.isArray(value)) throw new Error(`Expected array value for ${table}.${key}`);
    return `${quoted(`[${value.join(',')}]`)}::halfvec`;
  }
  if (persistedType === 'text[]' || persistedType === 'uuid[]') {
    if (!Array.isArray(value)) throw new Error(`Expected array value for ${table}.${key}`);
    return `ARRAY[${value.map((item) => quoted(String(item))).join(',')}]::${persistedType}`;
  }
  if (Array.isArray(value) || typeof value === 'object')
    throw new Error(`Missing persisted structured type for ${table ?? 'unknown table'}.${key}`);
  return quoted(String(value));
}

function passwordExpression(password: string): string {
  const encoded = Buffer.from(password, 'utf8').toString('base64');
  return `crypt(convert_from(decode('${encoded}','base64'),'UTF8'), gen_salt('bf', 12))`;
}

function inserts<T extends object>(
  table: string,
  rows: ReadonlyArray<T>,
  extra: Readonly<Record<string, unknown>> = {},
): string {
  return rows
    .map((row) => {
      const merged = { ...row, ...extra } as Record<string, unknown>;
      const keys = Object.keys(merged).filter((key) => key !== 'credentialState');
      const compositeConflicts: Readonly<Record<string, string>> = {
        'vietride_trip.operator_stations': 'operator_id,station_id',
        'vietride_trip.route_stops': 'route_id,stop_id',
        'vietride_trip.alternative_route_stops': 'alternative_route_id,stop_id',
        'vietride_trip.trip_stops': 'trip_id,stop_id',
        'vietride_trip.trip_stop_fares': 'trip_id,stop_id',
        'vietride_payment.wallets': 'user_id',
        'vietride_parcel.parcel_route_fares': 'route_id,size_category',
      };
      const conflict = compositeConflicts[table] ?? (keys.includes('id') ? 'id' : null);
      const onConflict = conflict ? ` ON CONFLICT (${conflict}) DO NOTHING` : '';
      return `INSERT INTO ${table} (${keys.map(snake).join(',')}) VALUES (${keys.map((key) => literal(merged[key], key, table)).join(',')})${onConflict};`;
    })
    .join('\n');
}

export function buildPersistedInsertSql(
  table: string,
  row: Readonly<Record<string, unknown>>,
): string {
  return inserts(table, [row]);
}

export function buildRoutePersistenceSql(routes: ReadonlyArray<object>): string {
  const rows = routes.map((route) => route as AnyRow);
  if (rows.length === 0) return '';
  const keys = Object.keys(rows[0]);
  const values = rows
    .map((row) => `(${keys.map((key) => literal(row[key], key)).join(',')})`)
    .join(',\n');
  return `INSERT INTO vietride_trip.routes (${keys.map(snake).join(',')}) VALUES\n${values}\nON CONFLICT (id) DO NOTHING;`;
}

function batch(database: Database, statements: ReadonlyArray<string>): SqlBatch {
  return { database, sql: `BEGIN;\n${statements.filter(Boolean).join('\n')}\nCOMMIT;` };
}

export function buildDay44SeedPlan(
  options: SeedOptions,
  bootstrapSystemAdminId: string,
): Day44SeedPlan {
  const input = validateSeedOptions(options);
  if (!/^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(bootstrapSystemAdminId))
    throw new Error('Bootstrap System Admin is missing');
  const trip = planDay44TripFixture({
    environment: input.environment,
    startDate: input.startDate,
    currentInstant: input.now,
    existingState: {
      vehicleTypes: DAY44_CANONICAL_VEHICLE_TYPES,
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
  });
  const identity = planDay44IdentityFixture({
    environment: input.environment,
    accountPassword: input.password,
    startDate: input.startDate,
    currentInstant: input.now,
    tripDepartureInstantsByOperator: trip.tripDepartureInstantsByOperator,
    existingState: {
      bootstrapSystemAdmins: [
        { id: bootstrapSystemAdminId, role: 'SYSTEM_ADMIN', status: 'ACTIVE', deletedAt: null },
      ],
      subscriptionPlans: [starterPlan],
      operators: [],
      users: [],
      subscriptions: [],
    },
  });
  const commerce = planDay44CommerceFixture({
    environment: input.environment,
    startDate: input.startDate,
    currentInstant: input.now,
    references: {
      bootstrapSystemAdminId,
      businessPlanId: identity.subscriptionPlans[1].id,
      operatorIds: {
        A: day44IdentityFixtureIds.operators.A,
        B: day44IdentityFixtureIds.operators.B,
      },
      operatorAdminIds: {
        A: day44IdentityFixtureIds.operatorAdmins.A,
        B: day44IdentityFixtureIds.operatorAdmins.B,
      },
      subscriptionIds: {
        A: day44IdentityFixtureIds.subscriptions.A,
        B: day44IdentityFixtureIds.subscriptions.B,
      },
      passengerIds: day44IdentityFixtureIds.passengers,
      routeIds: { A: String(trip.routes[0].id), B: String(trip.routes[3].id) },
    },
    existingState: emptyDay44CommerceState(),
  });
  const rag = planDay44RagFixture({
    startDate: input.startDate,
    bootstrapSystemAdminId,
    operatorAId: day44IdentityFixtureIds.operators.A,
  });
  const passwordHashSetting = 'vietride.day44_password_hash';
  const initializePasswordHash = `DO $day44$ BEGIN PERFORM set_config('${passwordHashSetting}', ${passwordExpression(input.password)}, true); END $day44$;`;
  const userSql = identity.users
    .map(projectIdentityUserForStore)
    .map((row) => {
      const keys = Object.keys(row);
      return `INSERT INTO vietride_identity.users (${keys.map(snake).join(',')},password_hash) VALUES (${keys.map((key) => literal((row as unknown as Record<string, unknown>)[key], key)).join(',')},current_setting('${passwordHashSetting}')) ON CONFLICT (id) DO NOTHING;`;
    })
    .join('\n');

  const batches = [
    batch('vietride_identity', [
      inserts('vietride_identity.subscription_plans', identity.subscriptionPlans.slice(1)),
      inserts('vietride_identity.operators', identity.operators),
      initializePasswordHash,
      userSql,
      inserts('vietride_identity.operator_subscriptions', identity.subscriptions),
      inserts(
        'vietride_identity.subscription_upgrade_attempts',
        commerce.subscriptionUpgradeAttempts,
      ),
      inserts('vietride_identity.integration_inbox', commerce.identityInboxEvents),
    ]),
    batch('vietride_trip', [
      inserts('vietride_trip.vehicle_types', trip.vehicleTypes),
      inserts('vietride_trip.stations', trip.stations),
      inserts('vietride_trip.operator_stations', trip.operatorStations),
      inserts('vietride_trip.stops', trip.stops),
      buildRoutePersistenceSql(trip.routes),
      inserts('vietride_trip.route_stops', trip.routeStops),
      inserts('vietride_trip.alternative_routes', trip.alternativeRoutes),
      inserts('vietride_trip.alternative_route_stops', trip.alternativeRouteStops),
      inserts('vietride_trip.vehicles', trip.vehicles),
      inserts('vietride_trip.driver_schedules', trip.driverSchedules),
      inserts('vietride_trip.trips', trip.trips),
      inserts('vietride_trip.trip_seats', trip.tripSeats),
      inserts('vietride_trip.trip_stops', trip.tripStops),
    ]),
    batch('vietride_payment', [
      inserts('vietride_payment.wallets', commerce.wallets),
      inserts('vietride_payment.top_up_requests', commerce.topUpRequests),
      inserts('vietride_payment.wallet_transactions', commerce.walletTransactions),
      inserts('vietride_payment.payments', commerce.payments),
      inserts('vietride_payment.processed_integration_events', commerce.paymentProcessedEvents),
      inserts('vietride_payment.outbox_events', commerce.paymentOutboxEvents),
      inserts('vietride_payment.invoices', commerce.invoices),
      `/*DAY44_PLATFORM_WALLET_UPDATE*/`,
      inserts('vietride_payment.platform_wallet_transactions', commerce.platformWalletTransactions),
    ]),
    batch('vietride_booking', [
      inserts('vietride_booking.vouchers', commerce.vouchers),
      inserts('vietride_booking.operator_voucher_consents', commerce.voucherConsents),
    ]),
    batch('vietride_parcel', [
      inserts('vietride_parcel.parcel_route_fares', commerce.parcelRouteFares),
    ]),
    batch('vietride_rag', [
      inserts('vietride_rag.knowledge_documents', rag.documents),
      inserts('vietride_rag.knowledge_chunks', rag.chunks),
    ]),
  ];
  const checksum = createHash('sha256')
    .update(JSON.stringify({ identity, trip, commerce, rag }))
    .digest('hex');
  return {
    startDate: input.startDate,
    checksum,
    batches,
    expected: {
      operators: 3,
      users: 26,
      trips: 126,
      tripSeats: 3948,
      vouchers: 5,
      ragDocuments: 3,
    },
    fixtures: { identity, trip, commerce, rag },
  };
}

type AnyRow = Record<string, unknown>;
interface IdentityFixtures extends AnyRow {
  bootstrapSystemAdminId: string;
  subscriptionPlans: AnyRow[];
  operators: AnyRow[];
  users: AnyRow[];
  subscriptions: AnyRow[];
}
interface TripFixtures extends AnyRow {
  routes: AnyRow[];
  tripDepartureInstantsByOperator: Readonly<Record<'A' | 'B' | 'C', ReadonlyArray<string>>>;
}
interface CommerceFixtures extends AnyRow {
  payments: AnyRow[];
  paymentOutboxEvents: AnyRow[];
  subscriptionUpgradeAttempts: AnyRow[];
  identityInboxEvents: AnyRow[];
  platformWalletTransactions: AnyRow[];
  vouchers: AnyRow[];
  voucherConsents: AnyRow[];
  parcelRouteFares: AnyRow[];
}
interface RagFixtures extends AnyRow {
  documents: AnyRow[];
  chunks: AnyRow[];
}
interface FixtureGraph {
  identity: IdentityFixtures;
  trip: TripFixtures;
  commerce: CommerceFixtures;
  rag: RagFixtures;
}

function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object')
    return `{${Object.entries(value as AnyRow)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => `${JSON.stringify(key)}:${canonical(item)}`)
      .join(',')}}`;
  return JSON.stringify(value);
}

function normalizeValue(actual: unknown, expected: unknown): unknown {
  if (expected === null || expected === undefined) return actual ?? null;
  if (typeof expected === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(expected))
    return new Date(String(actual)).toISOString();
  if (typeof expected === 'string' && actual !== null && typeof actual === 'object')
    return JSON.stringify(actual);
  if (typeof expected === 'number') return Number(actual);
  if (Array.isArray(expected) && typeof actual === 'string' && actual.startsWith('['))
    return JSON.parse(actual);
  return actual;
}

export function quantizeBinary16(value: number): number {
  if (!Number.isFinite(value)) throw new Error('Day 44 halfvec value must be finite');
  const buffer = new ArrayBuffer(4);
  const float32 = new Float32Array(buffer);
  const uint32 = new Uint32Array(buffer);
  float32[0] = value;
  const bits = uint32[0];
  const sign = (bits >>> 16) & 0x8000;
  let exponent = ((bits >>> 23) & 0xff) - 127 + 15;
  let mantissa = bits & 0x7fffff;
  let half: number;
  if (exponent <= 0) {
    if (exponent < -10) half = sign;
    else {
      mantissa |= 0x800000;
      const shift = 14 - exponent;
      let rounded = mantissa >>> shift;
      const remainder = mantissa & (2 ** shift - 1);
      const halfway = 2 ** (shift - 1);
      if (remainder > halfway || (remainder === halfway && (rounded & 1) === 1)) rounded += 1;
      half = sign | rounded;
    }
  } else {
    let rounded = mantissa >>> 13;
    const remainder = mantissa & 0x1fff;
    if (remainder > 0x1000 || (remainder === 0x1000 && (rounded & 1) === 1)) rounded += 1;
    if (rounded === 0x400) {
      rounded = 0;
      exponent += 1;
    }
    if (exponent >= 31) throw new Error('Day 44 halfvec value exceeds binary16 range');
    half = sign | (exponent << 10) | rounded;
  }
  const halfSign = (half & 0x8000) === 0 ? 1 : -1;
  const halfExponent = (half >>> 10) & 0x1f;
  const halfMantissa = half & 0x3ff;
  if (halfExponent === 0) return halfSign * halfMantissa * 2 ** -24;
  return halfSign * (1 + halfMantissa / 1024) * 2 ** (halfExponent - 15);
}

function normalizeStructuredForCompare(table: string, key: string, value: unknown): unknown {
  if (table === 'vietride_rag.knowledge_chunks' && key === 'embedding') {
    if (!Array.isArray(value) || value.length !== 2048)
      throw new Error('Day 44 knowledge chunk embedding must contain exactly 2048 values');
    return value.map((item) => quantizeBinary16(Number(item)));
  }
  if (structuredType(table, key) !== 'jsonb' || typeof value !== 'string') return value;
  try {
    return JSON.parse(value);
  } catch {
    throw new Error(`Day 44 ${table}.${key} contains invalid JSON`);
  }
}

export function buildStoreScopePredicate(
  expectedRows: ReadonlyArray<AnyRow>,
  naturalKeys: ReadonlyArray<string>,
  scopePredicate?: string,
): string | undefined {
  const ids = expectedRows.map((row) => row.id).filter(Boolean);
  const predicates = ids.length
    ? [`id::text IN (${ids.map((id) => literal(id, 'id')).join(',')})`]
    : [];
  if (naturalKeys.length)
    predicates.push(
      ...naturalKeys.flatMap((group) => {
        const keys = group.split('+');
        return expectedRows.map(
          (row) =>
            `(${keys
              .map((key) =>
                row[key] == null
                  ? `${snake(key)} IS NULL`
                  : `${snake(key)}::text=${literal(String(row[key]), key)}`,
              )
              .join(' AND ')})`,
        );
      }),
    );
  if (scopePredicate) predicates.push(`(${scopePredicate})`);
  return predicates.length === 0 ? undefined : predicates.join(' OR ');
}

function queryRowsSql(
  table: string,
  expectedRows: ReadonlyArray<AnyRow>,
  naturalKeys: ReadonlyArray<string>,
  scopePredicate?: string,
): string {
  const predicate = buildStoreScopePredicate(expectedRows, naturalKeys, scopePredicate);
  return predicate
    ? `SELECT COALESCE(json_agg(to_jsonb(t)),'[]'::json)::text FROM ${table} t WHERE ${predicate}`
    : `SELECT '[]'::text`;
}

export interface Day44ReadRequest {
  database: Database;
  key: string;
  sql: string;
}

export function buildBatchedReadSql(requests: ReadonlyArray<Day44ReadRequest>): string {
  return requests
    .map((request) => {
      if (!/^[a-zA-Z0-9_.-]+$/.test(request.key)) throw new Error('Invalid Day 44 read key');
      const statement = request.sql.trim().replace(/;+$/, '');
      return `SELECT json_build_object('key','${request.key}','value',(SELECT to_jsonb(day44_value) FROM (${statement}) day44_read(day44_value)))::text;`;
    })
    .join('\n');
}

export function decodeBatchedReadOutput(output: string): Map<string, unknown> {
  const decoded = new Map<string, unknown>();
  for (const line of output.split(/\r?\n/).filter(Boolean)) {
    const envelope = JSON.parse(line) as { key?: unknown; value?: unknown };
    if (typeof envelope.key !== 'string' || decoded.has(envelope.key))
      throw new Error('Invalid Day 44 batched read output');
    decoded.set(envelope.key, envelope.value);
  }
  return decoded;
}

export function executeBatchedReads(
  requests: ReadonlyArray<Day44ReadRequest>,
  runner: (database: string, sql: string) => string = psql,
): Map<string, unknown> {
  const decoded = new Map<string, unknown>();
  for (const database of [...new Set(requests.map((request) => request.database))]) {
    const databaseRequests = requests.filter((request) => request.database === database);
    const databaseOutput = decodeBatchedReadOutput(
      runner(database, buildBatchedReadSql(databaseRequests)),
    );
    for (const request of databaseRequests) {
      if (!databaseOutput.has(request.key))
        throw new Error(`Day 44 batched read result is missing ${request.key}`);
      decoded.set(request.key, databaseOutput.get(request.key));
    }
  }
  return decoded;
}

function camelRecord(row: AnyRow): AnyRow {
  return Object.fromEntries(
    Object.entries(row).map(([key, value]) => [
      key.replace(/_([a-z])/g, (_match, letter: string) => letter.toUpperCase()),
      value,
    ]),
  );
}

export function validateProjectedStoreRows(
  table: string,
  expectedRows: ReadonlyArray<AnyRow>,
  rawRows: ReadonlyArray<AnyRow>,
  naturalKeys: ReadonlyArray<string> = [],
  syntheticKeys: ReadonlyArray<string> = [],
): AnyRow[] {
  return rawRows.map(camelRecord).map((actual) => {
    const expected = expectedRows.find(
      (candidate) =>
        (candidate.id != null && actual.id != null && candidate.id === actual.id) ||
        naturalKeys.some((group) =>
          group.split('+').every((key) => candidate[key] === actual[key]),
        ),
    );
    if (!expected) throw new Error(`Day 44 ${table} foreign natural-key collision`);
    const projected = Object.fromEntries(
      Object.keys(expected).map((key) => [
        key,
        syntheticKeys.includes(key) ? expected[key] : normalizeValue(actual[key], expected[key]),
      ]),
    );
    const comparableProjected = Object.fromEntries(
      Object.entries(projected).map(([key, value]) => [
        key,
        normalizeStructuredForCompare(table, key, value),
      ]),
    );
    const comparableExpected = Object.fromEntries(
      Object.entries(expected).map(([key, value]) => [
        key,
        normalizeStructuredForCompare(table, key, value),
      ]),
    );
    if (canonical(comparableProjected) !== canonical(comparableExpected))
      throw new Error(`Day 44 ${table} exact-ID/natural-key/full-state mismatch`);
    return { ...expected };
  });
}

export function projectIdentitySubscriptionPlansForStore(plans: ReadonlyArray<AnyRow>): AnyRow[] {
  return plans.map((plan) =>
    plan.id === starterPlan.id
      ? { ...plan, description: RUNTIME_STARTER_DESCRIPTION }
      : { ...plan },
  );
}

export function validateRagPreflightState(state: {
  expectedDocuments: number;
  expectedChunks: number;
  existingDocuments: number;
  existingChunks: number;
  dimensionReady: number;
  searchVectorReady: number;
}): void {
  const empty =
    state.existingDocuments === 0 &&
    state.existingChunks === 0 &&
    state.dimensionReady === 0 &&
    state.searchVectorReady === 0;
  const complete =
    state.existingDocuments === state.expectedDocuments &&
    state.existingChunks === state.expectedChunks &&
    state.dimensionReady === state.expectedChunks &&
    state.searchVectorReady === state.expectedChunks;
  if (!empty && !complete) throw new Error('Day 44 RAG vector/search state mismatch');
}

export function validateNoDemoOauthIdentities(count: number): void {
  if (count !== 0) throw new Error('Day 44 demo users must not have attached OAuth identities');
}

function preflightRealStore(
  plan: Day44SeedPlan,
  password: string,
): { walletVersion: number; hasFinancialState: boolean } {
  const fixtures = plan.fixtures as unknown as FixtureGraph;
  const identity = fixtures.identity;
  const trip = fixtures.trip;
  const commerce = fixtures.commerce;
  const rag = fixtures.rag;
  const operatorSql = Object.values(day44IdentityFixtureIds.operators)
    .map((id) => literal(id, 'operatorId'))
    .join(',');
  const passengerSql = day44IdentityFixtureIds.passengers
    .map((id) => literal(id, 'userId'))
    .join(',');
  const storeSubscriptionPlans = projectIdentitySubscriptionPlansForStore(
    identity.subscriptionPlans,
  );
  const storeUsers = identity.users.map(projectIdentityUserForStore);
  const routeScope = `SELECT id FROM vietride_trip.routes WHERE operator_id IN (${operatorSql})`;
  const tripScope = `SELECT id FROM vietride_trip.trips WHERE operator_id IN (${operatorSql})`;
  const alternativeScope = `SELECT id FROM vietride_trip.alternative_routes WHERE route_id IN (${routeScope})`;
  const tripKeys: Array<[string, string, string[], string]> = [
    [
      'vehicleTypes',
      'vietride_trip.vehicle_types',
      ['code'],
      `code IN ('STANDARD_BUS','LIMOUSINE','SLEEPER_BUS')`,
    ],
    ['stations', 'vietride_trip.stations', ['slug'], `slug LIKE 'day44-%'`],
    [
      'operatorStations',
      'vietride_trip.operator_stations',
      ['operatorId+stationId'],
      `operator_id IN (${operatorSql})`,
    ],
    ['stops', 'vietride_trip.stops', [], `operator_id IN (${operatorSql})`],
    ['routes', 'vietride_trip.routes', ['operatorId+name'], `operator_id IN (${operatorSql})`],
    [
      'routeStops',
      'vietride_trip.route_stops',
      ['routeId+orderIndex'],
      `route_id IN (${routeScope})`,
    ],
    ['alternativeRoutes', 'vietride_trip.alternative_routes', [], `route_id IN (${routeScope})`],
    [
      'alternativeRouteStops',
      'vietride_trip.alternative_route_stops',
      ['alternativeRouteId+orderIndex'],
      `alternative_route_id IN (${alternativeScope})`,
    ],
    ['vehicles', 'vietride_trip.vehicles', ['licensePlate'], `operator_id IN (${operatorSql})`],
    ['driverSchedules', 'vietride_trip.driver_schedules', [], `operator_id IN (${operatorSql})`],
    [
      'trips',
      'vietride_trip.trips',
      ['driverUserId+departureDateTime', 'vehicleId+departureDateTime'],
      `operator_id IN (${operatorSql})`,
    ],
    ['tripSeats', 'vietride_trip.trip_seats', ['tripId+seatNumber'], `trip_id IN (${tripScope})`],
    ['tripStops', 'vietride_trip.trip_stops', ['tripId+orderIndex'], `trip_id IN (${tripScope})`],
    ['tripStopFares', 'vietride_trip.trip_stop_fares', [], `trip_id IN (${tripScope})`],
  ];
  const businessOperatorSql = [
    day44IdentityFixtureIds.operators.A,
    day44IdentityFixtureIds.operators.B,
  ]
    .map((id) => literal(id, 'operatorId'))
    .join(',');
  const paymentIdSql = commerce.payments.map((row: AnyRow) => literal(row.id, 'id')).join(',');
  const eventIdSql = commerce.paymentOutboxEvents
    .map((row: AnyRow) => literal(row.id, 'eventId'))
    .join(',');
  const voucherIdSql = commerce.vouchers
    .map((row: AnyRow) => literal(row.id, 'voucherId'))
    .join(',');
  const commerceSpecs: Array<[string, Database, string, string[], string]> = [
    [
      'wallets',
      'vietride_payment',
      'vietride_payment.wallets',
      ['userId'],
      `user_id IN (${passengerSql})`,
    ],
    [
      'topUpRequests',
      'vietride_payment',
      'vietride_payment.top_up_requests',
      ['vnpayTxnRef'],
      `user_id IN (${passengerSql}) OR vnpay_txn_ref LIKE 'D44-%'`,
    ],
    [
      'walletTransactions',
      'vietride_payment',
      'vietride_payment.wallet_transactions',
      ['userId+referenceType+referenceId'],
      `user_id IN (${passengerSql})`,
    ],
    [
      'payments',
      'vietride_payment',
      'vietride_payment.payments',
      ['vnpayTxnRef', 'idempotencyKey'],
      `operator_id IN (${businessOperatorSql}) OR user_id IN (${passengerSql}) OR idempotency_key LIKE 'day44-v1:%' OR vnpay_txn_ref LIKE 'D44-%'`,
    ],
    [
      'paymentProcessedEvents',
      'vietride_payment',
      'vietride_payment.processed_integration_events',
      ['consumer+eventId'],
      `event_id IN (${eventIdSql})`,
    ],
    [
      'paymentOutboxEvents',
      'vietride_payment',
      'vietride_payment.outbox_events',
      [],
      `id IN (${eventIdSql}) OR payload::text LIKE '%day44-v1%'`,
    ],
    [
      'subscriptionUpgradeAttempts',
      'vietride_identity',
      'vietride_identity.subscription_upgrade_attempts',
      ['idempotencyKey'],
      `operator_id IN (${businessOperatorSql}) OR idempotency_key LIKE 'day44-v1:%'`,
    ],
    [
      'identityInboxEvents',
      'vietride_identity',
      'vietride_identity.integration_inbox',
      ['consumerName+messageId'],
      `message_id IN (${eventIdSql})`,
    ],
    [
      'invoices',
      'vietride_payment',
      'vietride_payment.invoices',
      ['invoiceNumber', 'paymentId'],
      `operator_id IN (${businessOperatorSql}) OR payment_id IN (${paymentIdSql})`,
    ],
    [
      'platformWalletTransactions',
      'vietride_payment',
      'vietride_payment.platform_wallet_transactions',
      ['type+referenceType+referenceId'],
      `TRUE`,
    ],
    [
      'vouchers',
      'vietride_booking',
      'vietride_booking.vouchers',
      ['code'],
      `code LIKE 'D44%' OR owner_operator_id IN (${operatorSql}) OR applicable_operator_ids && ARRAY[${operatorSql}]::uuid[]`,
    ],
    [
      'voucherConsents',
      'vietride_booking',
      'vietride_booking.operator_voucher_consents',
      ['voucherId+operatorId'],
      `voucher_id IN (${voucherIdSql}) OR operator_id IN (${operatorSql})`,
    ],
    [
      'parcelRouteFares',
      'vietride_parcel',
      'vietride_parcel.parcel_route_fares',
      ['routeId+sizeCategory'],
      `operator_id IN (${operatorSql})`,
    ],
  ];
  type RowSpec = [string, Database, string, ReadonlyArray<AnyRow>, string[], string[], string?];
  const bootstrapAdmins = [
    {
      id: identity.bootstrapSystemAdminId,
      role: 'SYSTEM_ADMIN',
      status: 'ACTIVE',
      deletedAt: null,
    },
  ];
  const rowSpecs: RowSpec[] = [
    [
      'identity.subscriptionPlans',
      'vietride_identity',
      'vietride_identity.subscription_plans',
      storeSubscriptionPlans,
      ['name'],
      [],
      `id IN ('00000000-0000-0000-0000-000000000001','44000000-0000-4000-8000-000000000001') OR name IN ('Starter (Free Trial)','Business (Demo)')`,
    ],
    [
      'identity.users',
      'vietride_identity',
      'vietride_identity.users',
      storeUsers,
      ['email', 'phone'],
      [],
      `id IN (${identity.users.map((row: AnyRow) => literal(row.id, 'id')).join(',')}) OR email LIKE '%@demo.vietride.local' OR operator_id IN (${operatorSql})`,
    ],
    [
      'identity.bootstrapAdmins',
      'vietride_identity',
      'vietride_identity.users',
      bootstrapAdmins,
      [],
      [],
    ],
    [
      'identity.operators',
      'vietride_identity',
      'vietride_identity.operators',
      identity.operators,
      ['businessRegistrationNumber', 'taxCode', 'contactEmail'],
      [],
      `id IN (${operatorSql}) OR contact_email LIKE 'operator.%@demo.vietride.local' OR name LIKE 'Day44 %'`,
    ],
    [
      'identity.subscriptions',
      'vietride_identity',
      'vietride_identity.operator_subscriptions',
      identity.subscriptions,
      ['operatorId'],
      [],
      `operator_id IN (${operatorSql})`,
    ],
    ...tripKeys.map(
      ([key, table, natural, scope]): RowSpec => [
        `trip.${key}`,
        'vietride_trip',
        table,
        trip[key] as AnyRow[],
        natural,
        [],
        scope,
      ],
    ),
    ...commerceSpecs.map(
      ([key, database, table, natural, scope]): RowSpec => [
        `commerce.${key}`,
        database,
        table,
        commerce[key] as AnyRow[],
        natural,
        [],
        scope,
      ],
    ),
    [
      'rag.documents',
      'vietride_rag',
      'vietride_rag.knowledge_documents',
      rag.documents,
      ['storagePath'],
      [],
      `storage_path LIKE 'day44-v1/%' OR operator_id='${day44IdentityFixtureIds.operators.A}'`,
    ],
    [
      'rag.chunks',
      'vietride_rag',
      'vietride_rag.knowledge_chunks',
      rag.chunks,
      ['documentId+chunkIndex'],
      ['searchVector'],
      `document_id IN (SELECT id FROM vietride_rag.knowledge_documents WHERE storage_path LIKE 'day44-v1/%' OR operator_id='${day44IdentityFixtureIds.operators.A}')`,
    ],
  ];
  const encoded = Buffer.from(password, 'utf8').toString('base64');
  const auxiliary: Day44ReadRequest[] = [
    {
      database: 'vietride_identity',
      key: 'identity.oauthCount',
      sql: `SELECT count(*) FROM vietride_identity.oauth_identities WHERE user_id IN (${identity.users.map((row: AnyRow) => literal(row.id, 'userId')).join(',')})`,
    },
    {
      database: 'vietride_identity',
      key: 'identity.credentialCount',
      sql: `WITH credentials AS (SELECT count(*) AS matched_count,count(DISTINCT password_hash) AS distinct_hashes,min(password_hash) AS password_hash,bool_and(password_hash LIKE '$2%$12$%') AS cost_ready FROM vietride_identity.users WHERE id IN (${identity.users.map((row: AnyRow) => literal(row.id, 'id')).join(',')})) SELECT CASE WHEN matched_count=${identity.users.length} AND distinct_hashes=1 AND cost_ready AND password_hash=crypt(convert_from(decode('${encoded}','base64'),'UTF8'),password_hash) THEN matched_count ELSE 0 END FROM credentials`,
    },
    {
      database: 'vietride_payment',
      key: 'payment.platformWallet',
      sql: `SELECT json_build_object('balance',balance,'rowVersion',row_version)::text FROM vietride_payment.platform_wallets`,
    },
    {
      database: 'vietride_rag',
      key: 'rag.readiness',
      sql: `SELECT json_build_object('dimensionReady',count(*) FILTER (WHERE vector_dims(embedding)=2048),'searchVectorReady',count(*) FILTER (WHERE search_vector IS NOT NULL))::text FROM vietride_rag.knowledge_chunks WHERE id IN (${rag.chunks.map((row: AnyRow) => literal(row.id, 'id')).join(',')})`,
    },
  ];
  const requests: Day44ReadRequest[] = [
    ...rowSpecs.map(([key, database, table, rows, natural, , scope]) => ({
      database,
      key,
      sql: queryRowsSql(table, rows, natural, scope),
    })),
    ...auxiliary,
  ];
  const results = executeBatchedReads(requests);
  const parsed = (key: string): unknown => {
    const value = results.get(key);
    if (typeof value !== 'string') return value;
    try {
      return JSON.parse(value);
    } catch {
      return value;
    }
  };
  const validated = Object.fromEntries(
    rowSpecs.map(([key, , table, rows, natural, synthetic]) => [
      key,
      validateProjectedStoreRows(table, rows, parsed(key) as AnyRow[], natural, synthetic),
    ]),
  ) as Record<string, AnyRow[]>;
  validateNoDemoOauthIdentities(Number(parsed('identity.oauthCount')));
  const validatedStoreSubscriptionPlans = validated['identity.subscriptionPlans'];
  const validatedStoreUsers = validated['identity.users'];
  const identityState = {
    bootstrapSystemAdmins: validated['identity.bootstrapAdmins'],
    subscriptionPlans: validatedStoreSubscriptionPlans.map((stored) => {
      const desired = identity.subscriptionPlans.find((item: AnyRow) => item.id === stored.id);
      if (!desired) throw new Error('Day 44 subscription plan projection lost its desired row');
      return { ...desired };
    }),
    operators: validated['identity.operators'],
    users: validatedStoreUsers.map((stored) => {
      const desired = identity.users.find((item: AnyRow) => item.id === stored.id);
      if (!desired) throw new Error('Day 44 user projection lost its desired row');
      return { ...desired };
    }),
    subscriptions: validated['identity.subscriptions'],
  };
  if (Number(parsed('identity.credentialCount')) !== identityState.users.length)
    throw new Error('Day 44 login credential mismatch');
  const tripState = Object.fromEntries(tripKeys.map(([key]) => [key, validated[`trip.${key}`]]));
  planDay44TripFixture({
    environment: process.env.NODE_ENV,
    startDate: plan.startDate,
    currentInstant: new Date(),
    existingState: tripState as never,
  });
  planDay44IdentityFixture({
    environment: process.env.NODE_ENV,
    accountPassword: password,
    startDate: plan.startDate,
    currentInstant: new Date(),
    tripDepartureInstantsByOperator: trip.tripDepartureInstantsByOperator,
    existingState: identityState as never,
  });
  const commerceState = Object.fromEntries(
    commerceSpecs.map(([key]) => [key, validated[`commerce.${key}`]]),
  ) as AnyRow;
  const wallet = (parsed('payment.platformWallet') ?? {}) as {
    balance?: number;
    rowVersion?: number;
  };
  if (wallet.balance === undefined || wallet.rowVersion === undefined)
    throw new Error('Platform wallet singleton is missing');
  commerceState.platformWalletBalance = Number(wallet.balance);
  planDay44CommerceFixture({
    environment: process.env.NODE_ENV,
    startDate: plan.startDate,
    currentInstant: new Date(),
    references: {
      bootstrapSystemAdminId: identity.bootstrapSystemAdminId,
      businessPlanId: String(identity.subscriptionPlans[1].id),
      operatorIds: {
        A: day44IdentityFixtureIds.operators.A,
        B: day44IdentityFixtureIds.operators.B,
      },
      operatorAdminIds: {
        A: day44IdentityFixtureIds.operatorAdmins.A,
        B: day44IdentityFixtureIds.operatorAdmins.B,
      },
      subscriptionIds: {
        A: day44IdentityFixtureIds.subscriptions.A,
        B: day44IdentityFixtureIds.subscriptions.B,
      },
      passengerIds: day44IdentityFixtureIds.passengers,
      routeIds: { A: String(trip.routes[0].id), B: String(trip.routes[3].id) },
    },
    existingState: commerceState as never,
  });

  const existingRagDocuments = validated['rag.documents'];
  const existingRagChunks = validated['rag.chunks'];
  const ragReadiness = parsed('rag.readiness') as {
    dimensionReady: number;
    searchVectorReady: number;
  };
  validateRagPreflightState({
    expectedDocuments: rag.documents.length,
    expectedChunks: rag.chunks.length,
    existingDocuments: existingRagDocuments.length,
    existingChunks: existingRagChunks.length,
    dimensionReady: Number(ragReadiness.dimensionReady),
    searchVectorReady: Number(ragReadiness.searchVectorReady),
  });
  return {
    walletVersion: Number(wallet.rowVersion),
    hasFinancialState: (commerceState.platformWalletTransactions as AnyRow[]).length > 0,
  };
}

export function buildChecksumScopePredicate(
  table: string,
  rows: ReadonlyArray<AnyRow>,
  emptyScope?: string,
): string | undefined {
  const rowKeys: Readonly<Record<string, ReadonlyArray<string>>> = {
    'vietride_trip.operator_stations': ['operatorId', 'stationId'],
    'vietride_trip.route_stops': ['routeId', 'stopId'],
    'vietride_trip.alternative_route_stops': ['alternativeRouteId', 'stopId'],
    'vietride_trip.trip_stops': ['tripId', 'stopId'],
    'vietride_trip.trip_stop_fares': ['tripId', 'stopId'],
    'vietride_payment.wallets': ['userId'],
    'vietride_parcel.parcel_route_fares': ['routeId', 'sizeCategory'],
  };
  const keys = rowKeys[table];
  if (keys && rows.length)
    return rows
      .map(
        (row) =>
          `(${keys.map((key) => `${snake(key)}::text=${literal(row[key], key)}`).join(' AND ')})`,
      )
      .join(' OR ');
  if (rows.length) {
    const ids = rows.map((row) => row.id).filter(Boolean);
    if (ids.length !== rows.length)
      throw new Error(`Day 44 checksum key projection is missing for ${table}`);
    return `id::text IN (${ids.map((id) => literal(id, 'id')).join(',')})`;
  }
  return emptyScope;
}

function realStoreChecksum(plan: Day44SeedPlan): string {
  const fixtures = plan.fixtures as unknown as FixtureGraph;
  const specifications: Array<[Database, string, AnyRow[]]> = [
    [
      'vietride_identity',
      'vietride_identity.subscription_plans',
      fixtures.identity.subscriptionPlans.slice(1),
    ],
    ['vietride_identity', 'vietride_identity.operators', fixtures.identity.operators],
    [
      'vietride_identity',
      'vietride_identity.users',
      fixtures.identity.users.map(projectIdentityUserForStore),
    ],
    ['vietride_identity', 'vietride_identity.oauth_identities', []],
    [
      'vietride_identity',
      'vietride_identity.operator_subscriptions',
      fixtures.identity.subscriptions,
    ],
    [
      'vietride_identity',
      'vietride_identity.subscription_upgrade_attempts',
      fixtures.commerce.subscriptionUpgradeAttempts,
    ],
    [
      'vietride_identity',
      'vietride_identity.integration_inbox',
      fixtures.commerce.identityInboxEvents,
    ],
  ];
  const tripTables: Array<[string, string]> = [
    ['vehicle_types', 'vehicleTypes'],
    ['stations', 'stations'],
    ['operator_stations', 'operatorStations'],
    ['stops', 'stops'],
    ['routes', 'routes'],
    ['route_stops', 'routeStops'],
    ['alternative_routes', 'alternativeRoutes'],
    ['alternative_route_stops', 'alternativeRouteStops'],
    ['vehicles', 'vehicles'],
    ['driver_schedules', 'driverSchedules'],
    ['trips', 'trips'],
    ['trip_seats', 'tripSeats'],
    ['trip_stops', 'tripStops'],
    ['trip_stop_fares', 'tripStopFares'],
  ];
  tripTables.forEach(([table, key]) =>
    specifications.push([
      'vietride_trip',
      `vietride_trip.${table}`,
      fixtures.trip[key] as AnyRow[],
    ]),
  );
  const paymentTables: Array<[string, string]> = [
    ['wallets', 'wallets'],
    ['top_up_requests', 'topUpRequests'],
    ['wallet_transactions', 'walletTransactions'],
    ['payments', 'payments'],
    ['processed_integration_events', 'paymentProcessedEvents'],
    ['outbox_events', 'paymentOutboxEvents'],
    ['invoices', 'invoices'],
    ['platform_wallet_transactions', 'platformWalletTransactions'],
  ];
  paymentTables.forEach(([table, key]) =>
    specifications.push([
      'vietride_payment',
      `vietride_payment.${table}`,
      fixtures.commerce[key] as AnyRow[],
    ]),
  );
  specifications.push(
    ['vietride_booking', 'vietride_booking.vouchers', fixtures.commerce.vouchers],
    [
      'vietride_booking',
      'vietride_booking.operator_voucher_consents',
      fixtures.commerce.voucherConsents,
    ],
    ['vietride_parcel', 'vietride_parcel.parcel_route_fares', fixtures.commerce.parcelRouteFares],
    ['vietride_rag', 'vietride_rag.knowledge_documents', fixtures.rag.documents],
    ['vietride_rag', 'vietride_rag.knowledge_chunks', fixtures.rag.chunks],
  );
  const checksumRequests: Day44ReadRequest[] = specifications.map(([database, table, rows]) => {
    const emptyScopes: Readonly<Record<string, string>> = {
      'vietride_identity.oauth_identities': `user_id IN (${fixtures.identity.users
        .map((row: AnyRow) => literal(row.id, 'userId'))
        .join(',')})`,
      'vietride_trip.trip_stop_fares': `trip_id IN (SELECT id FROM vietride_trip.trips WHERE operator_id IN (${Object.values(
        day44IdentityFixtureIds.operators,
      )
        .map((id) => literal(id, 'operatorId'))
        .join(',')}))`,
    };
    const where = buildChecksumScopePredicate(table, rows, emptyScopes[table]);
    if (!where) throw new Error(`Day 44 checksum scope is missing for ${table}`);
    return {
      database,
      key: `checksum.${table}`,
      sql: `SELECT COALESCE(json_agg(to_jsonb(t) ORDER BY COALESCE(to_jsonb(t)->>'id',to_jsonb(t)::text)),'[]'::json)::text FROM ${table} t WHERE ${where}`,
    };
  });
  checksumRequests.push({
    database: 'vietride_payment',
    key: 'checksum.platform_wallet',
    sql: `SELECT json_build_object('balance',balance,'rowVersion',row_version)::text FROM vietride_payment.platform_wallets`,
  });
  const results = executeBatchedReads(checksumRequests);
  const snapshot: unknown[] = specifications.map(([, table]) => [
    table,
    results.get(`checksum.${table}`),
  ]);
  snapshot.push(['platform_wallet', results.get('checksum.platform_wallet')]);
  return createHash('sha256').update(canonical(snapshot)).digest('hex');
}

export const DAY44_PSQL_MAX_BUFFER_BYTES = 64 * 1024 * 1024;

interface PsqlSpawnResult {
  status: number | null;
  stdout?: string | null;
  stderr?: string | null;
  error?: (Error & { code?: string }) | undefined;
}

function boundedPsqlDiagnostic(stderr: string | null | undefined): string {
  const safe = (stderr ?? '')
    .split(/\r?\n/)
    .filter((line) => /^(ERROR|DETAIL|HINT|CONTEXT):/.test(line))
    .filter((line) => !/(password|authorization|crypt\(|decode\()/i.test(line))
    .join('\n')
    .slice(0, 2000);
  return safe || 'PostgreSQL stderr unavailable or suppressed';
}

export function readPsqlSpawnResult(result: PsqlSpawnResult): string {
  if (result.error) {
    const code = result.error.code ?? result.error.name ?? 'SPAWN_ERROR';
    throw new Error(`Day 44 database command execution failed (${code})`);
  }
  if (result.status !== 0)
    throw new Error(
      `Day 44 database command failed (exit ${result.status ?? 'unknown'}): ${boundedPsqlDiagnostic(result.stderr)}`,
    );
  return result.stdout?.trim() ?? '';
}

function psql(database: string, sql: string): string {
  const container = process.env.DAY44_POSTGRES_CONTAINER;
  if (!container) throw new Error('DAY44_POSTGRES_CONTAINER is required');
  const result = spawnSync(
    'docker',
    [
      'exec',
      '-i',
      container,
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-At',
      '-U',
      process.env.POSTGRES_USER ?? 'vietride',
      '-d',
      database,
    ],
    {
      input: sql,
      encoding: 'utf8',
      env: process.env,
      // 3,948 projected TripSeat rows are several MiB; 64 MiB leaves bounded headroom
      // for the complete strict-state graph without permitting unbounded child output.
      maxBuffer: DAY44_PSQL_MAX_BUFFER_BYTES,
    },
  );
  return readPsqlSpawnResult(result);
}

export function injectPlatformWalletUpdate(
  paymentSql: string,
  walletVersion: number,
  hasFinancialState: boolean,
): string {
  const marker = '/*DAY44_PLATFORM_WALLET_UPDATE*/';
  if (paymentSql.split(marker).length !== 2)
    throw new Error('Day 44 Payment batch must contain exactly one platform wallet marker');
  const walletUpdate = hasFinancialState
    ? `DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM vietride_payment.platform_wallets WHERE balance=4000000 AND row_version=${walletVersion}) THEN RAISE EXCEPTION 'Day 44 platform wallet rerun mismatch'; END IF; END $$;`
    : `DO $$ DECLARE changed int; BEGIN UPDATE vietride_payment.platform_wallets SET balance=4000000,row_version=row_version+1,updated_at=now() WHERE balance=0 AND row_version=${walletVersion}; GET DIAGNOSTICS changed=ROW_COUNT; IF changed<>1 THEN RAISE EXCEPTION 'Day 44 platform wallet optimistic update failed'; END IF; END $$;`;
  return paymentSql.replace(marker, () => walletUpdate);
}

export function runDay44Seed(options: SeedOptions): Day44SeedPlan {
  const validated = validateSeedOptions(options);
  const admins = psql(
    'vietride_identity',
    "SELECT id FROM vietride_identity.users WHERE role='SYSTEM_ADMIN' AND status='ACTIVE' AND deleted_at IS NULL ORDER BY id;",
  );
  const adminIds = admins.split(/\r?\n/).filter(Boolean);
  if (adminIds.length !== 1)
    throw new Error(
      `Day 44 requires exactly one active non-deleted System Admin; found ${adminIds.length}`,
    );
  const adminId = adminIds[0];
  const plan = buildDay44SeedPlan(validated, adminId);
  const preflight = preflightRealStore(plan, validated.password);
  plan.batches.forEach(({ database, sql }) => {
    let executable = sql;
    if (database === 'vietride_payment') {
      executable = injectPlatformWalletUpdate(
        executable,
        preflight.walletVersion,
        preflight.hasFinancialState,
      );
    }
    psql(database, executable);
  });
  plan.checksum = realStoreChecksum(plan);
  console.log(`DAY44_SEED_CHECKSUM=${plan.checksum}`);
  console.log('DAY44_SEED=PASS');
  return plan;
}

function argument(name: string): string | undefined {
  const prefix = `${name}=`;
  return process.argv
    .slice(2)
    .find((value) => value.startsWith(prefix))
    ?.slice(prefix.length);
}

if (require.main === module) runDay44Seed({ startDate: argument('--start-date') });
