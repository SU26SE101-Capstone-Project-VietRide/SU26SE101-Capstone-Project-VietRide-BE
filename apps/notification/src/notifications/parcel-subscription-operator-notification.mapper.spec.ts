import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
  PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY,
  PAYOUT_FAILED_ROUTING_KEY,
  SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
  TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
} from './parcel-subscription-operator-events.constants';
import { mapParcelSubscriptionOperatorEventToNotifications } from './parcel-subscription-operator-notification.mapper';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const PARCEL_ID = '44444444-4444-4444-8444-444444444444';
const TRIP_ID = '55555555-5555-4555-8555-555555555555';
const SETTLEMENT_ID = '66666666-6666-4666-8666-666666666666';
const INVOICE_ID = '77777777-7777-4777-8777-777777777777';
const PAYOUT_ID = '88888888-8888-4888-8888-888888888888';

describe('mapParcelSubscriptionOperatorEventToNotifications', () => {
  it('maps parcel loaded event to a sender notification', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        PARCEL_LOADED_ROUTING_KEY,
        {
          userId: USER_ID,
          parcelId: PARCEL_ID,
          parcelCode: 'PRC123',
          tripId: TRIP_ID,
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      {
        userId: USER_ID,
        type: NotificationType.PARCEL_LOADED,
        title: 'Hang da duoc len xe',
        body: 'Don PRC123 da duoc tai len xe.',
        data: expect.objectContaining({
          parcelId: PARCEL_ID,
          parcelCode: 'PRC123',
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
        title: 'Da xac nhan chuyen chuyen xe',
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
        title: 'Vuot gioi han goi dich vu',
      }),
    ]);
  });

  it('maps invoice issued with existing subscription notification type', async () => {
    await expect(
      mapParcelSubscriptionOperatorEventToNotifications(
        INVOICE_ISSUED_ROUTING_KEY,
        {
          userId: USER_ID,
          operatorId: OPERATOR_ID,
          invoiceId: INVOICE_ID,
          invoiceNumber: 'VR-INV-202606-000001',
        },
        resolveNoOperatorRecipients,
      ),
    ).resolves.toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_APPROVED,
        title: 'Hoa don moi da duoc phat hanh',
      }),
    ]);
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
          amount: '2500000',
        },
        resolveNoOperatorRecipients,
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
