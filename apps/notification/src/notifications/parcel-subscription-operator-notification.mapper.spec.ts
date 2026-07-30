/* eslint-disable @typescript-eslint/no-unsafe-assignment -- Jest asymmetric matchers expose any by design. */
import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
  BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
  BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY,
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_UNLOADED_ROUTING_KEY,
  PARCEL_AUTO_REJECTED_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY,
  PARCEL_DELIVERY_REJECTED_ROUTING_KEY,
  PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY,
  PARCEL_REVIEW_APPROVED_ROUTING_KEY,
  PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
  PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY,
  PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY,
  PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY,
  PAYOUT_FAILED_ROUTING_KEY,
  SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY,
  SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY,
  SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
  SUBSCRIPTION_EXPIRED_ROUTING_KEY,
  SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY,
  TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
  TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
} from './parcel-subscription-operator-events.constants';
import { mapParcelSubscriptionOperatorEventToNotifications } from './parcel-subscription-operator-notification.mapper';
import type { ParcelRecipientSnapshot } from './parcel-recipient.provider';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const PARCEL_ID = '44444444-4444-4444-8444-444444444444';
const TRIP_ID = '55555555-5555-4555-8555-555555555555';
const SETTLEMENT_ID = '66666666-6666-4666-8666-666666666666';
const INVOICE_ID = '77777777-7777-4777-8777-777777777777';
const PAYOUT_ID = '88888888-8888-4888-8888-888888888888';
const VOUCHER_ID = '99999999-9999-4999-8999-999999999999';
const PLAN_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('mapParcelSubscriptionOperatorEventToNotifications', () => {
  it('uses the canonical Identity subscription lifecycle routing keys', () => {
    expect(SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY).toBe(
      'identity.subscription.trial_expiring',
    );
    expect(SUBSCRIPTION_EXPIRED_ROUTING_KEY).toBe('identity.subscription.expired');
    expect(SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY).toBe(
      'identity.subscription.payment_auto_reverted',
    );
  });

  it('maps Sprint 4 Parcel facts to tenant-scoped notifications', async () => {
    const loaded = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_LOADED_ROUTING_KEY,
      {
        eventId: '88888888-8888-4888-8888-888888888888',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        tripId: TRIP_ID,
        actualWeightKg: 12.5,
        userIds: [USER_ID, SECOND_USER_ID],
      },
      resolveNoOperatorRecipients,
    );
    const unloaded = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_UNLOADED_ROUTING_KEY,
      { parcelId: PARCEL_ID, tripId: TRIP_ID, userIds: [USER_ID] },
      resolveNoOperatorRecipients,
    );
    const rejected = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_AUTO_REJECTED_ROUTING_KEY,
      {
        eventId: '99999999-9999-4999-8999-999999999999',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        tripId: TRIP_ID,
        refundAmount: 100,
      },
      resolveNoOperatorRecipients,
    );
    expect(loaded).toHaveLength(2);
    expect(unloaded[0]?.type).toBe(NotificationType.PARCEL_IN_TRANSIT);
    expect(rejected[0]?.type).toBe(NotificationType.PARCEL_REJECTED);
  });

  it('maps canonical delivered-pending-confirm only to explicit recipient accounts', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
        {
          eventId: '88888888-8888-4888-8888-888888888888',
          occurredAt: '2026-07-30T03:00:00Z',
          parcelId: PARCEL_ID,
          parcelCode: 'VR-PCL-20260730-ABCDEFGH',
          operatorId: OPERATOR_ID,
          tripId: TRIP_ID,
          userId: USER_ID,
          recipientUserIds: [USER_ID, SECOND_USER_ID],
          expiresAt: '2026-08-01T03:00:00Z',
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
      }),
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
      }),
    ]);
  });

  it.each(['deliveryToken', 'deliveryUrl'])(
    'rejects forbidden delivered-pending-confirm field %s before mapping',
    async (field) => {
      await expect(
        mapParcelSubscriptionOperatorEventToNotifications(
          PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
          {
            eventId: '88888888-8888-4888-8888-888888888888',
            occurredAt: '2026-07-30T03:00:00Z',
            parcelId: PARCEL_ID,
            parcelCode: 'VR-PCL-20260730-ABCDEFGH',
            operatorId: OPERATOR_ID,
            tripId: TRIP_ID,
            userId: USER_ID,
            [field]: 'forbidden-secret',
          },
          resolveNoOperatorRecipients,
        ),
      ).rejects.toThrow(ZodError);
    },
  );

  it('maps delivery-confirmation re-alerts to operator admins with an existing type', async () => {
    const resolveOperatorRecipients = jest.fn(async () => [SECOND_USER_ID]);

    const notifications = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY,
      {
        eventId: '88888888-8888-4888-8888-888888888888',
        occurredAt: '2026-07-30T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'VR-PCL-20260730-ABCDEFGH',
        operatorId: OPERATOR_ID,
        tripId: TRIP_ID,
        expiredAt: '2026-07-23T03:00:00Z',
      },
      resolveOperatorRecipients,
    );

    expect(resolveOperatorRecipients).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notifications).toEqual([
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
        data: expect.objectContaining({
          parcelId: PARCEL_ID,
          expiredAt: '2026-07-23T03:00:00Z',
        }),
      }),
    ]);
  });

  it('maps pending-operator-action re-alerts to admins instead of the sender userId', async () => {
    const resolveOperatorRecipients = jest.fn(async () => [SECOND_USER_ID]);

    const notifications = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY,
      {
        eventId: '88888888-8888-4888-8888-888888888888',
        occurredAt: '2026-07-30T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'VR-PCL-20260730-ABCDEFGH',
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        tripId: TRIP_ID,
      },
      resolveOperatorRecipients,
    );

    expect(resolveOperatorRecipients).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notifications).toEqual([
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.PARCEL_IN_TRANSIT,
      }),
    ]);
    expect(notifications[0]?.data).not.toHaveProperty('userId');
  });

  it('uses the canonical vehicle-substitution routing key', () => {
    expect(TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY).toBe('trip.trip.vehicle_substituted');
  });

  it.each([
    ['CHECK_IN_TIMEOUT', 'không check-in đúng hạn'],
    ['FINAL_PAYMENT_TIMEOUT', 'không thanh toán số dư đúng hạn'],
  ] as const)('maps %s to one sender notification with forfeited deposit', async (reason, copy) => {
    const [notification] = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_AUTO_REJECTED_ROUTING_KEY,
      {
        eventId: '99999999-9999-4999-8999-999999999999',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        tripId: TRIP_ID,
        reason,
        forfeitedDepositVnd: 150000,
        refundAmount: 0,
      },
      resolveNoOperatorRecipients,
    );

    expect(notification).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_REJECTED,
        body: expect.stringContaining(copy),
      }),
    );
    expect(notification?.body).toContain('150000 VND');
  });

  it('maps review approval and final payment request to dedicated sender types', async () => {
    const approved = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_REVIEW_APPROVED_ROUTING_KEY,
      {
        eventId: '99999999-9999-4999-8999-999999999999',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        depositRequiredVnd: 50000,
      },
      resolveNoOperatorRecipients,
    );
    const finalPayment = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY,
      {
        eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        tripId: TRIP_ID,
        balanceRequiredVnd: 90000,
        balancePaidVnd: 0,
        finalPaymentDeadline: '2026-07-18T04:00:00Z',
      },
      resolveNoOperatorRecipients,
    );

    expect(approved[0]).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_REVIEW_APPROVED,
        body: expect.stringContaining('50000 VND'),
      }),
    );
    expect(finalPayment[0]).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_FINAL_PAYMENT_REQUIRED,
        body: expect.stringContaining('2026-07-18T04:00:00Z'),
      }),
    );
  });

  it.each([
    ['READY_TO_LOAD', 'sẵn sàng lên xe'],
    ['CANCELLED', 'Số tiền cần hoàn'],
  ] as const)('maps settlement recovery %s as a corrective sender notification', async (status, copy) => {
    const notifications = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY,
      {
        eventId: '99999999-9999-4999-8999-999999999999',
        occurredAt: '2026-07-18T03:00:00Z',
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
        userId: USER_ID,
        tripId: TRIP_ID,
        recoveredStatus: status,
        refundAmountVnd: 50000,
      },
      resolveNoOperatorRecipients,
    );

    expect(notifications).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_SETTLEMENT_RECOVERED,
        body: expect.stringContaining(copy),
      }),
    ]);
  });

  it('uses Parcel snapshot for sender and operator policies on legacy delivery events', async () => {
    const snapshot = async (): Promise<ParcelRecipientSnapshot> => ({
      parcelId: PARCEL_ID,
      tripId: TRIP_ID,
      status: 'DELIVERED',
      senderUserId: USER_ID,
      recipientUserId: SECOND_USER_ID,
      operatorId: OPERATOR_ID,
      dropoffStopId: null,
    });
    const confirmed = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY,
      { parcelId: PARCEL_ID, userId: SECOND_USER_ID },
      resolveNoOperatorRecipients,
      snapshot,
    );
    const rejected = await mapParcelSubscriptionOperatorEventToNotifications(
      PARCEL_DELIVERY_REJECTED_ROUTING_KEY,
      { parcelId: PARCEL_ID, reason: 'Người nhận từ chối' },
      async () => [SECOND_USER_ID],
      snapshot,
    );

    expect(confirmed.map(({ userId }) => userId)).toEqual([USER_ID]);
    expect(confirmed[0]?.data).not.toHaveProperty('userId');
    expect(confirmed[0]?.data).not.toHaveProperty('senderUserId');
    expect(confirmed[0]?.data).not.toHaveProperty('recipientUserId');
    expect(rejected.map(({ userId }) => userId)).toEqual([SECOND_USER_ID]);
    expect(rejected[0]?.body).toContain('Người nhận từ chối');
  });

  it('rejects Sprint 4 Parcel producer-consumer schema drift before persistence', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_LOADED_ROUTING_KEY,
        {
          eventId: '88888888-8888-4888-8888-888888888888',
          occurredAt: '2026-07-18T03:00:00Z',
          parcelId: PARCEL_ID,
          tripId: TRIP_ID,
          actualWeightKg: 12.5,
          userIds: [USER_ID],
          parcelCode: 'legacy',
        },
        resolveNoOperatorRecipients,
      ),
    ).rejects.toThrow(ZodError);
  });

  it('maps parcel loaded event to a sender notification', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_LOADED_ROUTING_KEY,
        {
          eventId: '88888888-8888-4888-8888-888888888888',
          occurredAt: '2026-07-18T03:00:00Z',
          parcelId: PARCEL_ID,
          tripId: TRIP_ID,
          actualWeightKg: 12.5,
          userIds: [USER_ID],
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      {
        userId: USER_ID,
        type: NotificationType.PARCEL_LOADED,
        title: 'Hàng đã được lên xe',
        body: `Đơn gửi hàng ${PARCEL_ID} đã được tải lên xe.`,
        data: expect.objectContaining({
          parcelId: PARCEL_ID,
          tripId: TRIP_ID,
        }),
      },
    ]);
  });

  it('maps parcel review request to operator recipients resolved by provider', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
        {
          operatorId: OPERATOR_ID,
          parcelId: PARCEL_ID,
          reviewReason: 'Oversized parcel',
        },
        async (operatorId) => {
          expect(operatorId).toBe(OPERATOR_ID);
          return [USER_ID, USER_ID, SECOND_USER_ID];
        },
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_REVIEW_REQUESTED,
      }),
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.PARCEL_REVIEW_REQUESTED,
      }),
    ]);
  });

  it('maps parcel transfer confirmed without reusing loaded notification type', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY,
        {
          userId: USER_ID,
          parcelId: PARCEL_ID,
          parcelCode: 'PRC123',
          tripId: TRIP_ID,
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_IN_TRANSIT,
        title: 'Đã xác nhận chuyển chuyến xe',
      }),
    ]);
  });

  it('maps subscription limit warning', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
        {
          userIds: [USER_ID],
          operatorId: OPERATOR_ID,
          operatorName: 'Sao Viet',
          planName: 'Starter',
          resource: 'trip',
          limit: 30,
          attempted: 31,
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
        title: 'Vượt giới hạn gói dịch vụ',
      }),
    ]);
  });

  it('maps voucher consent accepted to operator recipients', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
        {
          operatorId: OPERATOR_ID,
          voucherId: VOUCHER_ID,
        },
        async (operatorId) => {
          expect(operatorId).toBe(OPERATOR_ID);
          return [USER_ID];
        },
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.VOUCHER_CONSENT_ACCEPTED,
        title: 'Đã chấp nhận voucher',
      }),
    ]);
  });

  it.each([
    ['PERCENT_OFF', 15, 'giảm 15%'],
    ['FIXED_AMOUNT', 50000, 'giảm 50000 VND'],
  ] as const)(
    'maps requested %s voucher to operator admins with canonical content',
    async (voucherType, voucherValue, discountText) => {
      const resolveOperatorRecipients = jest.fn(async () => [USER_ID]);

      await expect(
        mapParcelSubscriptionOperatorEventToNotifications(
          BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
          {
            eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            occurredAt: '2026-07-27T08:30:00+07:00',
            voucherId: VOUCHER_ID,
            operatorId: OPERATOR_ID,
            voucherCode: 'SUMMER26',
            voucherType,
            voucherValue,
            userId: SECOND_USER_ID,
          },
          resolveOperatorRecipients,
        ),
      ).resolves.toEqual([
        {
          userId: USER_ID,
          type: NotificationType.VOUCHER_CONSENT_REQUESTED,
          title: 'Đề xuất voucher mới',
          body: `VietRide đề xuất voucher SUMMER26 ${discountText} cho chuyến của nhà xe. Đề xuất đang chờ bạn xác nhận áp dụng.`,
          data: {
            eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            occurredAt: '2026-07-27T08:30:00+07:00',
            voucherId: VOUCHER_ID,
            operatorId: OPERATOR_ID,
            voucherCode: 'SUMMER26',
            voucherType,
            voucherValue,
          },
        },
      ]);
      expect(resolveOperatorRecipients).toHaveBeenCalledWith(OPERATOR_ID);
    },
  );

  it('rejects malformed voucher consent request before recipient lookup', async () => {
    const resolveOperatorRecipients = jest.fn(async () => [USER_ID]);

    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
        {
          eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          occurredAt: '2026-07-27T08:30:00+07:00',
          voucherId: VOUCHER_ID,
          operatorId: OPERATOR_ID,
          voucherCode: 'SUMMER26',
          voucherType: 'UNKNOWN',
          voucherValue: 15,
        },
        resolveOperatorRecipients,
      ),
    ).rejects.toThrow(ZodError);
    expect(resolveOperatorRecipients).not.toHaveBeenCalled();
  });

  it('maps voucher consent rejected with reason', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          voucherId: VOUCHER_ID,
          reason: 'Budget exceeded',
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.VOUCHER_CONSENT_REJECTED,
        body: expect.stringContaining('Budget exceeded'),
      }),
    ]);
  });

  it('maps subscription payment pending warning', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          dueDate: '2026-07-10T00:00:00.000Z',
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_PAYMENT_PENDING_WARN,
      }),
    ]);
  });

  it('maps subscription payment auto reverted', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          previousPlanId: PLAN_ID,
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_PAYMENT_AUTO_REVERTED,
      }),
    ]);
  });

  it('maps invoice issued with a dedicated type and only the web deep-link', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        INVOICE_ISSUED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          invoiceId: INVOICE_ID,
          invoiceNumber: 'VR-INV-202606-000001',
          amount: '1200000',
          invoiceWebUrl: `https://operator.vietride.vn/invoices/${INVOICE_ID}`,
          downloadApiUrl: `https://api.vietride.vn/v1/operator/invoices/${INVOICE_ID}/download`,
        },
        async () => [USER_ID],
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.INVOICE_ISSUED,
        title: 'Hóa đơn mới đã được phát hành',
        data: expect.objectContaining({
          invoiceWebUrl: `https://operator.vietride.vn/invoices/${INVOICE_ID}`,
        }),
      }),
    ]);
    const [notification] = await mapParcelSubscriptionOperatorEventToNotifications(
      INVOICE_ISSUED_ROUTING_KEY,
      {
        userId: USER_ID,
        operatorId: OPERATOR_ID,
        invoiceId: INVOICE_ID,
        invoiceNumber: 'VR-INV-202606-000001',
        amount: '1200000',
        invoiceWebUrl: `https://operator.vietride.vn/invoices/${INVOICE_ID}`,
        downloadApiUrl: `https://api.vietride.vn/v1/operator/invoices/${INVOICE_ID}/download`,
      },
      async () => [USER_ID],
    );
    expect(notification?.data).not.toHaveProperty('downloadApiUrl');
  });

  it('maps trip settlement completed to wallet credited notification', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          settlementId: SETTLEMENT_ID,
          tripId: TRIP_ID,
          netAmount: '2500000',
          settlementMethod: 'AUTO_WEEKLY',
          settledAt: '2026-07-14T02:00:00+00:00',
        },
        async () => [USER_ID],
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.WALLET_CREDITED,
        body: expect.stringContaining('2500000 VND'),
      }),
    ]);
  });

  it('maps payout failed notification', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PAYOUT_FAILED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          payoutId: PAYOUT_ID,
          reason: 'Bank rejected',
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PAYOUT_FAILED,
        body: expect.stringContaining('Bank rejected'),
      }),
    ]);
  });

  it('rejects payload without direct recipient or operator id', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_LOADED_ROUTING_KEY,
        {
          parcelId: PARCEL_ID,
        },
        resolveNoOperatorRecipients,
      ),
    ).rejects.toThrow(ZodError);
  });
});

async function resolveNoOperatorRecipients(operatorId: string): Promise<string[]> {
  void operatorId;
  return [];
}
