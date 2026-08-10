interface NodeHash {
  update(value: Uint8Array): NodeHash;
  digest(): Uint8Array;
}

declare const require: (moduleName: string) => unknown;

const { createHash } = require('node:crypto') as {
  createHash(algorithm: 'sha1'): NodeHash;
};

const VIETNAM_OFFSET_MILLISECONDS = 7 * 60 * 60 * 1000;
const UUID_NAMESPACE = '44000000-0000-5000-8000-000000000001';
const STARTER_PLAN_ID = '00000000-0000-0000-0000-000000000001';
const BUSINESS_PLAN_ID = '44000000-0000-4000-8000-000000000001';
const BUSINESS_DESCRIPTION =
  'Day 44 demo-only Business plan; commercial pricing is non-canonical and must not be used as production policy.';

type OperatorLetter = 'A' | 'B' | 'C';
type UserRole = 'OPERATOR_ADMIN' | 'DRIVER' | 'ASSISTANT' | 'PASSENGER';

export interface IdentitySubscriptionPlanFixture {
  id: string;
  name: string;
  description: string;
  pricePerMonth: number;
  pricePerYear: number;
  maxVehicles: number;
  maxDrivers: number;
  maxAssistants: number;
  maxOperatorUsers: number;
  maxRoutes: number;
  maxTripsPerMonth: number;
  enableParcel: boolean;
  enableShuttle: boolean;
  enableRag: boolean;
  isActive: true;
  createdAt?: string;
  updatedAt?: string;
}

export interface IdentityOperatorFixture {
  id: string;
  name: string;
  businessRegistrationNumber: string;
  taxCode: string;
  contactEmail: string;
  contactPhone: string;
  logoUrl: null;
  addressStreet: '44 Demo Street';
  addressWard: 'Demo Ward';
  addressProvince: 'Hồ Chí Minh';
  representativeName: string;
  representativePhone: string;
  registrationStatus: 'APPROVED';
  approvedAt: null;
  approvedByUserId: null;
  rejectedAt: null;
  rejectedByUserId: null;
  rejectReason: null;
  suspendedAt: null;
  suspendReason: null;
  cancellationPolicy: null;
  parcelNoShowPolicy: null;
  luggagePolicy: null;
  bankAccountName: null;
  bankAccountNumber: null;
  bankName: null;
  isActive: true;
  deletedAt: null;
  createdAt: string;
  updatedAt: string;
}

export interface IdentityUserFixture {
  id: string;
  email: string;
  phone: string;
  displayName: string;
  avatarUrl: null;
  role: UserRole;
  status: 'ACTIVE';
  lockedFromStatus: null;
  operatorId: string | null;
  failedLoginAttempts: 0;
  lastFailedLoginAt: null;
  lastLoginAt: null;
  dateOfBirth: null;
  gender: null;
  oauthIdentity: null;
  credentialState: 'LOGIN_READY_BCRYPT_COST_12';
  deletedAt: null;
  createdAt: string;
  updatedAt: string;
}

export interface IdentityOperatorSubscriptionFixture {
  id: string;
  operatorId: string;
  activePlanId: string;
  status: 'ACTIVE';
  startedAt: string;
  expiresAt: string;
  paymentMethod: 'VNPAY' | null;
  billingPeriod: 'MONTHLY' | null;
  currentVehicles: 3;
  currentDrivers: 3;
  currentAssistants: 1;
  currentOperatorUsers: 1;
  currentRoutes: 3;
  currentTripsThisMonth: number;
  lastResetAt: string;
  trialExpiringWarnSentAt: null;
  createdAt: string;
  updatedAt: string;
}

export interface ExistingIdentityFixtureState {
  bootstrapSystemAdmins: ReadonlyArray<{
    id: string;
    role: 'SYSTEM_ADMIN';
    status: 'ACTIVE';
    deletedAt: null;
  }>;
  subscriptionPlans: ReadonlyArray<IdentitySubscriptionPlanFixture>;
  operators: ReadonlyArray<IdentityOperatorFixture>;
  users: ReadonlyArray<IdentityUserFixture>;
  subscriptions: ReadonlyArray<IdentityOperatorSubscriptionFixture>;
}

export interface Day44IdentityPlannerInput {
  environment: string | undefined;
  accountPassword: string | undefined;
  startDate: string;
  currentInstant: Date;
  tripDepartureInstantsByOperator: Readonly<Record<OperatorLetter, ReadonlyArray<string>>>;
  existingState: ExistingIdentityFixtureState;
  logger?: {
    info(message: string, fields: Readonly<Record<string, unknown>>): void;
  };
}

export interface Day44IdentityFixturePlan {
  schemaVersion: 1;
  namespace: 'day44-v1';
  timezone: 'Asia/Ho_Chi_Minh';
  startDate: string;
  bootstrapSystemAdminId: string;
  credentialPolicy: {
    source: 'DEMO_SEED_ACCOUNT_PASSWORD';
    lifecycle: 'EXISTING_IDENTITY_BCRYPT_COST_12';
  };
  subscriptionPlans: ReadonlyArray<IdentitySubscriptionPlanFixture>;
  operators: ReadonlyArray<IdentityOperatorFixture>;
  users: ReadonlyArray<IdentityUserFixture>;
  subscriptions: ReadonlyArray<IdentityOperatorSubscriptionFixture>;
}

export const day44IdentityFixtureIds = Object.freeze({
  operators: Object.freeze({
    A: '6276b48c-3984-582b-9c35-0c2fbe20baa7',
    B: 'd63b3c32-8c12-5130-a347-0ef8df286605',
    C: '8554beea-8b1b-57c5-bb87-8d1f136654a3',
  }),
  operatorAdmins: Object.freeze({
    A: '9c90f052-9323-5c47-9402-ad100db3dec9',
    B: '65cfe24b-a43e-5dad-b43d-c6bf1b3cd914',
    C: 'e21cf2e5-c8fc-5155-a8bb-345a4e6f3f8b',
  }),
  drivers: Object.freeze({
    A: Object.freeze([
      '6a61b1d5-4c98-5f40-8e0f-494651deebfa',
      '1432b243-ab2b-5a33-8db5-5441efd4d489',
      '67086aa7-71f3-5f60-9d13-f7f30bb8c7c8',
    ]),
    B: Object.freeze([
      'ea9c2b90-c811-5281-9793-4722253b5b17',
      'aeebce20-d2d9-525c-9394-8c43c6cf8800',
      'f55eadcb-f314-5e35-898a-6d5ddad291aa',
    ]),
    C: Object.freeze([
      '6e236fff-7856-51c4-917c-89c6724b7d60',
      'a052ed42-ef29-5180-b92e-317b01b92b65',
      '04ebbfdc-c20c-5f1c-b145-030eb9e247d4',
    ]),
  }),
  assistants: Object.freeze({
    A: '316ba0dc-6bea-5173-858d-4c9c3cde50de',
    B: '2b7ae533-41e1-5fb6-9875-76e8923c4916',
    C: 'f0931d74-4698-59a6-8eb6-de775b44e6fe',
  }),
  passengers: Object.freeze([
    '167b6f1c-e47d-56cd-9715-1d9b75637cd3',
    'c251549f-b0d5-5d73-9e36-50ff74bf69f2',
    '6288dc1d-ac87-50b6-8b85-f45e7852ea50',
    'b5ec73ed-ae93-5fb7-b0fe-c61ada94d4ba',
    'fc58a993-6184-5cf1-971d-c38118fbbee7',
    'b41d9085-e396-5014-ab7a-67e6b2d6fd88',
    '4ca78bdc-23ba-5a01-b40a-49e2d84f69c5',
    '1fcc1bb2-20fb-5c8f-bea4-41f319ed885f',
    '99aa3004-333a-5105-8fd4-09d8f366de92',
    '820ece02-0f0c-5bb4-90d4-0d5bbf0962ec',
  ]),
  subscriptions: Object.freeze({
    A: '9b7f508d-7215-5228-af11-f3d29ff5e14b',
    B: 'fe24eec8-2cbd-523b-8710-5e4276541ab0',
    C: '5d5879bb-7e22-5bc2-97e4-bbf923dd4739',
  }),
});

const letters: ReadonlyArray<OperatorLetter> = ['A', 'B', 'C'];

function uuidToBytes(uuid: string): Uint8Array {
  return Uint8Array.from(uuid.replaceAll('-', '').match(/.{2}/g) ?? [], (byte) =>
    Number.parseInt(byte, 16),
  );
}

function uuidV5(key: string): string {
  const namespace = uuidToBytes(UUID_NAMESPACE);
  const name = new TextEncoder().encode(key);
  const source = new Uint8Array(namespace.length + name.length);
  source.set(namespace);
  source.set(name, namespace.length);
  const bytes = createHash('sha1').update(source).digest().slice(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x50;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function assertFixtureId(key: string, listedId: string): void {
  if (uuidV5(key) !== listedId) {
    throw new Error(
      `Day 44 manifest contains an unlisted or invalid identity fixture ID for ${key}`,
    );
  }
}

function parseDate(date: string): { year: number; month: number; day: number } {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
  if (!match) throw new Error('startDate must use YYYY-MM-DD');
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const check = new Date(Date.UTC(year, month - 1, day));
  if (
    check.getUTCFullYear() !== year ||
    check.getUTCMonth() !== month - 1 ||
    check.getUTCDate() !== day
  ) {
    throw new Error('startDate is not a valid Asia/Ho_Chi_Minh calendar date');
  }
  return { year, month, day };
}

function ictInstant(year: number, month: number, day: number): Date {
  return new Date(Date.UTC(year, month - 1, day) - VIETNAM_OFFSET_MILLISECONDS);
}

function addVietnamDays(date: string, days: number): Date {
  const value = parseDate(date);
  return new Date(ictInstant(value.year, value.month, value.day).getTime() + days * 86_400_000);
}

function addVietnamMonth(date: string): Date {
  const value = parseDate(date);
  const targetMonthIndex = value.month;
  const targetYear = value.year + Math.floor(targetMonthIndex / 12);
  const targetMonth = (targetMonthIndex % 12) + 1;
  const lastDay = new Date(Date.UTC(targetYear, targetMonth, 0)).getUTCDate();
  return ictInstant(targetYear, targetMonth, Math.min(value.day, lastDay));
}

function ictDateParts(instant: Date): { year: number; month: number; day: number } {
  const shifted = new Date(instant.getTime() + VIETNAM_OFFSET_MILLISECONDS);
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth() + 1,
    day: shifted.getUTCDate(),
  };
}

function compareDate(
  left: { year: number; month: number; day: number },
  right: typeof left,
): number {
  return (
    left.year * 10_000 +
    left.month * 100 +
    left.day -
    (right.year * 10_000 + right.month * 100 + right.day)
  );
}

function assertPlannerInputs(input: Day44IdentityPlannerInput): void {
  if (input.environment?.trim().toLowerCase() === 'production') {
    throw new Error('Day 44 identity fixture is forbidden in Production');
  }
  if (!input.accountPassword?.trim()) {
    throw new Error('DEMO_SEED_ACCOUNT_PASSWORD is required');
  }
  if (Number.isNaN(input.currentInstant.getTime())) throw new Error('currentInstant is invalid');
  const start = parseDate(input.startDate);
  if (compareDate(start, ictDateParts(input.currentInstant)) <= 0) {
    throw new Error('startDate must be at least one day after the current Asia/Ho_Chi_Minh date');
  }
}

function fixtureTimestamp(date: Date): string {
  return date.toISOString();
}

function buildPlans(createdAt: string): ReadonlyArray<IdentitySubscriptionPlanFixture> {
  return [
    {
      id: STARTER_PLAN_ID,
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
    },
    {
      id: BUSINESS_PLAN_ID,
      name: 'Business (Demo)',
      description: BUSINESS_DESCRIPTION,
      pricePerMonth: 2_000_000,
      pricePerYear: 20_000_000,
      maxVehicles: 20,
      maxDrivers: 40,
      maxAssistants: 40,
      maxOperatorUsers: 20,
      maxRoutes: 30,
      maxTripsPerMonth: 2_000,
      enableParcel: true,
      enableShuttle: true,
      enableRag: true,
      isActive: true,
      createdAt,
      updatedAt: createdAt,
    },
  ];
}

function buildOperators(createdAt: string): ReadonlyArray<IdentityOperatorFixture> {
  return letters.map((letter, index) => {
    const number = index + 1;
    const phone = `+8490444000${number}`;
    return {
      id: day44IdentityFixtureIds.operators[letter],
      name: `Day44 ${letter === 'C' ? 'Starter' : 'Business'} Operator ${letter}`,
      businessRegistrationNumber: `D44-BRN-${letter}`,
      taxCode: `D44-TAX-${letter}`,
      contactEmail: `operator.${letter.toLowerCase()}@demo.vietride.local`,
      contactPhone: phone,
      logoUrl: null,
      addressStreet: '44 Demo Street',
      addressWard: 'Demo Ward',
      addressProvince: 'Hồ Chí Minh',
      representativeName: `Day44 Operator ${letter} Admin`,
      representativePhone: phone,
      registrationStatus: 'APPROVED',
      approvedAt: null,
      approvedByUserId: null,
      rejectedAt: null,
      rejectedByUserId: null,
      rejectReason: null,
      suspendedAt: null,
      suspendReason: null,
      cancellationPolicy: null,
      parcelNoShowPolicy: null,
      luggagePolicy: null,
      bankAccountName: null,
      bankAccountNumber: null,
      bankName: null,
      isActive: true,
      deletedAt: null,
      createdAt,
      updatedAt: createdAt,
    };
  });
}

function userFixture(
  id: string,
  email: string,
  phone: string,
  displayName: string,
  role: UserRole,
  operatorId: string | null,
  createdAt: string,
): IdentityUserFixture {
  return {
    id,
    email,
    phone,
    displayName,
    avatarUrl: null,
    role,
    status: 'ACTIVE',
    lockedFromStatus: null,
    operatorId,
    failedLoginAttempts: 0,
    lastFailedLoginAt: null,
    lastLoginAt: null,
    dateOfBirth: null,
    gender: null,
    oauthIdentity: null,
    credentialState: 'LOGIN_READY_BCRYPT_COST_12',
    deletedAt: null,
    createdAt,
    updatedAt: createdAt,
  };
}

function buildUsers(createdAt: string): ReadonlyArray<IdentityUserFixture> {
  const users: IdentityUserFixture[] = [];
  letters.forEach((letter, operatorIndex) => {
    const lower = letter.toLowerCase();
    const operatorId = day44IdentityFixtureIds.operators[letter];
    users.push(
      userFixture(
        day44IdentityFixtureIds.operatorAdmins[letter],
        `operator.${lower}@demo.vietride.local`,
        `+8490444010${operatorIndex + 1}`,
        `Day44 Operator ${letter} Admin`,
        'OPERATOR_ADMIN',
        operatorId,
        createdAt,
      ),
    );
    day44IdentityFixtureIds.drivers[letter].forEach((id, driverIndex) => {
      users.push(
        userFixture(
          id,
          `driver.${lower}${driverIndex + 1}@demo.vietride.local`,
          `+849044410${operatorIndex + 1}${driverIndex + 1}`,
          `Day44 Driver ${letter}${driverIndex + 1}`,
          'DRIVER',
          operatorId,
          createdAt,
        ),
      );
    });
    users.push(
      userFixture(
        day44IdentityFixtureIds.assistants[letter],
        `assistant.${lower}@demo.vietride.local`,
        `+8490444020${operatorIndex + 1}`,
        `Day44 Assistant ${letter}`,
        'ASSISTANT',
        operatorId,
        createdAt,
      ),
    );
  });
  day44IdentityFixtureIds.passengers.forEach((id, index) => {
    const suffix = String(index + 1).padStart(2, '0');
    users.push(
      userFixture(
        id,
        `passenger${suffix}@demo.vietride.local`,
        `+849044403${suffix}`,
        `Day44 Passenger ${suffix}`,
        'PASSENGER',
        null,
        createdAt,
      ),
    );
  });
  return users;
}

function tripCountForStartMonth(input: Day44IdentityPlannerInput, letter: OperatorLetter): number {
  const departures = input.tripDepartureInstantsByOperator[letter];
  if (departures.length !== 42) {
    throw new Error(`Operator ${letter} must provide exactly 42 materialized Trip departures`);
  }
  const unique = new Set(departures);
  if (unique.size !== departures.length) {
    throw new Error(`Operator ${letter} Trip departure input contains duplicates`);
  }
  const start = parseDate(input.startDate);
  return departures.reduce((count, value) => {
    const instant = new Date(value);
    if (Number.isNaN(instant.getTime()))
      throw new Error(`Operator ${letter} has an invalid Trip instant`);
    const parts = ictDateParts(instant);
    return count + (parts.year === start.year && parts.month === start.month ? 1 : 0);
  }, 0);
}

function buildSubscriptions(
  input: Day44IdentityPlannerInput,
  createdAt: string,
  startedAt: string,
): ReadonlyArray<IdentityOperatorSubscriptionFixture> {
  const start = parseDate(input.startDate);
  const lastResetAt = fixtureTimestamp(ictInstant(start.year, start.month, 1));
  return letters.map((letter) => ({
    id: day44IdentityFixtureIds.subscriptions[letter],
    operatorId: day44IdentityFixtureIds.operators[letter],
    activePlanId: letter === 'C' ? STARTER_PLAN_ID : BUSINESS_PLAN_ID,
    status: 'ACTIVE',
    startedAt,
    expiresAt: fixtureTimestamp(
      letter === 'C' ? addVietnamDays(input.startDate, 30) : addVietnamMonth(input.startDate),
    ),
    paymentMethod: letter === 'C' ? null : 'VNPAY',
    billingPeriod: letter === 'C' ? null : 'MONTHLY',
    currentVehicles: 3,
    currentDrivers: 3,
    currentAssistants: 1,
    currentOperatorUsers: 1,
    currentRoutes: 3,
    currentTripsThisMonth: tripCountForStartMonth(input, letter),
    lastResetAt,
    trialExpiringWarnSentAt: null,
    createdAt,
    updatedAt: createdAt,
  }));
}

function assertListedIds(): void {
  letters.forEach((letter) => {
    const lower = letter.toLowerCase();
    assertFixtureId(`identity:operator:${lower}`, day44IdentityFixtureIds.operators[letter]);
    assertFixtureId(
      `identity:user:operator-admin:${lower}`,
      day44IdentityFixtureIds.operatorAdmins[letter],
    );
    day44IdentityFixtureIds.drivers[letter].forEach((id, index) =>
      assertFixtureId(`identity:user:driver:${lower}:${index + 1}`, id),
    );
    assertFixtureId(`identity:user:assistant:${lower}`, day44IdentityFixtureIds.assistants[letter]);
    assertFixtureId(
      `identity:subscription:${lower}`,
      day44IdentityFixtureIds.subscriptions[letter],
    );
  });
  day44IdentityFixtureIds.passengers.forEach((id, index) =>
    assertFixtureId(`identity:user:passenger:${String(index + 1).padStart(2, '0')}`, id),
  );
}

function assertCollisionGate<T>(
  label: string,
  existing: ReadonlyArray<T>,
  expected: ReadonlyArray<T>,
): void {
  const canonicalJson = (value: unknown): string => {
    const canonicalize = (current: unknown): unknown => {
      if (Array.isArray(current)) return current.map(canonicalize);
      if (current !== null && typeof current === 'object') {
        return Object.fromEntries(
          Object.entries(current)
            .sort(([left], [right]) => left.localeCompare(right))
            .map(([key, child]) => [key, canonicalize(child)]),
        );
      }
      return current;
    };
    return JSON.stringify(canonicalize(value));
  };
  existing.forEach((row) => {
    if (!expected.some((fixture) => canonicalJson(fixture) === canonicalJson(row))) {
      throw new Error(`Day 44 ${label} foreign natural-key collision or full-state mismatch`);
    }
  });
}

function assertExistingState(
  existing: ExistingIdentityFixtureState,
  plans: ReadonlyArray<IdentitySubscriptionPlanFixture>,
  operators: ReadonlyArray<IdentityOperatorFixture>,
  users: ReadonlyArray<IdentityUserFixture>,
  subscriptions: ReadonlyArray<IdentityOperatorSubscriptionFixture>,
): string {
  if (existing.bootstrapSystemAdmins.length !== 1) {
    throw new Error('Day 44 requires exactly one existing ACTIVE bootstrap System Admin');
  }
  const admin = existing.bootstrapSystemAdmins[0];
  if (admin.role !== 'SYSTEM_ADMIN' || admin.status !== 'ACTIVE' || admin.deletedAt !== null) {
    throw new Error('Day 44 bootstrap System Admin is not login-ready');
  }
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(admin.id)
  ) {
    throw new Error('Day 44 bootstrap System Admin ID is invalid');
  }
  assertCollisionGate('SubscriptionPlan', existing.subscriptionPlans, plans);
  if (!existing.subscriptionPlans.some((plan) => plan.id === STARTER_PLAN_ID)) {
    throw new Error('Canonical Starter subscription plan is missing');
  }
  assertCollisionGate('Operator', existing.operators, operators);
  assertCollisionGate('User', existing.users, users);
  assertCollisionGate('OperatorSubscription', existing.subscriptions, subscriptions);
  return admin.id;
}

export function planDay44IdentityFixture(
  input: Day44IdentityPlannerInput,
): Day44IdentityFixturePlan {
  assertPlannerInputs(input);
  assertListedIds();
  const startedAt = fixtureTimestamp(addVietnamDays(input.startDate, 0));
  const createdAt = fixtureTimestamp(addVietnamDays(input.startDate, -2));
  const subscriptionPlans = buildPlans(createdAt);
  const operators = buildOperators(createdAt);
  const users = buildUsers(createdAt);
  const subscriptions = buildSubscriptions(input, createdAt, startedAt);
  const bootstrapSystemAdminId = assertExistingState(
    input.existingState,
    subscriptionPlans,
    operators,
    users,
    subscriptions,
  );
  input.logger?.info('Day 44 identity fixture plan validated', {
    operators: operators.length,
    users: users.length,
    subscriptions: subscriptions.length,
  });
  return {
    schemaVersion: 1,
    namespace: 'day44-v1',
    timezone: 'Asia/Ho_Chi_Minh',
    startDate: input.startDate,
    bootstrapSystemAdminId,
    credentialPolicy: {
      source: 'DEMO_SEED_ACCOUNT_PASSWORD',
      lifecycle: 'EXISTING_IDENTITY_BCRYPT_COST_12',
    },
    subscriptionPlans,
    operators,
    users,
    subscriptions,
  };
}
