declare const require: (moduleName: string) => unknown;

const assert = require('node:assert/strict') as {
  deepEqual(actual: unknown, expected: unknown): void;
  doesNotMatch(value: string, regexp: RegExp): void;
  equal(actual: unknown, expected: unknown): void;
  ok(value: unknown): void;
  throws(block: () => unknown, regexp: RegExp): void;
};
const { describe, test } = require('node:test') as {
  describe(name: string, block: () => void): void;
  test(name: string, block: () => void): void;
};
import {
  Day44IdentityPlannerInput,
  ExistingIdentityFixtureState,
  IdentitySubscriptionPlanFixture,
  planDay44IdentityFixture,
} from './seed-identity';

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

const bootstrapSystemAdmin = {
  id: '40000000-0000-4000-8000-000000000001',
  role: 'SYSTEM_ADMIN' as const,
  status: 'ACTIVE' as const,
  deletedAt: null,
};

const operatorIds = {
  A: '6276b48c-3984-582b-9c35-0c2fbe20baa7',
  B: 'd63b3c32-8c12-5130-a347-0ef8df286605',
  C: '8554beea-8b1b-57c5-bb87-8d1f136654a3',
} as const;
const operatorAdminIds = {
  A: '9c90f052-9323-5c47-9402-ad100db3dec9',
  B: '65cfe24b-a43e-5dad-b43d-c6bf1b3cd914',
  C: 'e21cf2e5-c8fc-5155-a8bb-345a4e6f3f8b',
} as const;
const driverIds = {
  A: [
    '6a61b1d5-4c98-5f40-8e0f-494651deebfa',
    '1432b243-ab2b-5a33-8db5-5441efd4d489',
    '67086aa7-71f3-5f60-9d13-f7f30bb8c7c8',
  ],
  B: [
    'ea9c2b90-c811-5281-9793-4722253b5b17',
    'aeebce20-d2d9-525c-9394-8c43c6cf8800',
    'f55eadcb-f314-5e35-898a-6d5ddad291aa',
  ],
  C: [
    '6e236fff-7856-51c4-917c-89c6724b7d60',
    'a052ed42-ef29-5180-b92e-317b01b92b65',
    '04ebbfdc-c20c-5f1c-b145-030eb9e247d4',
  ],
} as const;
const assistantIds = {
  A: '316ba0dc-6bea-5173-858d-4c9c3cde50de',
  B: '2b7ae533-41e1-5fb6-9875-76e8923c4916',
  C: 'f0931d74-4698-59a6-8eb6-de775b44e6fe',
} as const;
const passengerIds = [
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
] as const;
const subscriptionIds = {
  A: '9b7f508d-7215-5228-af11-f3d29ff5e14b',
  B: 'fe24eec8-2cbd-523b-8710-5e4276541ab0',
  C: '5d5879bb-7e22-5bc2-97e4-bbf923dd4739',
} as const;
const letters = ['A', 'B', 'C'] as const;

function departures(startDay: number): string[] {
  const values: string[] = [];
  for (let day = 0; day < 14; day += 1) {
    values.push(
      new Date(Date.UTC(2026, 7, startDay + day, 1)).toISOString(),
      new Date(Date.UTC(2026, 7, startDay + day, 3)).toISOString(),
      new Date(Date.UTC(2026, 7, startDay + day, 7)).toISOString(),
    );
  }
  return values;
}

function expectedUser(
  id: string,
  email: string,
  phone: string,
  displayName: string,
  role: 'OPERATOR_ADMIN' | 'DRIVER' | 'ASSISTANT' | 'PASSENGER',
  operatorId: string | null,
): Record<string, unknown> {
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
    createdAt: '2026-08-07T17:00:00.000Z',
    updatedAt: '2026-08-07T17:00:00.000Z',
  };
}

function expectedUsers(): ReadonlyArray<Record<string, unknown>> {
  const users: Array<Record<string, unknown>> = [];
  letters.forEach((letter, operatorIndex) => {
    const lower = letter.toLowerCase();
    users.push(
      expectedUser(
        operatorAdminIds[letter],
        `operator.${lower}@demo.vietride.local`,
        `+8490444010${operatorIndex + 1}`,
        `Day44 Operator ${letter} Admin`,
        'OPERATOR_ADMIN',
        operatorIds[letter],
      ),
    );
    driverIds[letter].forEach((id, driverIndex) =>
      users.push(
        expectedUser(
          id,
          `driver.${lower}${driverIndex + 1}@demo.vietride.local`,
          `+849044410${operatorIndex + 1}${driverIndex + 1}`,
          `Day44 Driver ${letter}${driverIndex + 1}`,
          'DRIVER',
          operatorIds[letter],
        ),
      ),
    );
    users.push(
      expectedUser(
        assistantIds[letter],
        `assistant.${lower}@demo.vietride.local`,
        `+8490444020${operatorIndex + 1}`,
        `Day44 Assistant ${letter}`,
        'ASSISTANT',
        operatorIds[letter],
      ),
    );
  });
  passengerIds.forEach((id, index) => {
    const suffix = String(index + 1).padStart(2, '0');
    users.push(
      expectedUser(
        id,
        `passenger${suffix}@demo.vietride.local`,
        `+849044403${suffix}`,
        `Day44 Passenger ${suffix}`,
        'PASSENGER',
        null,
      ),
    );
  });
  return users;
}

function expectedOperators(): ReadonlyArray<Record<string, unknown>> {
  return letters.map((letter, index) => {
    const phone = `+8490444000${index + 1}`;
    return {
      id: operatorIds[letter],
      name: `Day44 ${letter === 'C' ? 'Starter' : 'Business'} Operator ${letter}`,
      businessRegistrationNumber: `D44-BRN-${letter}`,
      taxCode: `D44-TAX-${letter}`,
      contactEmail: `operator.${letter.toLowerCase()}@demo.vietride.local`,
      contactPhone: phone,
      logoUrl: null,
      addressStreet: '44 Demo Street',
      addressWard: 'Demo Ward',
      addressDistrict: 'Demo District',
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
      createdAt: '2026-08-07T17:00:00.000Z',
      updatedAt: '2026-08-07T17:00:00.000Z',
    };
  });
}

function expectedSubscriptions(): ReadonlyArray<Record<string, unknown>> {
  return letters.map((letter) => ({
    id: subscriptionIds[letter],
    operatorId: operatorIds[letter],
    activePlanId:
      letter === 'C'
        ? '00000000-0000-0000-0000-000000000001'
        : '44000000-0000-4000-8000-000000000001',
    status: 'ACTIVE',
    startedAt: '2026-08-09T17:00:00.000Z',
    expiresAt: letter === 'C' ? '2026-09-08T17:00:00.000Z' : '2026-09-09T17:00:00.000Z',
    paymentMethod: letter === 'C' ? null : 'VNPAY',
    billingPeriod: letter === 'C' ? null : 'MONTHLY',
    currentVehicles: 3,
    currentDrivers: 3,
    currentAssistants: 1,
    currentOperatorUsers: 1,
    currentRoutes: 3,
    currentTripsThisMonth: 42,
    lastResetAt: '2026-07-31T17:00:00.000Z',
    trialExpiringWarnSentAt: null,
    createdAt: '2026-08-07T17:00:00.000Z',
    updatedAt: '2026-08-07T17:00:00.000Z',
  }));
}

function emptyExistingState(): ExistingIdentityFixtureState {
  return {
    bootstrapSystemAdmins: [bootstrapSystemAdmin],
    subscriptionPlans: [starterPlan],
    operators: [],
    users: [],
    subscriptions: [],
  };
}

function validInput(): Day44IdentityPlannerInput {
  return {
    environment: 'Development',
    accountPassword: 'runtime-only-secret-value',
    startDate: '2026-08-10',
    currentInstant: new Date('2026-08-08T12:00:00.000Z'),
    tripDepartureInstantsByOperator: {
      A: departures(10),
      B: departures(10),
      C: departures(10),
    },
    existingState: emptyExistingState(),
  };
}

describe('Day 44 identity fixture planner', () => {
  test('plans the exact manifest roles, tenants, plans, subscriptions, and Asia/Ho_Chi_Minh counters', () => {
    const plan = planDay44IdentityFixture(validInput());

    assert.deepEqual(plan, {
      schemaVersion: 1,
      namespace: 'day44-v1',
      timezone: 'Asia/Ho_Chi_Minh',
      startDate: '2026-08-10',
      bootstrapSystemAdminId: bootstrapSystemAdmin.id,
      credentialPolicy: {
        source: 'DEMO_SEED_ACCOUNT_PASSWORD',
        lifecycle: 'EXISTING_IDENTITY_BCRYPT_COST_12',
      },
      subscriptionPlans: [
        starterPlan,
        {
          id: '44000000-0000-4000-8000-000000000001',
          name: 'Business (Demo)',
          description:
            'Day 44 demo-only Business plan; commercial pricing is non-canonical and must not be used as production policy.',
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
          createdAt: '2026-08-07T17:00:00.000Z',
          updatedAt: '2026-08-07T17:00:00.000Z',
        },
      ],
      operators: expectedOperators(),
      users: expectedUsers(),
      subscriptions: expectedSubscriptions(),
    });
  });

  test('calculates the Asia/Ho_Chi_Minh-month Trip counter across a month boundary', () => {
    const input = validInput();
    input.startDate = '2026-08-25';
    input.tripDepartureInstantsByOperator = {
      A: departures(25),
      B: departures(25),
      C: departures(25),
    };

    const plan = planDay44IdentityFixture(input);

    assert.equal(input.tripDepartureInstantsByOperator.A.length, 42);
    assert.deepEqual(
      plan.subscriptions.map(({ currentTripsThisMonth }) => currentTripsThisMonth),
      [21, 21, 21],
    );
  });

  test('accepts an exact rerun state and rejects any full-state mismatch', () => {
    const first = planDay44IdentityFixture(validInput());
    const rerun = validInput();
    rerun.existingState = {
      bootstrapSystemAdmins: [bootstrapSystemAdmin],
      subscriptionPlans: first.subscriptionPlans,
      operators: first.operators,
      users: first.users,
      subscriptions: first.subscriptions,
    };
    assert.deepEqual(planDay44IdentityFixture(rerun), first);

    const mismatch = validInput();
    mismatch.existingState = {
      ...emptyExistingState(),
      operators: [{ ...first.operators[0], taxCode: 'FOREIGN-TAX' }],
    };
    assert.throws(() => planDay44IdentityFixture(mismatch), /full-state mismatch/);
  });

  test('rejects Production, missing credentials, non-future dates, and invalid Trip inputs', () => {
    const production = validInput();
    production.environment = 'Production';
    assert.throws(() => planDay44IdentityFixture(production), /forbidden in Production/);

    const missingPassword = validInput();
    missingPassword.accountPassword = '  ';
    assert.throws(() => planDay44IdentityFixture(missingPassword), /DEMO_SEED_ACCOUNT_PASSWORD/);

    const today = validInput();
    today.startDate = '2026-08-08';
    assert.throws(() => planDay44IdentityFixture(today), /at least one day/);

    const incompleteTrips = validInput();
    incompleteTrips.tripDepartureInstantsByOperator = {
      ...incompleteTrips.tripDepartureInstantsByOperator,
      A: departures(10).slice(0, 41),
    };
    assert.throws(() => planDay44IdentityFixture(incompleteTrips), /exactly 42/);
  });

  test('rejects random IDs and foreign natural-key collisions', () => {
    const expected = planDay44IdentityFixture(validInput());
    const collision = validInput();
    collision.existingState = {
      ...emptyExistingState(),
      users: [
        {
          ...expected.users[0],
          id: '40000000-0000-4000-8000-000000000099',
        },
      ],
    };
    assert.throws(() => planDay44IdentityFixture(collision), /foreign natural-key collision/);
  });

  test('never returns or logs credential material', () => {
    const input = validInput();
    const logs: unknown[] = [];
    input.logger = {
      info(message, fields): void {
        logs.push({ message, fields });
      },
    };
    const plan = planDay44IdentityFixture(input);
    const serialized = JSON.stringify({ plan, logs });

    assert.doesNotMatch(serialized, /runtime-only-secret-value/);
    assert.doesNotMatch(serialized, /passwordHash|refreshToken|otp|token/i);
    assert.equal(plan.credentialPolicy.lifecycle, 'EXISTING_IDENTITY_BCRYPT_COST_12');
  });
});
