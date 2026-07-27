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
    title: 'Đã chấp nhận voucher',
    body: `${formatOperatorLabel(payload)} đã chấp nhận voucher ${payload.voucherId}.`,
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
    title: 'Đã từ chối voucher',
    body: `${formatOperatorLabel(payload)} đã từ chối voucher ${payload.voucherId}.${
      payload.reason ? ` Lý do: ${payload.reason}.` : ''
    }`,
    data: buildNotificationData(payload),
  };
}

function mapParcelCreated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Đơn gửi hàng đã được tạo',
    'đã được tạo.',
  );
}

function mapParcelLoaded(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_LOADED,
    'Hàng đã được lên xe',
    'đã được tải lên xe.',
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
    'Hàng đã rời xe',
    'đã được dỡ khỏi xe.',
  );
}

function mapParcelPendingConfirm(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
    'Chờ xác nhận giao hàng',
    'đã giao tới người nhận và đang chờ xác nhận.',
  );
}

function mapParcelDeliveryConfirmed(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Giao hàng thành công',
    'đã được xác nhận giao thành công.',
  );
}

function mapParcelDeliveryRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Người nhận từ chối hàng',
    'bị từ chối khi giao.',
  );
}

function mapParcelCancelled(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Đơn gửi hàng đã bị hủy',
    'đã bị hủy.',
  );
}

function mapParcelRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_REJECTED,
    'Đơn gửi hàng bị từ chối',
    'đã bị từ chối.',
  );
}

function mapParcelReturned(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_RETURNED,
    'Hàng đang được hoàn trả',
    'đang được hoàn trả.',
  );
}

function mapParcelAutoRejected(userId: string, payload: ParcelPayload): CreateNotificationDto {
  const refundText = payload.refundAmount
    ? ` Số tiền hoàn: ${formatMoney(payload.refundAmount)} VND.`
    : '';

  return {
    ...buildParcelNotification(
      userId,
      payload,
      NotificationType.PARCEL_REJECTED,
      'Đơn gửi hàng tự động bị từ chối',
      'đã quá thời gian xử lý và bị từ chối.',
    ),
    body: `${formatParcelLabel(payload)} đã quá thời gian xử lý và bị từ chối.${refundText}`,
  };
}

function mapParcelReviewRequested(
  userId: string,
  payload: z.infer<typeof ParcelReviewRequestedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_REVIEW_REQUESTED,
    title: 'Cần xem xét đơn gửi hàng',
    body: `${formatParcelLabel(payload)} cần được nhân viên vận hành xem xét.`,
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
    title: 'Đơn gửi hàng được chuyển chuyến xe',
    body: `${formatParcelLabel(payload)} đang được chuyển sang chuyến xe phù hợp hơn.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelTransferConfirmed(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Đã xác nhận chuyển chuyến xe',
    'đã được xác nhận chuyển sang chuyến xe mới.',
  );
}

function mapParcelTransferEscalated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_IN_TRANSIT,
    'Cần xử lý chuyển chuyến xe',
    'quá thời gian xác nhận chuyển chuyến xe và cần vận hành xử lý.',
  );
}

function mapParcelReturnInitiated(userId: string, payload: ParcelPayload): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_RETURNED,
    'Bắt đầu hoàn trả hàng',
    'đã bắt đầu quy trình hoàn trả.',
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
    'Cần vận hành xử lý đơn gửi hàng',
    'cần nhà xe xử lý thủ công.',
  );
}

function mapTripStopArrived(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_VEHICLE_APPROACHING,
    title: 'Xe đã đến điểm dừng',
    body: `Chuyến ${payload.tripId ?? 'xe'} đã ghi nhận đến điểm dừng.`,
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
    title: 'Đã thay xe cho chuyến',
    body: `Chuyến ${payload.tripId ?? 'xe'} đã được gán xe thay thế.${payload.reason ? ` Lý do: ${payload.reason}.` : ''}`,
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
    title: 'Vượt giới hạn gói dịch vụ',
    body: `${formatOperatorLabel(payload)} đã chạm giới hạn gói${payload.planName ? ` ${payload.planName}` : ''}.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionTrial(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_TRIAL_EXPIRING,
    title: 'Gói dùng thử sắp hết hạn',
    body: `${formatOperatorLabel(payload)} sắp hết thời gian dùng thử.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionExpired(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_EXPIRED,
    title: 'Gói dịch vụ đã hết hạn',
    body: `${formatOperatorLabel(payload)} đã hết hạn gói dịch vụ.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionApproved(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_APPROVED,
    title: 'Gói dịch vụ đã được duyệt',
    body: `${formatOperatorLabel(payload)} đã được kích hoạt gói dịch vụ.`,
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
    title: 'Cần thanh toán gói dịch vụ',
    body: `${formatOperatorLabel(payload)} có thanh toán gói dịch vụ sắp đến hạn${payload.dueDate ? ` vào ${payload.dueDate}` : ''}.`,
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
    title: 'Gói dịch vụ đã được hoàn về',
    body: `${formatOperatorLabel(payload)} đã được hoàn về gói trước đó.`,
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
    title: 'Hóa đơn mới đã được phát hành',
    body: `Hóa đơn ${payload.invoiceNumber} đã sẵn sàng.`,
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
    title: 'Đã tất toán doanh thu chuyến',
    body: `Đã tất toán ${formatMoney(payload.netAmount)} VND từ chuyến ${payload.tripId} vào ví nhà xe.`,
    data: buildNotificationData(payload),
  };
}

function mapPayoutProcessed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_PROCESSED,
    title: 'Lệnh chi trả đã xử lý',
    body: `Lệnh chi trả ${payload.payoutId ?? ''} đã được xử lý thành công.`,
    data: buildNotificationData(payload),
  };
}

function mapPayoutFailed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_FAILED,
    title: 'Lệnh chi trả thất bại',
    body: `Lệnh chi trả ${payload.payoutId ?? ''} xử lý thất bại.${payload.reason ? ` Lý do: ${payload.reason}.` : ''}`,
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
    body: `${formatParcelLabel(payload)} ${actionText}${payload.reason ? ` Lý do: ${payload.reason}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

function formatParcelLabel(payload: ParcelPayload): string {
  return payload.parcelCode ? `Đơn ${payload.parcelCode}` : `Đơn gửi hàng ${payload.parcelId}`;
}

function formatOperatorLabel(payload: OperatorPayload): string {
  return payload.operatorName ? `Nhà xe ${payload.operatorName}` : `Nhà xe ${payload.operatorId}`;
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
