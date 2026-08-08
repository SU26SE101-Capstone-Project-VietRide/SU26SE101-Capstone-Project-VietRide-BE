import { day44IdentityFixtureIds } from './seed-identity';

interface NodeHash {
  update(value: string): NodeHash;
  digest(encoding: 'hex'): string;
}

declare const require: (moduleName: string) => unknown;

const { createHash } = require('node:crypto') as {
  createHash(algorithm: 'sha256'): NodeHash;
};

const ICT_OFFSET_MILLISECONDS = 7 * 60 * 60 * 1000;
const BUSINESS_PLAN_ID = '44000000-0000-4000-8000-000000000001';
const PAYMENT_EVENT_TYPE = 'payment.subscription.payment_succeeded';
const SUBSCRIPTION_INVOICE_CONSUMER = 'payment.subscription-invoice';
const IDENTITY_SUBSCRIPTION_CONSUMER = 'identity.subscription-payment-succeeded';
const ROUTE_IDS = {
  A: 'c908c072-337a-526e-bf89-27254cae8e8f',
  B: '67db3832-0894-5afc-94ab-ea73b3dd8671',
} as const;

type OperatorLetter = 'A' | 'B';
type FixtureRow = Readonly<Record<string, unknown>>;

export interface ExistingCommerceFixtureState {
  wallets: ReadonlyArray<FixtureRow>;
  topUpRequests: ReadonlyArray<FixtureRow>;
  walletTransactions: ReadonlyArray<FixtureRow>;
  payments: ReadonlyArray<FixtureRow>;
  paymentProcessedEvents: ReadonlyArray<FixtureRow>;
  paymentOutboxEvents: ReadonlyArray<FixtureRow>;
  subscriptionUpgradeAttempts: ReadonlyArray<FixtureRow>;
  identityInboxEvents: ReadonlyArray<FixtureRow>;
  invoices: ReadonlyArray<FixtureRow>;
  platformWalletTransactions: ReadonlyArray<FixtureRow>;
  vouchers: ReadonlyArray<FixtureRow>;
  voucherConsents: ReadonlyArray<FixtureRow>;
  parcelRouteFares: ReadonlyArray<FixtureRow>;
  platformWalletBalance: number;
}

export interface CommerceLogicalReferences {
  bootstrapSystemAdminId: string;
  businessPlanId: string;
  operatorIds: Readonly<Record<OperatorLetter, string>>;
  operatorAdminIds: Readonly<Record<OperatorLetter, string>>;
  subscriptionIds: Readonly<Record<OperatorLetter, string>>;
  passengerIds: ReadonlyArray<string>;
  routeIds: Readonly<Record<OperatorLetter, string>>;
}

export interface Day44CommercePlannerInput {
  environment: string | undefined;
  startDate: string;
  currentInstant: Date;
  references: CommerceLogicalReferences;
  existingState: ExistingCommerceFixtureState;
  logger?: { info(message: string, fields: Readonly<Record<string, unknown>>): void };
}

export interface Day44CommerceFixturePlan extends ExistingCommerceFixtureState {
  schemaVersion: 1;
  namespace: 'day44-v1';
  timezone: 'Asia/Ho_Chi_Minh';
  startDate: string;
  moneyPolicy: {
    storage: 'BIGINT_VND';
    fractionalResultRounding: 'MidpointRounding.AwayFromZero';
  };
  platformWalletOpeningBalance: 0;
  platformWalletClosingBalance: 4_000_000;
  inserts: Omit<ExistingCommerceFixtureState, 'platformWalletBalance'>;
}

export const day44CommerceFixtureIds = Object.freeze({
  upgradeAttempts: Object.freeze({
    A: '74b73558-f03e-5a68-aaf3-edf1563f61de',
    B: 'a9755051-3e91-5618-be34-b5a9b63180e3',
  }),
  identityInbox: Object.freeze({
    A: '6f1a2f10-d9ca-5d89-8d55-7194dae1364d',
    B: 'ce48381f-919e-5222-a900-b645b00578be',
  }),
  payments: Object.freeze({
    A: '9c10727f-749d-56c2-bbd9-e981b996d699',
    B: 'bac61192-d30c-5029-acf2-167bae06a9f0',
  }),
  paymentProcessedEvents: Object.freeze({
    A: '496209ea-4358-5d81-a91e-33704ed81c77',
    B: '6fcceb19-f24c-5e0e-8bc3-59351df2da68',
  }),
  paymentEvents: Object.freeze({
    A: '3ddf16ca-8deb-5719-83b7-b3683392b782',
    B: 'a213a3e7-d834-5897-a404-9b2c883afd00',
  }),
  invoices: Object.freeze({
    A: '5f61025c-d8e3-5a2e-865d-a992ed3d27d7',
    B: '01c5dcff-bbbe-558a-aaea-52b75b723a2a',
  }),
  platformWalletTransactions: Object.freeze({
    A: 'f43385d5-7142-5f8b-be72-a1b67ec0004f',
    B: '372d57c2-56c8-5de6-b6e2-b18f5ff28edd',
  }),
  topUpRequests: Object.freeze([
    '4d9b721c-6912-557e-9a3d-61facdeb1374',
    'a52f2599-315f-57b5-b8ef-7c5b3c658611',
    '509e1500-83a0-506d-8bf6-b573013dbfd2',
    'fdd195b7-89ec-5f0d-b69b-4be96a106be9',
    '81082ea4-f4cb-5349-867f-5c25eb53aeb5',
    'f5cbb7a0-3268-534c-8fc3-53aa73c821e9',
    '08c9e6a2-7530-50f0-b257-11b8d93629e9',
    '5c588db3-35a4-5d13-85f5-2c4870c4e1fc',
    '027ad379-42bc-5808-b4d6-d2c8add12624',
    '86717bbe-bcd0-5ac9-9c73-47dc1bfc94cb',
  ]),
  walletTransactions: Object.freeze([
    '2a92330c-c88e-538b-9d44-45375a2b9d18',
    'b03eef06-6237-555f-95e6-d1e6ecd932ad',
    '17c64fbc-b0cf-5c58-94a2-421338755ccf',
    'de6ae2dd-77c3-5da7-ad61-99c48c2d51e2',
    'ed0da330-d327-5a71-b73f-4a07dc993d17',
    '305ec29f-1c38-5d9b-8171-4d3f2034f7ca',
    '59c598ed-fe4f-53fe-82b5-50c650e2fbc1',
    '412eb268-5b6f-54eb-b2d6-746c61b4bb76',
    '360f61dc-98e2-5b12-8dbf-f05fa865d133',
    'bd78d2ad-64dd-529e-b9e3-b5c44e6efd0d',
  ]),
  vouchers: Object.freeze({
    D44RIDE10: '8d0fa121-27f3-5239-aa2c-894541991249',
    D44BOOK50: '556d31a1-21ba-534a-8440-b2db3dc77179',
    D44PARTNER15: '84e96b26-d4b1-55d0-8f5a-46750b58ce89',
    D44OPA30: '10671adf-d61c-563e-a49d-669077c57f99',
    D44OPBPARCEL20: 'e96a29bf-f8e9-593d-8d8c-89408533ffe6',
  }),
  voucherConsents: Object.freeze({
    A: '9696626f-c0de-590b-be11-a36160137e17',
    B: '2e3c1e47-9318-59a5-ac29-63847e5a9551',
  }),
  routes: ROUTE_IDS,
});

function ictInstant(startDate: string, dayOffset: number, hour = 0, minute = 0): Date {
  const [year, month, day] = startDate.split('-').map(Number);
  return new Date(
    Date.UTC(year, month - 1, day + dayOffset, hour, minute) - ICT_OFFSET_MILLISECONDS,
  );
}

function addIctMonth(startDate: string): string {
  const [year, month, day] = startDate.split('-').map(Number);
  const targetYear = month === 12 ? year + 1 : year;
  const targetMonthIndex = month === 12 ? 0 : month;
  const lastDay = new Date(Date.UTC(targetYear, targetMonthIndex + 1, 0)).getUTCDate();
  return new Date(
    Date.UTC(targetYear, targetMonthIndex, Math.min(day, lastDay)) - ICT_OFFSET_MILLISECONDS,
  ).toISOString();
}

function canonicalJson(value: unknown): string {
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
}

function serializePublishedPayload(payload: FixtureRow): string {
  return JSON.stringify(payload);
}

function publishedPayloadSha256(payloadJson: string): string {
  return createHash('sha256').update(payloadJson).digest('hex').toUpperCase();
}

function assertInputs(input: Day44CommercePlannerInput): void {
  if (input.environment?.toLowerCase() === 'production')
    throw new Error('Day 44 commerce fixture is forbidden in Production');
  if (!/^\d{4}-\d{2}-\d{2}$/.test(input.startDate))
    throw new Error('Day 44 startDate must use YYYY-MM-DD');
  const start = ictInstant(input.startDate, 0);
  if (Number.isNaN(start.getTime())) throw new Error('Day 44 startDate is invalid');
  const nowIct = new Date(input.currentInstant.getTime() + ICT_OFFSET_MILLISECONDS);
  const todayIct = Date.UTC(nowIct.getUTCFullYear(), nowIct.getUTCMonth(), nowIct.getUTCDate());
  const startIct = start.getTime() + ICT_OFFSET_MILLISECONDS;
  if (startIct < todayIct + 24 * 60 * 60 * 1000)
    throw new Error('Day 44 startDate must be at least one ICT day in the future');
}

function assertReferences(references: CommerceLogicalReferences): void {
  const expected = {
    businessPlanId: BUSINESS_PLAN_ID,
    operatorIds: { A: day44IdentityFixtureIds.operators.A, B: day44IdentityFixtureIds.operators.B },
    operatorAdminIds: {
      A: day44IdentityFixtureIds.operatorAdmins.A,
      B: day44IdentityFixtureIds.operatorAdmins.B,
    },
    subscriptionIds: {
      A: day44IdentityFixtureIds.subscriptions.A,
      B: day44IdentityFixtureIds.subscriptions.B,
    },
    passengerIds: [...day44IdentityFixtureIds.passengers],
    routeIds: ROUTE_IDS,
  };
  if (
    !references.bootstrapSystemAdminId ||
    !/^[0-9a-f-]{36}$/i.test(references.bootstrapSystemAdminId)
  )
    throw new Error('Day 44 commerce fixture requires the bootstrap System Admin ID');
  const actual = { ...references } as Record<string, unknown>;
  delete actual.bootstrapSystemAdminId;
  if (canonicalJson(actual) !== canonicalJson(expected))
    throw new Error('Day 44 commerce cross-service logical-ID mismatch');
}

function buyerSnapshot(letter: OperatorLetter): FixtureRow {
  const index = letter === 'A' ? 1 : 2;
  return {
    name: `Day44 Business Operator ${letter}`,
    businessRegistrationNumber: `D44-BRN-${letter}`,
    taxCode: `D44-TAX-${letter}`,
    contactEmail: `operator.${letter.toLowerCase()}@demo.vietride.local`,
    contactPhone: `+8490444000${index}`,
    addressStreet: '44 Demo Street',
    addressWard: 'Demo Ward',
    addressDistrict: 'Demo District',
    addressProvince: 'Hồ Chí Minh',
  };
}

function buildDesired(input: Day44CommercePlannerInput): ExistingCommerceFixtureState {
  const t0 = ictInstant(input.startDate, 0).toISOString();
  const paidAt = ictInstant(input.startDate, -1, 10).toISOString();
  const attemptCreatedAt = ictInstant(input.startDate, -1, 9, 55).toISOString();
  const dueAt = ictInstant(input.startDate, -1, 10, 10).toISOString();
  const topUpAt = ictInstant(input.startDate, -1, 9).toISOString();
  const invoicePdfCompletedAt = ictInstant(input.startDate, -1, 10, 1).toISOString();
  const periodTo = addIctMonth(input.startDate);
  const validFrom = ictInstant(input.startDate, -7).toISOString();
  const validUntil = ictInstant(input.startDate, 60, 23, 59);
  validUntil.setUTCSeconds(validUntil.getUTCSeconds() + 59);
  const requestedAt = ictInstant(input.startDate, -2).toISOString();
  const respondedAt = ictInstant(input.startDate, -2, 1).toISOString();

  const wallets = input.references.passengerIds.map((userId) => ({
    userId,
    balance: 2_000_000,
    currency: 'VND',
    rowVersion: 0,
    createdAt: topUpAt,
    updatedAt: topUpAt,
  }));
  const topUpRequests = input.references.passengerIds.map((userId, index) => ({
    id: day44CommerceFixtureIds.topUpRequests[index],
    userId,
    amount: 2_000_000,
    status: 'SUCCEEDED',
    vnpayTxnRef: `D44-TOPUP-${String(index + 1).padStart(2, '0')}`,
    vnpayResponseCode: '00',
    paymentRedirectUrl: null,
    succeededAt: topUpAt,
    expiredAt: null,
    createdAt: topUpAt,
    updatedAt: topUpAt,
  }));
  const walletTransactions = input.references.passengerIds.map((userId, index) => ({
    id: day44CommerceFixtureIds.walletTransactions[index],
    userId,
    type: 'CREDIT',
    amount: 2_000_000,
    balanceBefore: 0,
    balanceAfter: 2_000_000,
    referenceType: 'TOP_UP',
    referenceId: day44CommerceFixtureIds.topUpRequests[index],
    note: null,
    createdAt: topUpAt,
  }));

  const payments: FixtureRow[] = [];
  const paymentProcessedEvents: FixtureRow[] = [];
  const paymentOutboxEvents: FixtureRow[] = [];
  const subscriptionUpgradeAttempts: FixtureRow[] = [];
  const identityInboxEvents: FixtureRow[] = [];
  const invoices: FixtureRow[] = [];
  const platformWalletTransactions: FixtureRow[] = [];
  (['A', 'B'] as const).forEach((letter, index) => {
    const operatorId = input.references.operatorIds[letter];
    const subscriptionId = input.references.subscriptionIds[letter];
    const paymentId = day44CommerceFixtureIds.payments[letter];
    const eventId = day44CommerceFixtureIds.paymentEvents[letter];
    const snapshot = buyerSnapshot(letter);
    const context = {
      version: 1,
      operatorSubscriptionId: subscriptionId,
      planId: BUSINESS_PLAN_ID,
      planName: 'Business (Demo)',
      billingPeriod: 'MONTHLY',
      periodFrom: t0,
      periodTo,
      buyerSnapshot: snapshot,
    };
    const payload = {
      eventId,
      occurredAt: paidAt,
      paymentId,
      upgradeAttemptId: day44CommerceFixtureIds.upgradeAttempts[letter],
      operatorId,
      operatorSubscriptionId: subscriptionId,
      planId: BUSINESS_PLAN_ID,
      amount: 2_000_000,
      method: 'VNPAY',
      planName: 'Business (Demo)',
      billingPeriod: 'MONTHLY',
      periodFrom: t0,
      periodTo,
      succeededAt: paidAt,
      buyerSnapshot: snapshot,
    };
    const publishedPayload = serializePublishedPayload(payload);
    const payloadHash = publishedPayloadSha256(publishedPayload);
    payments.push({
      id: paymentId,
      referenceType: 'SUBSCRIPTION',
      referenceId: subscriptionId,
      userId: null,
      operatorId,
      amount: 2_000_000,
      method: 'VNPAY',
      status: 'SUCCEEDED',
      vnpayTxnRef: `D44-SUB-${letter}`,
      vnpayResponseCode: '00',
      idempotencyKey: `day44-v1:subscription:${letter.toLowerCase()}`,
      paymentRedirectUrl: null,
      dueAt,
      succeededAt: paidAt,
      failedAt: null,
      expiredAt: null,
      refundedAt: null,
      context,
      contextReconciliationRequired: false,
      createdAt: attemptCreatedAt,
      updatedAt: paidAt,
    });
    subscriptionUpgradeAttempts.push({
      id: day44CommerceFixtureIds.upgradeAttempts[letter],
      subscriptionId,
      operatorId,
      targetPlanId: BUSINESS_PLAN_ID,
      billingPeriod: 'MONTHLY',
      amount: 2_000_000,
      status: 'SUCCEEDED',
      latestPaymentId: paymentId,
      latestPaymentStatus: 'SUCCEEDED',
      paymentSessionVersion: 1,
      fallbackPolicy: 'RESTORE_CURRENT',
      idempotencyKey: `day44-v1:subscription:${letter.toLowerCase()}`,
      dueAt,
      createdAt: attemptCreatedAt,
      updatedAt: paidAt,
    });
    identityInboxEvents.push({
      id: day44CommerceFixtureIds.identityInbox[letter],
      consumerName: IDENTITY_SUBSCRIPTION_CONSUMER,
      messageId: eventId,
      payloadHash,
      processedAt: paidAt,
    });
    paymentProcessedEvents.push({
      id: day44CommerceFixtureIds.paymentProcessedEvents[letter],
      consumer: SUBSCRIPTION_INVOICE_CONSUMER,
      eventId,
      payloadHash,
      processedAt: paidAt,
      createdAt: paidAt,
    });
    paymentOutboxEvents.push({
      id: eventId,
      eventType: PAYMENT_EVENT_TYPE,
      payload: publishedPayload,
      status: 'PUBLISHED',
      retryCount: 0,
      lastError: null,
      createdAt: paidAt,
      publishedAt: paidAt,
    });
    const invoiceId = day44CommerceFixtureIds.invoices[letter];
    invoices.push({
      id: invoiceId,
      invoiceNumber: `VR-INV-${input.startDate.slice(0, 7).replace('-', '')}-44000${index + 1}`,
      operatorId,
      operatorSubscriptionId: subscriptionId,
      paymentId,
      amount: 2_000_000,
      periodFrom: t0,
      periodTo,
      status: 'ISSUED',
      issuedAt: paidAt,
      pdfUrl: `/v1/operator/invoices/${invoiceId}/download`,
      storageObjectPath: `invoices/${operatorId}/${invoiceId}.pdf`,
      pdfGenerationStatus: 'COMPLETED',
      pdfGenerationAttempts: 1,
      pdfGenerationStartedAt: paidAt,
      pdfGenerationNextRetryAt: null,
      pdfGenerationLastError: null,
      metadata: {
        version: 1,
        planName: 'Business (Demo)',
        billingPeriod: 'MONTHLY',
        buyerSnapshot: snapshot,
        subtotal: 2_000_000,
        discount: 0,
        tax: 0,
        total: 2_000_000,
        pdfCompletedAt: invoicePdfCompletedAt,
      },
      createdAt: paidAt,
      updatedAt: invoicePdfCompletedAt,
    });
    platformWalletTransactions.push({
      id: day44CommerceFixtureIds.platformWalletTransactions[letter],
      type: 'CREDIT',
      amount: 2_000_000,
      balanceBefore: index * 2_000_000,
      balanceAfter: (index + 1) * 2_000_000,
      referenceType: 'SUBSCRIPTION_PAYMENT',
      referenceId: paymentId,
      note: null,
      actorType: 'SYSTEM',
      actorUserId: null,
      actorDisplayName: null,
      actorEmail: null,
      actorRole: null,
      actorSnapshotResolved: true,
      createdAt: paidAt,
    });
  });

  const voucherBase = {
    totalUsageLimit: 10_000,
    perUserLimit: 100,
    validFrom,
    validUntil: validUntil.toISOString(),
    newUserOnly: false,
    isActive: true,
    deletedAt: null,
    createdAt: t0,
    updatedAt: t0,
  };
  const vouchers: FixtureRow[] = [
    {
      id: day44CommerceFixtureIds.vouchers.D44RIDE10,
      code: 'D44RIDE10',
      name: 'Day44 Ride 10',
      type: 'PERCENT_OFF',
      value: 10,
      minOrderAmount: 100_000,
      maxDiscountAmount: 50_000,
      applicablePaymentMethods: ['WALLET', 'VNPAY'],
      applicableServices: ['BOOKING', 'PARCEL'],
      applicableOperatorIds: null,
      applicableRouteIds: null,
      fundingType: 'VIETRIDE_FUNDED',
      ownerOperatorId: null,
      createdByUserId: input.references.bootstrapSystemAdminId,
      ...voucherBase,
    },
    {
      id: day44CommerceFixtureIds.vouchers.D44BOOK50,
      code: 'D44BOOK50',
      name: 'Day44 Booking 50K',
      type: 'FIXED_AMOUNT',
      value: 50_000,
      minOrderAmount: 200_000,
      maxDiscountAmount: null,
      applicablePaymentMethods: ['WALLET'],
      applicableServices: ['BOOKING'],
      applicableOperatorIds: [input.references.operatorIds.A],
      applicableRouteIds: [input.references.routeIds.A],
      fundingType: 'VIETRIDE_FUNDED',
      ownerOperatorId: null,
      createdByUserId: input.references.bootstrapSystemAdminId,
      ...voucherBase,
    },
    {
      id: day44CommerceFixtureIds.vouchers.D44PARTNER15,
      code: 'D44PARTNER15',
      name: 'Day44 Partner 15',
      type: 'PERCENT_OFF',
      value: 15,
      minOrderAmount: 100_000,
      maxDiscountAmount: 75_000,
      applicablePaymentMethods: ['WALLET', 'VNPAY'],
      applicableServices: ['BOOKING', 'PARCEL'],
      applicableOperatorIds: [input.references.operatorIds.A, input.references.operatorIds.B],
      applicableRouteIds: [input.references.routeIds.A, input.references.routeIds.B],
      fundingType: 'OPERATOR_FUNDED',
      ownerOperatorId: null,
      createdByUserId: input.references.bootstrapSystemAdminId,
      ...voucherBase,
    },
    {
      id: day44CommerceFixtureIds.vouchers.D44OPA30,
      code: 'D44OPA30',
      name: 'Day44 Operator A 30K',
      type: 'FIXED_AMOUNT',
      value: 30_000,
      minOrderAmount: 150_000,
      maxDiscountAmount: null,
      applicablePaymentMethods: ['WALLET'],
      applicableServices: ['BOOKING'],
      applicableOperatorIds: [input.references.operatorIds.A],
      applicableRouteIds: [input.references.routeIds.A],
      fundingType: 'OPERATOR_FUNDED',
      ownerOperatorId: input.references.operatorIds.A,
      createdByUserId: input.references.operatorAdminIds.A,
      ...voucherBase,
    },
    {
      id: day44CommerceFixtureIds.vouchers.D44OPBPARCEL20,
      code: 'D44OPBPARCEL20',
      name: 'Day44 Operator B Parcel 20',
      type: 'PERCENT_OFF',
      value: 20,
      minOrderAmount: 100_000,
      maxDiscountAmount: 100_000,
      applicablePaymentMethods: ['WALLET', 'VNPAY'],
      applicableServices: ['PARCEL'],
      applicableOperatorIds: [input.references.operatorIds.B],
      applicableRouteIds: [input.references.routeIds.B],
      fundingType: 'OPERATOR_FUNDED',
      ownerOperatorId: input.references.operatorIds.B,
      createdByUserId: input.references.operatorAdminIds.B,
      ...voucherBase,
    },
  ];
  const voucherConsents = (['A', 'B'] as const).map((letter) => ({
    id: day44CommerceFixtureIds.voucherConsents[letter],
    operatorId: input.references.operatorIds[letter],
    voucherId: day44CommerceFixtureIds.vouchers.D44PARTNER15,
    status: 'ACCEPTED',
    requestedAt,
    respondedAt,
    respondedByUserId: input.references.operatorAdminIds[letter],
    rejectReason: null,
    createdAt: requestedAt,
    updatedAt: respondedAt,
  }));
  const parcelRouteFares = (['A', 'B'] as const).map((letter) => ({
    routeId: input.references.routeIds[letter],
    sizeCategory: 'SMALL',
    operatorId: input.references.operatorIds[letter],
    priceVnd: 50_000,
    pricePerChargeableKgVnd: 0,
    minimumPriceVnd: 0,
    effectiveFrom: t0,
    effectiveUntil: null,
    createdAt: t0,
    updatedAt: t0,
  }));

  return {
    wallets,
    topUpRequests,
    walletTransactions,
    payments,
    paymentProcessedEvents,
    paymentOutboxEvents,
    subscriptionUpgradeAttempts,
    identityInboxEvents,
    invoices,
    platformWalletTransactions,
    vouchers,
    voucherConsents,
    parcelRouteFares,
    platformWalletBalance: 4_000_000,
  };
}

function assertExistingAndPlanInserts(
  existing: ExistingCommerceFixtureState,
  desired: ExistingCommerceFixtureState,
): Omit<ExistingCommerceFixtureState, 'platformWalletBalance'> {
  const collections = Object.keys(desired).filter(
    (key) => key !== 'platformWalletBalance',
  ) as Array<keyof Omit<ExistingCommerceFixtureState, 'platformWalletBalance'>>;
  const inserts = {} as Record<string, ReadonlyArray<FixtureRow>>;
  collections.forEach((key) => {
    const expectedStates = new Set(desired[key].map(canonicalJson));
    existing[key].forEach((row) => {
      if (!expectedStates.has(canonicalJson(row)))
        throw new Error(
          `Day 44 commerce ${key} foreign natural-key collision or full-state mismatch`,
        );
    });
    const existingStates = new Set(existing[key].map(canonicalJson));
    inserts[key] = desired[key].filter((row) => !existingStates.has(canonicalJson(row)));
  });

  const moneyAndEventKeys = [
    'wallets',
    'topUpRequests',
    'walletTransactions',
    'payments',
    'paymentProcessedEvents',
    'paymentOutboxEvents',
    'subscriptionUpgradeAttempts',
    'identityInboxEvents',
    'invoices',
    'platformWalletTransactions',
  ] as const;
  const existingFinancialRows = moneyAndEventKeys.reduce(
    (sum, key) => sum + existing[key].length,
    0,
  );
  const expectedFinancialRows = moneyAndEventKeys.reduce(
    (sum, key) => sum + desired[key].length,
    0,
  );
  if (existingFinancialRows !== 0 && existingFinancialRows !== expectedFinancialRows)
    throw new Error('Day 44 commerce partial money/event state is not adoptable');
  const expectedBalance = existingFinancialRows === 0 ? 0 : desired.platformWalletBalance;
  if (existing.platformWalletBalance !== expectedBalance)
    throw new Error('Day 44 commerce platform wallet reconciliation mismatch');
  return inserts as Omit<ExistingCommerceFixtureState, 'platformWalletBalance'>;
}

export function emptyDay44CommerceState(platformWalletBalance = 0): ExistingCommerceFixtureState {
  return {
    wallets: [],
    topUpRequests: [],
    walletTransactions: [],
    payments: [],
    paymentProcessedEvents: [],
    paymentOutboxEvents: [],
    subscriptionUpgradeAttempts: [],
    identityInboxEvents: [],
    invoices: [],
    platformWalletTransactions: [],
    vouchers: [],
    voucherConsents: [],
    parcelRouteFares: [],
    platformWalletBalance,
  };
}

export function planDay44CommerceFixture(
  input: Day44CommercePlannerInput,
): Day44CommerceFixturePlan {
  assertInputs(input);
  assertReferences(input.references);
  const desired = buildDesired(input);
  const inserts = assertExistingAndPlanInserts(input.existingState, desired);
  input.logger?.info('Day 44 commerce fixture plan validated', {
    wallets: desired.wallets.length,
    subscriptionPayments: desired.payments.length,
    vouchers: desired.vouchers.length,
    parcelRouteFares: desired.parcelRouteFares.length,
    plannedInserts: Object.values(inserts).reduce((sum, rows) => sum + rows.length, 0),
  });
  return {
    schemaVersion: 1,
    namespace: 'day44-v1',
    timezone: 'Asia/Ho_Chi_Minh',
    startDate: input.startDate,
    moneyPolicy: {
      storage: 'BIGINT_VND',
      fractionalResultRounding: 'MidpointRounding.AwayFromZero',
    },
    ...desired,
    platformWalletOpeningBalance: 0,
    platformWalletClosingBalance: 4_000_000,
    inserts,
  };
}
