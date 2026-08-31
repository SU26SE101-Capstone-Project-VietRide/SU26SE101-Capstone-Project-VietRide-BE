import { z } from 'zod';
import {
  BookingVoucherConsentRequestedEventSchema,
  ParcelAutoRejectedEventSchema,
  ParcelDeliveredPendingConfirmEventSchema,
  ParcelDeliveryConfirmationRealertedEventSchema,
  ParcelFinalPaymentRequestedEventSchema,
  ParcelLoadedEventSchema,
  ParcelPendingOperatorActionRealertedEventSchema,
  ParcelReviewApprovedEventSchema,
  ParcelSettlementRecoveredEventSchema,
  type ParcelAutoRejectedEvent,
  type ParcelDeliveredPendingConfirmEvent,
  type ParcelDeliveryConfirmationRealertedEvent,
  type ParcelFinalPaymentRequestedEvent,
  type ParcelLoadedEvent,
  type ParcelPendingOperatorActionRealertedEvent,
  type ParcelReviewApprovedEvent,
  type ParcelSettlementRecoveredEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { formatVietnamDateTime } from '@vietride/nest-common';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  formatDisplayReason,
  formatOperatorLabel,
  formatParcelLabel,
} from './notification-display';
import type { ParcelRecipientSnapshot } from './parcel-recipient.provider';
import {
  BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
  BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
  BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY,
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_AUTO_REJECTED_ROUTING_KEY,
  PARCEL_CANCELLED_ROUTING_KEY,
  PARCEL_CREATED_ROUTING_KEY,
  PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY,
  PARCEL_DELIVERY_REJECTED_ROUTING_KEY,
  PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY,
  PARCEL_INCIDENT_OPENED_ROUTING_KEY,
  PARCEL_INCIDENT_UPDATED_ROUTING_KEY,
  PARCEL_CLAIM_SUBMITTED_ROUTING_KEY,
  PARCEL_CLAIM_DECIDED_ROUTING_KEY,
  PARCEL_COMPENSATION_PAID_ROUTING_KEY,
  PARCEL_COMPENSATION_FUNDING_PENDING_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_REJECTED_ROUTING_KEY,
  PARCEL_RETURNED_ROUTING_KEY,
  PARCEL_RETURN_INITIATED_ROUTING_KEY,
  PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
  PARCEL_REVIEW_APPROVED_ROUTING_KEY,
  PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY,
  PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY,
  PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY,
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
  recipientUserId: z.string().uuid().nullable().optional(),
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

const ParcelReliabilityIncidentPayloadSchema = z
  .object({
    incidentId: z.string().uuid(),
    parcelId: z.string().uuid(),
    operatorId: z.string().uuid().optional(),
    type: z.string().trim().min(1).optional(),
    status: z.string().trim().min(1).optional(),
    searchDeadline: z.string().datetime({ offset: true }).optional(),
    targetTripId: z.string().uuid().optional(),
  })
  .passthrough();

const ParcelClaimPayloadSchema = z
  .object({
    claimId: z.string().uuid(),
    parcelId: z.string().uuid(),
    operatorId: z.string().uuid(),
    beneficiaryUserId: z.string().uuid(),
    status: z.string().trim().min(1).optional(),
    totalAwardVnd: MoneyAmountSchema,
  })
  .passthrough();

const ParcelCompensationPayloadSchema = z
  .object({
    payoutId: z.string().uuid(),
    claimId: z.string().uuid(),
    parcelId: z.string().uuid(),
    operatorId: z.string().uuid(),
    beneficiaryUserId: z.string().uuid(),
    amountVnd: RequiredMoneyAmountSchema,
    status: z.string().trim().min(1),
  })
  .passthrough();

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

const TripVehicleSubstitutedPayloadSchema = BaseOperatorPayloadSchema.and(
  z.object({
    operatorId: z.string().uuid(),
    newTripId: z.string().uuid().optional(),
    newVehicleId: z.string().uuid().optional(),
    newVehiclePlateNumber: z.string().trim().min(1).optional(),
    incidentId: z.string().uuid().optional(),
    incidentLatitude: z.number().nullable().optional(),
    incidentLongitude: z.number().nullable().optional(),
    incidentDescription: z.string().nullable().optional(),
    newDriverId: z.string().uuid().optional(),
    newAssistantId: z.string().uuid().optional(),
  }),
);

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
    dueDate: z.string().datetime({ offset: true }).optional(),
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
  | typeof BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY
  | typeof BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY
  | typeof BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY
  | typeof PARCEL_CREATED_ROUTING_KEY
  | typeof PARCEL_LOADED_ROUTING_KEY
  | typeof PARCEL_UNLOADED_ROUTING_KEY
  | typeof PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY
  | typeof PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY
  | typeof PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY
  | typeof PARCEL_DELIVERY_REJECTED_ROUTING_KEY
  | typeof PARCEL_CANCELLED_ROUTING_KEY
  | typeof PARCEL_REJECTED_ROUTING_KEY
  | typeof PARCEL_RETURNED_ROUTING_KEY
  | typeof PARCEL_AUTO_REJECTED_ROUTING_KEY
  | typeof PARCEL_REVIEW_REQUESTED_ROUTING_KEY
  | typeof PARCEL_REVIEW_APPROVED_ROUTING_KEY
  | typeof PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY
  | typeof PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_INITIATED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY
  | typeof PARCEL_TRANSFER_ESCALATED_ROUTING_KEY
  | typeof PARCEL_RETURN_INITIATED_ROUTING_KEY
  | typeof PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY
  | typeof PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY
  | typeof PARCEL_INCIDENT_OPENED_ROUTING_KEY
  | typeof PARCEL_INCIDENT_UPDATED_ROUTING_KEY
  | typeof PARCEL_CLAIM_SUBMITTED_ROUTING_KEY
  | typeof PARCEL_CLAIM_DECIDED_ROUTING_KEY
  | typeof PARCEL_COMPENSATION_PAID_ROUTING_KEY
  | typeof PARCEL_COMPENSATION_FUNDING_PENDING_ROUTING_KEY
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
  resolveParcelSnapshot?: (parcelId: string) => Promise<ParcelRecipientSnapshot>,
): Promise<CreateNotificationDto[]> {
  switch (routingKey) {
    case BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY:
      return fanOut(
        BookingVoucherConsentRequestedEventSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapVoucherConsentRequested,
      );
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
      return mapParcelCreatedEvent(BaseParcelPayloadSchema.parse(payload));
    case PARCEL_LOADED_ROUTING_KEY:
      return (await mapParcelLoadedEvent(ParcelLoadedEventSchema.parse(payload))).map(
        (item) => item,
      );
    case PARCEL_UNLOADED_ROUTING_KEY:
      return mapParcelUnloadedEvent(ParcelUnloadedPayloadSchema.parse(payload));
    case PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY:
      return mapParcelPendingConfirmEvent(ParcelDeliveredPendingConfirmEventSchema.parse(payload));
    case PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY:
      return mapParcelOperatorEvent(
        ParcelDeliveryConfirmationRealertedEventSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelDeliveryConfirmationRealerted,
      );
    case PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY:
      return mapParcelSnapshotSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelDeliveryConfirmed,
      );
    case PARCEL_DELIVERY_REJECTED_ROUTING_KEY:
      return mapParcelOperatorEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelDeliveryRejected,
      );
    case PARCEL_CANCELLED_ROUTING_KEY:
      return mapParcelSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelCancelled,
      );
    case PARCEL_REJECTED_ROUTING_KEY:
      return mapParcelSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelRejected,
      );
    case PARCEL_RETURNED_ROUTING_KEY:
      return mapParcelSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelReturned,
      );
    case PARCEL_AUTO_REJECTED_ROUTING_KEY:
      return mapDirectParcelUser(
        ParcelAutoRejectedEventSchema.parse(payload),
        mapParcelAutoRejected,
      );
    case PARCEL_REVIEW_REQUESTED_ROUTING_KEY:
      return mapParcelOperatorEvent(
        ParcelReviewRequestedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelReviewRequested,
      );
    case PARCEL_REVIEW_APPROVED_ROUTING_KEY:
      return mapDirectParcelUser(
        ParcelReviewApprovedEventSchema.parse(payload),
        mapParcelReviewApproved,
      );
    case PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY:
      return mapDirectParcelUser(
        ParcelFinalPaymentRequestedEventSchema.parse(payload),
        mapParcelFinalPaymentRequested,
      );
    case PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY:
      return mapDirectParcelUser(
        ParcelSettlementRecoveredEventSchema.parse(payload),
        mapParcelSettlementRecovered,
      );
    case PARCEL_TRANSFER_INITIATED_ROUTING_KEY:
      return mapParcelSenderEvent(
        ParcelTransferInitiatedPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelTransferInitiated,
      );
    case PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY:
      return mapParcelSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelTransferConfirmed,
      );
    case PARCEL_TRANSFER_ESCALATED_ROUTING_KEY:
      return mapParcelOperatorEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelTransferEscalated,
      );
    case PARCEL_RETURN_INITIATED_ROUTING_KEY:
      return mapParcelSenderEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveParcelSnapshot,
        mapParcelReturnInitiated,
      );
    case PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY:
      return mapParcelOperatorEvent(
        BaseParcelPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelPendingOperatorAction,
      );
    case PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY:
      return mapParcelOperatorEvent(
        ParcelPendingOperatorActionRealertedEventSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
        mapParcelPendingOperatorActionRealerted,
      );
    case PARCEL_INCIDENT_OPENED_ROUTING_KEY:
      return mapParcelReliabilityIncident(
        ParcelReliabilityIncidentPayloadSchema.parse(payload),
        true,
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
      );
    case PARCEL_INCIDENT_UPDATED_ROUTING_KEY:
      return mapParcelReliabilityIncident(
        ParcelReliabilityIncidentPayloadSchema.parse(payload),
        false,
        resolveOperatorRecipientUserIds,
        resolveParcelSnapshot,
      );
    case PARCEL_CLAIM_SUBMITTED_ROUTING_KEY:
      return mapParcelClaimSubmitted(
        ParcelClaimPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
      );
    case PARCEL_CLAIM_DECIDED_ROUTING_KEY:
      return [mapParcelClaimDecided(ParcelClaimPayloadSchema.parse(payload))];
    case PARCEL_COMPENSATION_PAID_ROUTING_KEY:
      return [mapParcelCompensationPaid(ParcelCompensationPayloadSchema.parse(payload))];
    case PARCEL_COMPENSATION_FUNDING_PENDING_ROUTING_KEY:
      return [mapParcelCompensationFundingPending(ParcelCompensationPayloadSchema.parse(payload))];
    case TRIP_STOP_ARRIVED_ROUTING_KEY:
      return fanOut(
        BaseOperatorPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
        mapTripStopArrived,
      );
    case TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY:
      return mapTripVehicleSubstitutedEvent(
        TripVehicleSubstitutedPayloadSchema.parse(payload),
        resolveOperatorRecipientUserIds,
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

function mapVoucherConsentRequested(
  userId: string,
  payload: z.infer<typeof BookingVoucherConsentRequestedEventSchema>,
): CreateNotificationDto {
  const discount =
    payload.voucherType === 'PERCENT_OFF'
      ? `${payload.voucherValue}%`
      : `${payload.voucherValue} VND`;
  return {
    userId,
    type: NotificationType.VOUCHER_CONSENT_REQUESTED,
    title: 'Đề xuất mã ưu đãi mới',
    body: `VietRide đề xuất mã ưu đãi ${payload.voucherCode} giảm ${discount} cho chuyến của nhà xe. Đề xuất đang chờ bạn xác nhận áp dụng.`,
    data: buildNotificationData(payload),
  };
}

function mapVoucherConsentAccepted(
  userId: string,
  payload: z.infer<typeof VoucherConsentPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.VOUCHER_CONSENT_ACCEPTED,
    title: 'Đã chấp nhận mã ưu đãi',
    body: `${formatOperatorLabel(payload.operatorName)} đã chấp nhận mã ưu đãi.`,
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
    title: 'Đã từ chối mã ưu đãi',
    body: `${formatOperatorLabel(payload.operatorName)} đã từ chối mã ưu đãi.${
      payload.reason ? ` Lý do: ${formatDisplayReason(payload.reason)}.` : ''
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

function mapParcelCreatedEvent(payload: ParcelPayload): CreateNotificationDto[] {
  const recipients = [payload.senderUserId, payload.recipientUserId].filter(isString);
  return [...new Set(recipients)].map((userId) => mapParcelCreated(userId, payload));
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
  return payload.userIds.map((userId) => mapParcelLoaded(userId, { ...payload, userId }));
}

function mapParcelUnloadedEvent(
  payload: z.infer<typeof ParcelUnloadedPayloadSchema>,
): CreateNotificationDto[] {
  return payload.userIds.map((userId) => mapParcelUnloaded(userId, { ...payload, userId }));
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

function mapParcelPendingConfirm(
  userId: string,
  payload: ParcelDeliveredPendingConfirmEvent,
): CreateNotificationDto {
  return buildParcelNotification(
    userId,
    payload,
    NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
    'Chờ xác nhận giao hàng',
    'đã giao tới người nhận và đang chờ xác nhận.',
  );
}

function mapParcelPendingConfirmEvent(
  payload: ParcelDeliveredPendingConfirmEvent,
): CreateNotificationDto[] {
  const recipients = [payload.userId, ...(payload.recipientUserIds ?? [])].filter(isString);
  return [...new Set(recipients)].map((userId) => mapParcelPendingConfirm(userId, payload));
}

function mapParcelDeliveryConfirmationRealerted(
  userId: string,
  payload: ParcelDeliveryConfirmationRealertedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
    title: 'Xác nhận giao hàng đã quá hạn',
    body: `${formatParcelLabel(payload.parcelCode)} đã quá hạn xác nhận từ ${formatVietnamDateTime(payload.expiredAt)} và cần nhà xe xử lý.`,
    data: buildNotificationData(payload),
  };
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

function mapParcelAutoRejected(
  userId: string,
  payload: ParcelAutoRejectedEvent,
): CreateNotificationDto {
  if ('reason' in payload) {
    const timeoutText =
      payload.reason === 'CHECK_IN_TIMEOUT'
        ? 'không xác nhận lên xe đúng hạn'
        : 'không thanh toán số dư đúng hạn';
    return {
      userId,
      type: NotificationType.PARCEL_REJECTED,
      title: 'Đơn gửi hàng bị từ chối do quá hạn',
      body: `${formatParcelLabel(payload.parcelCode)} đã ${timeoutText} và bị từ chối. Số tiền cọc bị giữ: ${formatMoney(payload.forfeitedDepositVnd)} VND.`,
      data: buildNotificationData(payload),
    };
  }
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
    body: `${formatParcelLabel(payload.parcelCode)} đã quá thời gian xử lý và bị từ chối.${refundText}`,
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
    body: `${formatParcelLabel(payload.parcelCode)} cần được nhân viên vận hành xem xét.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelReviewApproved(
  userId: string,
  payload: ParcelReviewApprovedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_REVIEW_APPROVED,
    title: 'Đơn gửi hàng đã được duyệt',
    body: `${formatParcelLabel(payload.parcelCode)} đã được duyệt. Vui lòng thanh toán tiền cọc ${formatMoney(payload.depositRequiredVnd)} VND.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelFinalPaymentRequested(
  userId: string,
  payload: ParcelFinalPaymentRequestedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_FINAL_PAYMENT_REQUIRED,
    title: 'Cần thanh toán số dư đơn gửi hàng',
    body: `${formatParcelLabel(payload.parcelCode)} cần thanh toán số dư ${formatMoney(payload.balanceRequiredVnd)} VND trước ${formatVietnamDateTime(payload.finalPaymentDeadline)}.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelSettlementRecovered(
  userId: string,
  payload: ParcelSettlementRecoveredEvent,
): CreateNotificationDto {
  if (payload.recoveredStatus === 'READY_TO_LOAD') {
    return {
      userId,
      type: NotificationType.PARCEL_SETTLEMENT_RECOVERED,
      title: 'Đính chính trạng thái đơn gửi hàng',
      body: `${formatParcelLabel(payload.parcelCode)} đã được khôi phục và hiện sẵn sàng lên xe. Thông báo quá hạn trước đó không còn hiệu lực.`,
      data: buildNotificationData(payload),
    };
  }

  return {
    userId,
    type: NotificationType.PARCEL_SETTLEMENT_RECOVERED,
    title: 'Đính chính trạng thái đơn gửi hàng',
    body: `${formatParcelLabel(payload.parcelCode)} đã được đính chính sang trạng thái đã hủy. Số tiền cần hoàn: ${formatMoney(payload.refundAmountVnd)} VND.`,
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
    body: `${formatParcelLabel(payload.parcelCode)} đang được chuyển sang chuyến xe phù hợp hơn.`,
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

function mapParcelPendingOperatorActionRealerted(
  userId: string,
  payload: ParcelPendingOperatorActionRealertedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PARCEL_IN_TRANSIT,
    title: 'Nhắc xử lý đơn gửi hàng',
    body: `${formatParcelLabel(payload.parcelCode)} vẫn đang chờ nhà xe xử lý thủ công.`,
    data: buildNotificationData(payload),
  };
}

async function mapParcelReliabilityIncident(
  payload: z.infer<typeof ParcelReliabilityIncidentPayloadSchema>,
  opened: boolean,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
  resolveParcelSnapshot?: (parcelId: string) => Promise<ParcelRecipientSnapshot>,
): Promise<CreateNotificationDto[]> {
  if (!resolveParcelSnapshot) throw new Error('PARCEL_RECIPIENT_PROVIDER_REQUIRED');
  const snapshot = await resolveParcelSnapshot(payload.parcelId);
  const operatorId = payload.operatorId ?? snapshot.operatorId;
  const operatorRecipients = await resolveOperatorRecipientUserIds(operatorId);
  const parcelRecipients = [snapshot.senderUserId, snapshot.recipientUserId].filter(isString);
  const status = payload.status?.toUpperCase();
  const title = opened
    ? 'Đã mở tìm kiếm hàng hóa'
    : status === 'FOUND'
      ? 'Đã tìm thấy hàng hóa'
      : status === 'FORWARDING'
        ? 'Hàng đang được chuyển về đúng điểm nhận'
        : status === 'LOST_CONFIRMED'
          ? 'Đã xác nhận thất lạc hàng hóa'
          : 'Cập nhật tìm kiếm hàng hóa';
  const body = opened
    ? 'VietRide và nhà xe đang truy tìm kiện hàng từ vị trí xác nhận gần nhất.'
    : status === 'FOUND'
      ? 'Kiện hàng đã được tìm thấy và đang chờ phương án giao tiếp theo.'
      : status === 'FORWARDING'
        ? 'Kiện hàng đang được chuyển bằng chuyến mới đến đúng điểm nhận.'
        : status === 'LOST_CONFIRMED'
          ? 'Quá trình tìm kiếm đã kết thúc và người gửi có thể hoàn tất hồ sơ bồi thường.'
          : 'Hồ sơ tìm kiếm kiện hàng vừa có cập nhật mới.';

  return [...new Set([...parcelRecipients, ...operatorRecipients])].map((userId) => ({
    userId,
    type: NotificationType.INCIDENT_REPORTED,
    title,
    body,
    data: buildNotificationData({ ...payload, operatorId }),
  }));
}

async function mapParcelClaimSubmitted(
  payload: z.infer<typeof ParcelClaimPayloadSchema>,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
): Promise<CreateNotificationDto[]> {
  const operatorRecipients = await resolveOperatorRecipientUserIds(payload.operatorId);
  return [...new Set([payload.beneficiaryUserId, ...operatorRecipients])].map((userId) => ({
    userId,
    type: NotificationType.INCIDENT_REPORTED,
    title:
      userId === payload.beneficiaryUserId
        ? 'Đã tiếp nhận yêu cầu bồi thường'
        : 'Có yêu cầu bồi thường hàng hóa mới',
    body:
      userId === payload.beneficiaryUserId
        ? 'Hồ sơ bồi thường đã được tiếp nhận và đang chờ nhà xe xem xét.'
        : 'Một hồ sơ bồi thường mới cần được kiểm tra chứng từ và quyết định.',
    data: buildNotificationData(payload),
  }));
}

function mapParcelClaimDecided(
  payload: z.infer<typeof ParcelClaimPayloadSchema>,
): CreateNotificationDto {
  const approved = payload.status?.toUpperCase() === 'APPROVED';
  return {
    userId: payload.beneficiaryUserId,
    type: NotificationType.INCIDENT_REPORTED,
    title: approved ? 'Yêu cầu bồi thường đã được duyệt' : 'Yêu cầu bồi thường bị từ chối',
    body: approved
      ? `Tổng số tiền được duyệt là ${formatMoney(payload.totalAwardVnd)} VND và đang chờ chi trả.`
      : 'Nhà xe đã từ chối yêu cầu bồi thường. Vui lòng xem lý do trong chi tiết hồ sơ.',
    data: buildNotificationData(payload),
  };
}

function mapParcelCompensationFundingPending(
  payload: z.infer<typeof ParcelCompensationPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.beneficiaryUserId,
    type: NotificationType.INCIDENT_REPORTED,
    title: 'Khoản bồi thường đang chờ nguồn tiền',
    body: `Khoản bồi thường ${formatMoney(payload.amountVnd)} VND đã được duyệt và sẽ tự động chi trả khi nhà xe bổ sung đủ nguồn.`,
    data: buildNotificationData(payload),
  };
}

function mapParcelCompensationPaid(
  payload: z.infer<typeof ParcelCompensationPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.beneficiaryUserId,
    type: NotificationType.WALLET_CREDITED,
    title: 'Đã chi trả tiền bồi thường',
    body: `${formatMoney(payload.amountVnd)} VND đã được cộng vào ví VietRide của người gửi.`,
    data: buildNotificationData(payload),
  };
}

function mapTripStopArrived(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_VEHICLE_APPROACHING,
    title: 'Xe đã đến điểm dừng',
    body: 'Chuyến xe đã ghi nhận đến điểm dừng.',
    data: buildNotificationData(payload),
  };
}

async function mapTripVehicleSubstitutedEvent(
  payload: z.infer<typeof TripVehicleSubstitutedPayloadSchema>,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
): Promise<CreateNotificationDto[]> {
  const operatorId = payload.operatorId;
  if (!operatorId) {
    throw new z.ZodError([
      {
        code: z.ZodIssueCode.custom,
        message: 'operatorId is required',
        path: ['operatorId'],
      },
    ]);
  }
  const operatorRecipients = await resolveOperatorRecipientUserIds(operatorId);
  const crewRecipients = [payload.newDriverId, payload.newAssistantId].filter(isString);
  const userIds = [...new Set([...operatorRecipients, ...crewRecipients])];
  return userIds.map((userId) => {
    const isReplacementCrew = crewRecipients.includes(userId);
    const location =
      typeof payload.incidentLatitude === 'number' &&
      typeof payload.incidentLongitude === 'number'
        ? ` Tọa độ sự cố: ${payload.incidentLatitude}, ${payload.incidentLongitude}.`
        : payload.incidentDescription
          ? ` Vị trí sự cố: ${payload.incidentDescription}.`
          : '';
    return {
      userId,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: isReplacementCrew ? 'Bạn được gán xe thay thế' : 'Đã thay xe cho chuyến',
      body: isReplacementCrew
        ? `Bạn được gán vào chuyến thay thế và cần đến điểm sự cố để nhận hàng.${location}`
        : `Chuyến xe đã được gán xe thay thế.${payload.reason ? ` Lý do: ${formatDisplayReason(payload.reason)}.` : ''}`,
      data: buildNotificationData(payload),
    };
  });
}

function mapSubscriptionLimit(
  userId: string,
  payload: z.infer<typeof SubscriptionLimitPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
    title: 'Vượt giới hạn gói dịch vụ',
    body: `${formatOperatorLabel(payload.operatorName)} đã chạm giới hạn gói${payload.planName ? ` ${payload.planName}` : ''}.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionTrial(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_TRIAL_EXPIRING,
    title: 'Gói dùng thử sắp hết hạn',
    body: `${formatOperatorLabel(payload.operatorName)} sắp hết thời gian dùng thử.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionExpired(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_EXPIRED,
    title: 'Gói dịch vụ đã hết hạn',
    body: `${formatOperatorLabel(payload.operatorName)} đã hết hạn gói dịch vụ.`,
    data: buildNotificationData(payload),
  };
}

function mapSubscriptionApproved(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.SUBSCRIPTION_APPROVED,
    title: 'Gói dịch vụ đã được duyệt',
    body: `${formatOperatorLabel(payload.operatorName)} đã được kích hoạt gói dịch vụ.`,
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
    body: `${formatOperatorLabel(payload.operatorName)} có thanh toán gói dịch vụ sắp đến hạn${payload.dueDate ? ` vào ${formatVietnamDateTime(payload.dueDate)}` : ''}.`,
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
    body: `${formatOperatorLabel(payload.operatorName)} đã được hoàn về gói trước đó.`,
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
    body: `Đã tất toán ${formatMoney(payload.netAmount)} VND từ chuyến xe vào ví nhà xe.`,
    data: buildNotificationData(payload),
  };
}

function mapPayoutProcessed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_PROCESSED,
    title: 'Lệnh chi trả đã xử lý',
    body: 'Lệnh chi trả đã được xử lý thành công.',
    data: buildNotificationData(payload),
  };
}

function mapPayoutFailed(userId: string, payload: OperatorPayload): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.PAYOUT_FAILED,
    title: 'Lệnh chi trả thất bại',
    body: `Lệnh chi trả xử lý thất bại.${payload.reason ? ` Lý do: ${formatDisplayReason(payload.reason)}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

function mapDirectParcelUser<TPayload extends { userId: string }>(
  payload: TPayload,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): CreateNotificationDto[] {
  return [mapper(payload.userId, payload)];
}

async function mapParcelSenderEvent<TPayload extends ParcelPayload>(
  payload: TPayload,
  resolveParcelSnapshot: ((parcelId: string) => Promise<ParcelRecipientSnapshot>) | undefined,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): Promise<CreateNotificationDto[]> {
  const directSender = payload.userId ?? payload.senderUserId;
  if (directSender) return [mapper(directSender, payload)];

  const snapshot = await requireParcelSnapshot(payload.parcelId, resolveParcelSnapshot);
  return [mapper(snapshot.senderUserId, enrichParcelPayload(payload, snapshot))];
}

async function mapParcelSnapshotSenderEvent<TPayload extends ParcelPayload>(
  payload: TPayload,
  resolveParcelSnapshot: ((parcelId: string) => Promise<ParcelRecipientSnapshot>) | undefined,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): Promise<CreateNotificationDto[]> {
  const snapshot = await requireParcelSnapshot(payload.parcelId, resolveParcelSnapshot);
  return [mapper(snapshot.senderUserId, enrichParcelPayload(payload, snapshot))];
}

async function mapParcelOperatorEvent<TPayload extends ParcelPayload>(
  payload: TPayload,
  resolveOperatorRecipientUserIds: (operatorId: string) => Promise<string[]>,
  resolveParcelSnapshot: ((parcelId: string) => Promise<ParcelRecipientSnapshot>) | undefined,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): Promise<CreateNotificationDto[]> {
  let operatorId = payload.operatorId;
  let mappedPayload = payload;
  if (!operatorId) {
    const snapshot = await requireParcelSnapshot(payload.parcelId, resolveParcelSnapshot);
    operatorId = snapshot.operatorId;
    mappedPayload = enrichParcelPayload(payload, snapshot);
  }
  const recipients = await resolveOperatorRecipientUserIds(operatorId);
  return [...new Set(recipients)].map((userId) => mapper(userId, mappedPayload));
}

async function requireParcelSnapshot(
  parcelId: string,
  resolveParcelSnapshot: ((parcelId: string) => Promise<ParcelRecipientSnapshot>) | undefined,
): Promise<ParcelRecipientSnapshot> {
  if (!resolveParcelSnapshot) throw new Error('PARCEL_RECIPIENT_PROVIDER_NOT_CONFIGURED');
  return resolveParcelSnapshot(parcelId);
}

function enrichParcelPayload<TPayload extends ParcelPayload>(
  payload: TPayload,
  snapshot: ParcelRecipientSnapshot,
): TPayload {
  return {
    ...payload,
    tripId: payload.tripId ?? snapshot.tripId,
    operatorId: payload.operatorId ?? snapshot.operatorId,
    senderUserId: payload.senderUserId ?? snapshot.senderUserId,
    ...(payload.recipientUserId
      ? { recipientUserId: payload.recipientUserId }
      : snapshot.recipientUserId
        ? { recipientUserId: snapshot.recipientUserId }
        : {}),
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
    body: `${formatParcelLabel(payload.parcelCode)} ${actionText}${payload.reason ? ` Lý do: ${formatDisplayReason(payload.reason)}.` : ''}`,
    data: buildNotificationData(payload),
  };
}

function formatMoney(amount: z.infer<typeof MoneyAmountSchema>): string {
  return amount === undefined ? '0' : amount.toString();
}

function buildNotificationData(
  payload: RecipientPayload & Record<string, unknown>,
): Record<string, unknown> {
  const { userId, userIds, senderUserId, recipientUserId, recipientUserIds, ...data } = payload;
  void userId;
  void userIds;
  void senderUserId;
  void recipientUserId;
  void recipientUserIds;
  return data;
}

function isString(value: unknown): value is string {
  return typeof value === 'string';
}
