/* eslint-disable @typescript-eslint/naming-convention -- existing event schema exports follow contract naming. */
import { z } from 'zod';
import {
  ParcelAutoRejectedEventSchema,
  ParcelLoadedEventSchema,
  type ParcelLoadedEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
  BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY,
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_AUTO_REJECTED_ROUTING_KEY,
  PARCEL_CANCELLED_ROUTING_KEY,
  PARCEL_CREATED_ROUTING_KEY,
  PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY,
  PARCEL_DELIVERY_REJECTED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_REJECTED_ROUTING_KEY,
  PARCEL_RETURNED_ROUTING_KEY,
  PARCEL_RETURN_INITIATED_ROUTING_KEY,
  PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
  PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY,
  PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY,
  PARCEL_TRANSFER_ESCALATED_ROUTING_KEY,
  PARCEL_TRANSFER_INITIATED_ROUTING_KEY,
  PARCEL_UNLOADED_ROUTING_KEY,
  PAYOUT_FAILED_ROUTING_KEY,
  PAYOUT_PROCESSED_ROUTING_KEY,
  SUBSCRIPTION_APPROVED_ROUTING_KEY,
  SUBSCRIPTION_EXPIRED_ROUTING_KEY,
  SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
  SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY,
  SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY,
  SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY,
  TRIP_STOP_ARRIVED_ROUTING_KEY,
  TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
  TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
} from './parcel-subscription-operator-events.constants';

const MoneyAmountSchema = z
  .union([z.number().int().nonnegative(), z.string().regex(/^\d+$/)])
  .optional();
const RequiredMoneyAmountSchema = z.union([
  z.number().int().nonnegative(),
  z.string().regex(/^\d+$/),
]);

const RecipientPayloadSchema = z.object({
  userId: z.string().uuid().optional(),
  userIds: z.array(z.string().uuid()).optional(),
  senderUserId: z.string().uuid().optional(),
  recipientUserId: z.string().uuid().optional(),
  recipientUserIds: z.array(z.string().uuid()).optional(),
  operatorId: z.string().uuid().optional(),
});

const BaseParcelPayloadSchema = z
  .object({
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1).optional(),
    tripId: z.string().uuid().optional(),
    routeName: z.string().trim().min(1).optional(),
    reason: z.string().trim().min(1).optional(),
    refundAmount: MoneyAmountSchema,
  })
  .merge(RecipientPayloadSchema)
  .passthrough();

const ParcelReviewRequestedPayloadSchema = BaseParcelPayloadSchema.and(
  z.object({
    reviewReason: z.string().trim().min(1).optional(),
  }),
);

const ParcelUnloadedPayloadSchema = z
  .object({
    parcelId: z.string().uuid(),
    tripId: z.string().uuid(),
    userIds: z.array(z.string().uuid()).min(1),
  })
  .strict();

const ParcelTransferInitiatedPayloadSchema = BaseParcelPayloadSchema.and(
  z.object({
    newTripId: z.string().uuid().optional(),
    transferReason: z.string().trim().min(1).optional(),
  }),
);

const BaseOperatorPayloadSchema = z
  .object({
    operatorId: z.string().uuid(),
    operatorName: z.string().trim().min(1).optional(),
    planName: z.string().trim().min(1).optional(),
    invoiceId: z.string().uuid().optional(),
    invoiceNumber: z.string().trim().min(1).optional(),
    settlementId: z.string().uuid().optional(),
    tripId: z.string().uuid().optional(),
    payoutId: z.string().uuid().optional(),
    amount: MoneyAmountSchema,
    reason: z.string().trim().min(1).optional(),
  })
  .merge(RecipientPayloadSchema)
  .passthrough();

const SubscriptionLimitPayloadSchema = BaseOperatorPayloadSchema.and(
  z.object({
    resource: z.string().trim().min(1).optional(),
    limit: z.number().int().nonnegative().optional(),
    attempted: z.number().int().nonnegative().optional(),
  }),
);

const VoucherConsentPayloadSchema = BaseOperatorPayloadSchema.and(
  z.object({
    voucherId: z.string().uuid(),
    reason: z.string().trim().min(1).optional(),
  }),
);

const SubscriptionPaymentPendingWarnPayloadSchema = BaseOperatorPayloadSchema.and(
  z.object({
    dueDate: z.string().datetime().optional(),
  }),
);

const SubscriptionPaymentAutoRevertedPayloadSchema = BaseOperatorPayloadSchema.and(
  z.object({
    previousPlanId: z.string().uuid().optional(),
  }),
);

export const InvoiceIssuedPayloadSchema = z.object({
  invoiceId: z.string().uuid(),
  invoiceNumber: z.string().trim().min(1),
  operatorId: z.string().uuid(),
  amount: RequiredMoneyAmountSchema,
  invoiceWebUrl: z.string().url(),
  downloadApiUrl: z.string().url(),
});

export type InvoiceIssuedPayload = z.infer<typeof InvoiceIssuedPayloadSchema>;

const TripSettlementCompletedPayloadSchema = z.object({
  settlementId: z.string().uuid(),
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  netAmount: RequiredMoneyAmountSchema,
  settlementMethod: z.string().trim().min(1),
  settledAt: z.string().datetime({ offset: true }),
});

export type ParcelSubscriptionOperatorRoutingKey =
  | typeof BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY
  | typeof BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY
  | typeof PARCEL_CREATED_ROUTING_KEY
  | typeof PARCEL_LOADED_ROUTING_KEY
  | typeof PARCEL_UNLOADED_ROUTING_KEY
  | typeof PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY
  | typeof PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY
  | typeof PARCEL_DELIVERY_REJECTED_ROUTING_KEY
  | typeof PARCEL_CANCELLED_ROUTING_KEY
  | typeof PARCEL_REJECTED_ROUTING_KEY
  | typeof PARCEL_RETURNED_ROUTING_KEY
  | typeof PARCEL_AUTO_REJECTED_ROUTING_KEY
  | typeof PARCEL_REVIEW_REQUESTED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_INITIATED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_ESCALATED_ROUTING_KEY
  | typeof PARCEL_RETURN_INITIATED_ROUTING_KEY
  | typeof PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY
  | typeof TRIP_STOP_ARRIVED_ROUTING_KEY
  | typeof TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY
  | typeof SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY
  | typeof SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY
  | typeof SUBSCRIPTION_EXPIRED_ROUTING_KEY
  | typeof SUBSCRIPTION_APPROVED_ROUTING_KEY
  | typeof SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY
  | typeof SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY
  | typeof INVOICE_ISSUED_ROUTING_KEY
  | typeof TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY
  | typeof PAYOUT_PROCESSED_ROUTING_KEY
  | typeof PAYOUT_FAILED_ROUTING_KEY;

type RecipientPayload = z.infer<typeof RecipientPayloadSchema>;
type ParcelPayload = z.infer<typeof BaseParcelPayloadSchema>;
type OperatorPayload = z.infer<typeof BaseOperatorPayloadSchema>;

export async function mapParcelSubscriptionOperatorEventToNotifications(
  routingKey: ParcelSubscriptionOperatorRoutingKey,
  payload: unknown,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
): Promise<CreateNotificationDto[]> {
  switch (routingKey) {
    case BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY:
      return fanOut(
        VoucherConsentPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapVoucherConsentAccepted,
      );
    case BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY:
      return fanOut(
        VoucherConsentPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapVoucherConsentRejected,
      );
    case PARCEL_CREATED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelCreated,
      );
    case PARCEL_LOADED_ROUTING_KEY:
      return (await mapParcelLoadedEvent(ParcelLoadedEventSchema.parse(payload))).map((item) => item);
    case PARCEL_UNLOADED_ROUTING_KEY:
      return mapParcelUnloadedEvent(ParcelUnloadedPayloadSchema.parse(payload));
    case PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelPendingConfirm,
      );
    case PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelDeliveryConfirmed,
      );
    case PARCEL_DELIVERY_REJECTED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelDeliveryRejected,
      );
    case PARCEL_CANCELLED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelCancelled,
      );
    case PARCEL_REJECTED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelRejected,
      );
    case PARCEL_RETURNED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelReturned,
      );
    case PARCEL_AUTO_REJECTED_ROUTING_KEY:
      return fanOut(
        ParcelAutoRejectedEventSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelAutoRejected,
      );
    case PARCEL_REVIEW_REQUESTED_ROUTING_KEY:
      return fanOut(
        ParcelReviewRequestedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelReviewRequested,
      );
    case PARCEL_TRANSFER_INITIATED_ROUTING_KEY:
      return fanOut(
        ParcelTransferInitiatedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelTransferInitiated,
      );
    case PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelTransferConfirmed,
      );
    case PARCEL_TRANSFER_ESCALATED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelTransferEscalated,
      );
    case PARCEL_RETURN_INITIATED_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelReturnInitiated,
      );
    case PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY:
      return fanOut(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapParcelPendingOperatorAction,
      );
    case TRIP_STOP_ARRIVED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapTripStopArrived,
      );
    case TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapTripVehicleSubstituted,
      );
    case SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY:
      return fanOut(
        SubscriptionLimitPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionLimit,
      );
    case SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionTrial,
      );
    case SUBSCRIPTION_EXPIRED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionExpired,
      );
    case SUBSCRIPTION_APPROVED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionApproved,
      );
    case SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY:
      return fanOut(
        SubscriptionPaymentPendingWarnPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionPaymentPendingWarn,
      );
    case SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY:
      return fanOut(
        SubscriptionPaymentAutoRevertedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapSubscriptionPaymentAutoReverted,
      );
    case INVOICE_ISSUED_ROUTING_KEY:
      return fanOut(
        InvoiceIssuedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapInvoiceIssued,
      );
    case TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY:
      return fanOut(
        TripSettlementCompletedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapTripSettlementCompleted,
      );
    case PAYOUT_PROCESSED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapPayoutProcessed,
      );
    case PAYOUT_FAILED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapPayoutFailed,
      );
  }
}

function mapVoucherConsentAccepted(
  userId: string,
  payload: z.infer<typeof VoucherConsentPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.VOUCHER_CONSENT_ACCEPTED,
    title: 'Da chap nhan voucher',
    body: `${formatOperatorLabel(payload)} da chap nhan voucher ${payload.voucherId}.`,
    data: buildNotificationData(payload),
  };
}

function mapVoucherConsentRejected(
  userId: string,
  payload: z.infer<typeof VoucherConsentPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.VOUCHER_CONSENT_REJECTED,
    title: 'Da tu choi voucher',
    body: `${formatOperatorLabel(payload)} da tu choi voucher ${payload.voucherId}.${
      payload.reason ? ` Ly do: ${payload.reason}.` : ''
    }`,
    data: buildNotificationData(payload),
  };
}

function mapParcelCreated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Don gui hang da duoc tao',
    'da duoc tao.',
  );
}

function mapParcelLoaded(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_LOADED,
    'Hang da duoc len xe',
    'da duoc tai len xe.',
  );
}

function mapParcelLoadedEvent(payload: ParcelLoadedEvent): CreateNotificationDto[] {
  return payload.userIds.map((userId) =>
    mapParcelLoaded(userId, { ...payload, userId }),
  );
}

function mapParcelUnloadedEvent(
  payload: z.infer<typeof ParcelUnloadedPayloadSchema>,
): CreateNotificationDto[] {
  return payload.userIds.map((userId) =>
    mapParcelUnloaded(userId, { ...payload, userId }),
  );
}

function mapParcelUnloaded(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Hang da roi xe',
    'da duoc do khoi xe.',
  );
}

function mapParcelPendingConfirm(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
    'Cho xac nhan giao hang',
    'da giao toi nguoi nhan va dang cho xac nhan.',
  );
}

function mapParcelDeliveryConfirmed(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Giao hang thanh cong',
    'da duoc xac nhan giao thanh cong.',
  );
}

function mapParcelDeliveryRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Nguoi nhan tu choi hang',
    'bi tu choi khi giao.',
  );
}

function mapParcelCancelled(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Don gui hang da bi huy',
    'da bi huy.',
  );
}

function mapParcelRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Don gui hang bi tu choi',
    'da bi tu choi.',
  );
}

function mapParcelReturned(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_RETURNED,
    'Hang dang duoc hoan tra',
    'dang duoc hoan tra.',
  );
}

function mapParcelAutoRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  const refundText = payload.refundAmount
    ? ` So tien hoan: ${formatMoney(payload.refundAmount)} VND.`
    : '';

  return {
    ...buildParcelNotification(
      userId,
      payload,
      NotificationType.PARCEL_REJECTED,
      'Don gui hang tu dong bi tu choi',
      'da qua thoi gian xu ly va bi tu choi.',
    ),
    body: `${formatParcelLabel(payload)} da qua thoi gian xu ly va bi tu choi.${refundText}`,
  };
}

function mapParcelReviewRequested(
  userId: string,
  payload: z.infer<typeof ParcelReviewRequestedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_REVIEW_REQUESTED,
    title: 'Can xem xet don gui hang',
    body: `${formatParcelLabel(payload)} can duoc nhan vien van hanh xem xet.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelTransferInitiated(
  userId: string,
  payload: z.infer<typeof ParcelTransferInitiatedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_IN_TRANSIT,
    title: 'Don gui hang duoc chuyen chuyen xe',
    body: `${formatParcelLabel(payload)} dang duoc chuyen sang chuyen xe phu hop hon.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelTransferConfirmed(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Da xac nhan chuyen chuyen xe',
    'da duoc xac nhan chuyen sang chuyen xe moi.',
  );
}

function mapParcelTransferEscalated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Can xu ly chuyen chuyen xe',
    'qua thoi gian xac nhan chuyen chuyen xe va can van hanh xu ly.',
  );
}

function mapParcelReturnInitiated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_RETURNED,
    'Bat dau hoan tra hang',
    'da bat dau quy trinh hoan tra.',
  );
}

function mapParcelPendingOperatorAction(
  userId: string,
  payload: ParcelPayload,
): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Can van hanh xu ly don gui hang',
    'can nha xe xu ly thu cong.',
  );
}

function mapTripStopArrived(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_VEHICLE_APPROACHING,
    title: 'Xe da den diem dung',
    body: `Chuyen ${payload.tripId ?? 'xe'} da ghi nhan den diem dung.`,
    data: buildNotificationData(payload),
  };
}

function mapTripVehicleSubstituted(
  userId: string,
  payload: OperatorPayload,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Da thay xe cho chuyen',
    body: `Chuyen ${payload.tripId ?? 'xe'} da duoc gan xe thay the.${payload.reason ? ` Ly do: ${payload.reason}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionLimit(
  userId: string,
  payload: z.infer<typeof SubscriptionLimitPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
    title: 'Vuot gioi han goi dich vu',
    body: `${formatOperatorLabel(payload)} da cham gioi han goi${payload.planName ? ` ${payload.planName}` : ''}.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionTrial(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_TRIAL_EXPIRING,
    title: 'Goi dung thu sap het han',
    body: `${formatOperatorLabel(payload)} sap het thoi gian dung thu.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionExpired(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_EXPIRED,
    title: 'Goi dich vu da het han',
    body: `${formatOperatorLabel(payload)} da het han goi dich vu.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionApproved(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_APPROVED,
    title: 'Goi dich vu da duoc duyet',
    body: `${formatOperatorLabel(payload)} da duoc kich hoat goi dich vu.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionPaymentPendingWarn(
  userId: string,
  payload: z.infer<typeof SubscriptionPaymentPendingWarnPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_PAYMENT_PENDING_WARN,
    title: 'Can thanh toan goi dich vu',
    body: `${formatOperatorLabel(payload)} co thanh toan goi dich vu sap den han${payload.dueDate ? ` vao ${payload.dueDate}` : ''}.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionPaymentAutoReverted(
  userId: string,
  payload: z.infer<typeof SubscriptionPaymentAutoRevertedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_PAYMENT_AUTO_REVERTED,
    title: 'Goi dich vu da duoc hoan ve',
    body: `${formatOperatorLabel(payload)} da duoc hoan ve goi truoc do.`,
    data: buildNotificationData(payload),
  };
}

function mapInvoiceIssued(
  userId: string,
  payload: z.infer<typeof InvoiceIssuedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.INVOICE_ISSUED,
    title: 'Hoa don moi da duoc phat hanh',
    body: `Hoa don ${payload.invoiceNumber} da san sang.`,
    data: {
      invoiceId: payload.invoiceId,
      invoiceNumber: payload.invoiceNumber,
      operatorId: payload.operatorId,
      amount: payload.amount,
      invoiceWebUrl: payload.invoiceWebUrl,
    },
  };
}

function mapTripSettlementCompleted(
  userId: string,
  payload: z.infer<typeof TripSettlementCompletedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.WALLET_CREDITED,
    title: 'Da tat toan doanh thu chuyen',
    body: `Da tat toan ${formatMoney(payload.netAmount)} VND tu chuyen ${payload.tripId} vao vi nha xe.`,
    data: buildNotificationData(payload),
  };
}

function mapPayoutProcessed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_PROCESSED,
    title: 'Lenh chi tra da xu ly',
    body: `Lenh chi tra ${payload.payoutId ?? ''} da duoc xu ly thanh cong.`,
    data: buildNotificationData(payload),
  };
}

function mapPayoutFailed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_FAILED,
    title: 'Lenh chi tra that bai',
    body: `Lenh chi tra ${payload.payoutId ?? ''} xu ly that bai.${payload.reason ? ` Ly do: ${payload.reason}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

async function fanOut<TPayload extends RecipientPayload>(
  payload: TPayload,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): Promise<CreateNotificationDto[]> {
  const userIds = await collectRecipientUserIds(payload, resolveOperatorRecipientUserIds);
  return userIds.map((userId) => mapper(userId, payload));
}

async function collectRecipientUserIds(
  payload: RecipientPayload,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
): Promise<string[]> {
  const directUserIds = [
    payload.userId,
    ...(payload.userIds ?? []),
    ...(payload.recipientUserIds ?? []),
    ...(payload.senderUserId ? [payload.senderUserId] : []),
    ...(payload.recipientUserId ? [payload.recipientUserId] : []),
  ].filter(isString);
  if (directUserIds.length > 0) {
    return [...new Set(directUserIds)];
  }

  if (!payload.operatorId) {
    throw new z.ZodError([
      {
        code: z.ZodIssueCode.custom,
        message: 'At least one recipient user id or operatorId is required',
        path: ['userId'],
      },
    ]);
  }

  return [...new Set(await resolveOperatorRecipientUserIds(payload.operatorId))];
}

function buildParcelNotification(
  userId: string,
  payload: ParcelPayload,
  type: NotificationType,
  title: string,
  actionText: string,
): CreateNotificationDto {
  return {
    userId,
    type,
    title,
    body: `${formatParcelLabel(payload)} ${actionText}${payload.reason ? ` Ly do: ${payload.reason}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

function formatParcelLabel(payload: ParcelPayload): string {
  return payload.parcelCode ? `Don ${payload.parcelCode}` : `Don gui hang ${payload.parcelId}`;
}

function formatOperatorLabel(payload: OperatorPayload): string {
  return payload.operatorName ? `Nha xe ${payload.operatorName}` : `Nha xe ${payload.operatorId}`;
}

function formatMoney(amount: z.infer<typeof MoneyAmountSchema>): string {
  return amount === undefined ? '0' : amount.toString();
}

function buildNotificationData(
  payload: RecipientPayload & Record<string, unknown>,
): Record<string, unknown> {
  const { userId, userIds, recipientUserIds, ...data } = payload;

  return {
    ...data,
    userId: userId ?? null,
    userIds: userIds ?? null,
    recipientUserIds: recipientUserIds ?? null,
  };
}

function isString(value: string | undefined): value is string {
  return typeof value === 'string';
}
