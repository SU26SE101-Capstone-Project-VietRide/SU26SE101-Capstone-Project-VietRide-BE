declare const require: (moduleName: string) => unknown;

const assert = require('node:assert/strict') as {
  deepEqual(actual: unknown, expected: unknown): void;
  equal(actual: unknown, expected: unknown): void;
  match(value: string, regexp: RegExp): void;
  ok(value: unknown): void;
  throws(block: () => unknown, regexp: RegExp): void;
};
const { describe, test } = require('node:test') as {
  describe(name: string, block: () => void): void;
  test(name: string, block: () => void): void;
};
const { createHash } = require('node:crypto') as {
  createHash(algorithm: 'sha256'): {
    update(value: string): { digest(encoding: 'hex'): string };
  };
};

import { day44IdentityFixtureIds } from './seed-identity';
import {
  Day44CommerceFixturePlan,
  Day44CommercePlannerInput,
  ExistingCommerceFixtureState,
  day44CommerceFixtureIds,
  emptyDay44CommerceState,
  planDay44CommerceFixture,
} from './seed-commerce';

const SYSTEM_ADMIN_ID = '11111111-1111-4111-8111-111111111111';

function validInput(): Day44CommercePlannerInput {
  return {
    environment: 'Development',
    startDate: '2026-08-25',
    currentInstant: new Date('2026-08-08T05:00:00.000Z'),
    references: {
      bootstrapSystemAdminId: SYSTEM_ADMIN_ID,
      businessPlanId: '44000000-0000-4000-8000-000000000001',
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
      passengerIds: [...day44IdentityFixtureIds.passengers],
      routeIds: {
        A: day44CommerceFixtureIds.routes.A,
        B: day44CommerceFixtureIds.routes.B,
      },
    },
    existingState: emptyDay44CommerceState(),
  };
}

function persistedState(plan: Day44CommerceFixturePlan): ExistingCommerceFixtureState {
  return {
    wallets: plan.wallets,
    topUpRequests: plan.topUpRequests,
    walletTransactions: plan.walletTransactions,
    payments: plan.payments,
    paymentProcessedEvents: plan.paymentProcessedEvents,
    paymentOutboxEvents: plan.paymentOutboxEvents,
    subscriptionUpgradeAttempts: plan.subscriptionUpgradeAttempts,
    identityInboxEvents: plan.identityInboxEvents,
    invoices: plan.invoices,
    platformWalletTransactions: plan.platformWalletTransactions,
    vouchers: plan.vouchers,
    voucherConsents: plan.voucherConsents,
    parcelRouteFares: plan.parcelRouteFares,
    platformWalletBalance: plan.platformWalletClosingBalance,
  };
}

describe('Day 44 commerce fixture planner', () => {
  test('plans exactly ten funded passenger wallets without manual adjustments', () => {
    const plan = planDay44CommerceFixture(validInput());

    assert.equal(plan.wallets.length, 10);
    assert.equal(plan.topUpRequests.length, 10);
    assert.equal(plan.walletTransactions.length, 10);
    assert.deepEqual(plan.moneyPolicy, {
      storage: 'BIGINT_VND',
      fractionalResultRounding: 'MidpointRounding.AwayFromZero',
    });
    assert.ok(plan.wallets.every((row) => row.balance === 2_000_000 && row.rowVersion === 0));
    assert.ok(
      plan.topUpRequests.every((row) => row.status === 'SUCCEEDED' && row.amount === 2_000_000),
    );
    plan.walletTransactions.forEach((row, index) => {
      assert.deepEqual(
        {
          userId: row.userId,
          type: row.type,
          amount: row.amount,
          balanceBefore: row.balanceBefore,
          balanceAfter: row.balanceAfter,
          referenceType: row.referenceType,
          referenceId: row.referenceId,
        },
        {
          userId: day44IdentityFixtureIds.passengers[index],
          type: 'CREDIT',
          amount: 2_000_000,
          balanceBefore: 0,
          balanceAfter: 2_000_000,
          referenceType: 'TOP_UP',
          referenceId: day44CommerceFixtureIds.topUpRequests[index],
        },
      );
    });
    assert.equal(
      plan.walletTransactions.filter((row) => row.referenceType === 'MANUAL_ADJUSTMENT').length,
      0,
    );
  });

  test('plans the exact voucher consent matrix and two SMALL parcel fares', () => {
    const plan = planDay44CommerceFixture(validInput());

    assert.deepEqual(
      plan.vouchers.map((row) => row.code),
      ['D44RIDE10', 'D44BOOK50', 'D44PARTNER15', 'D44OPA30', 'D44OPBPARCEL20'],
    );
    assert.equal(plan.voucherConsents.length, 2);
    assert.ok(plan.voucherConsents.every((row) => row.status === 'ACCEPTED'));
    assert.ok(
      plan.voucherConsents.every(
        (row) => row.voucherId === day44CommerceFixtureIds.vouchers.D44PARTNER15,
      ),
    );
    const fixedDiscounts = plan.vouchers.filter((row) => row.type === 'FIXED_AMOUNT');
    assert.equal(fixedDiscounts.length, 2);
    assert.ok(fixedDiscounts.every((row) => row.maxDiscountAmount === null));

    const operatorOwned = plan.vouchers.filter((row) => row.ownerOperatorId !== null);
    assert.equal(operatorOwned.length, 2);
    operatorOwned.forEach((row) => {
      assert.equal(row.fundingType, 'OPERATOR_FUNDED');
      assert.deepEqual(row.applicableOperatorIds, [row.ownerOperatorId]);
    });
    assert.ok(
      operatorOwned.every(
        (voucher) => !plan.voucherConsents.some((consent) => consent.voucherId === voucher.id),
      ),
    );

    assert.equal(plan.parcelRouteFares.length, 2);
    assert.ok(
      plan.parcelRouteFares.every(
        (fare) =>
          fare.sizeCategory === 'SMALL' &&
          fare.priceVnd === 50_000 &&
          fare.pricePerChargeableKgVnd === 0 &&
          fare.minimumPriceVnd === 0 &&
          fare.effectiveUntil === null,
      ),
    );
  });

  test('plans two complete paid Business sagas and reconciled immutable credits', () => {
    const plan = planDay44CommerceFixture(validInput());

    assert.equal(plan.payments.length, 2);
    assert.ok(
      plan.payments.every(
        (row) =>
          row.referenceType === 'SUBSCRIPTION' &&
          row.method === 'VNPAY' &&
          row.status === 'SUCCEEDED' &&
          row.amount === 2_000_000,
      ),
    );
    assert.equal(plan.paymentOutboxEvents.length, 2);
    assert.ok(
      plan.paymentOutboxEvents.every(
        (row) =>
          row.eventType === 'payment.subscription.payment_succeeded' &&
          row.status === 'PUBLISHED' &&
          row.id === JSON.parse(String(row.payload)).eventId,
      ),
    );
    assert.equal(plan.paymentProcessedEvents.length, 2);
    assert.equal(plan.identityInboxEvents.length, 2);
    assert.ok(
      plan.paymentProcessedEvents.every((row) => row.consumer === 'payment.subscription-invoice'),
    );
    assert.ok(
      plan.identityInboxEvents.every(
        (row) => row.consumerName === 'identity.subscription-payment-succeeded',
      ),
    );
    plan.paymentOutboxEvents.forEach((outbox) => {
      const publishedPayload = String(outbox.payload);
      const eventId = JSON.parse(publishedPayload).eventId;
      const exactHash = createHash('sha256').update(publishedPayload).digest('hex').toUpperCase();
      assert.match(exactHash, /^[0-9A-F]{64}$/);
      const identityEvidence = plan.identityInboxEvents.find((row) => row.messageId === eventId);
      const paymentEvidence = plan.paymentProcessedEvents.find((row) => row.eventId === eventId);
      assert.equal(outbox.id, eventId);
      assert.equal(identityEvidence?.payloadHash, exactHash);
      assert.equal(paymentEvidence?.payloadHash, exactHash);
    });
    assert.equal(plan.subscriptionUpgradeAttempts.length, 2);
    assert.ok(plan.subscriptionUpgradeAttempts.every((row) => row.status === 'SUCCEEDED'));
    assert.ok(
      plan.identityInboxEvents.every((inbox) =>
        plan.paymentOutboxEvents.some((event) => event.id === inbox.messageId),
      ),
    );
    assert.equal(plan.invoices.length, 2);
    assert.ok(
      plan.invoices.every(
        (row) =>
          row.status === 'ISSUED' &&
          row.pdfGenerationStatus === 'COMPLETED' &&
          row.pdfGenerationAttempts === 1,
      ),
    );
    assert.deepEqual(
      plan.platformWalletTransactions.map((row) => [
        row.type,
        row.referenceType,
        row.amount,
        row.balanceBefore,
        row.balanceAfter,
      ]),
      [
        ['CREDIT', 'SUBSCRIPTION_PAYMENT', 2_000_000, 0, 2_000_000],
        ['CREDIT', 'SUBSCRIPTION_PAYMENT', 2_000_000, 2_000_000, 4_000_000],
      ],
    );
    assert.equal(plan.platformWalletOpeningBalance, 0);
    assert.equal(plan.platformWalletClosingBalance, 4_000_000);
  });

  test('emits no second money or event mutation on an exact rerun', () => {
    const first = planDay44CommerceFixture(validInput());
    const rerunInput = validInput();
    rerunInput.existingState = persistedState(first);
    const rerun = planDay44CommerceFixture(rerunInput);

    assert.ok(Object.values(rerun.inserts).every((rows) => rows.length === 0));
    assert.deepEqual(persistedState(rerun), persistedState(first));
  });

  test('fails closed on state, partial-ledger, and cross-service logical-ID mismatches', () => {
    const expected = planDay44CommerceFixture(validInput());
    const stateMismatch = validInput();
    const persisted = persistedState(expected);
    persisted.walletTransactions = [
      { ...persisted.walletTransactions[0], balanceAfter: 1_999_999 },
    ];
    stateMismatch.existingState = persisted;
    assert.throws(
      () => planDay44CommerceFixture(stateMismatch),
      /foreign natural-key collision or full-state mismatch/,
    );

    const partial = validInput();
    partial.existingState = {
      ...emptyDay44CommerceState(),
      wallets: expected.wallets,
    };
    assert.throws(() => planDay44CommerceFixture(partial), /partial money\/event state/);

    const logicalMismatch = validInput();
    logicalMismatch.references = {
      ...logicalMismatch.references,
      routeIds: { ...logicalMismatch.references.routeIds, A: day44CommerceFixtureIds.routes.B },
    };
    assert.throws(() => planDay44CommerceFixture(logicalMismatch), /logical-ID mismatch/);
  });
});
