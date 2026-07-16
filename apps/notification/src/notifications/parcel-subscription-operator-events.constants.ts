export const OPERATOR_RECIPIENT_PROVIDER = Symbol('OPERATOR_RECIPIENT_PROVIDER');

export const BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY = 'booking.voucher.consent_accepted';
export const BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY = 'booking.voucher.consent_rejected';

export const PARCEL_CREATED_ROUTING_KEY = 'parcel.parcel.created';
export const PARCEL_LOADED_ROUTING_KEY = 'parcel.parcel.loaded';
export const PARCEL_UNLOADED_ROUTING_KEY = 'parcel.parcel.unloaded';
export const PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY =
  'parcel.parcel.delivered_pending_confirm';
export const PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY = 'parcel.parcel.delivery_confirmed';
export const PARCEL_DELIVERY_REJECTED_ROUTING_KEY = 'parcel.parcel.delivery_rejected';
export const PARCEL_CANCELLED_ROUTING_KEY = 'parcel.parcel.cancelled';
export const PARCEL_REJECTED_ROUTING_KEY = 'parcel.parcel.rejected';
export const PARCEL_RETURNED_ROUTING_KEY = 'parcel.parcel.returned';
export const PARCEL_AUTO_REJECTED_ROUTING_KEY = 'parcel.parcel.auto_rejected';
export const PARCEL_REVIEW_REQUESTED_ROUTING_KEY = 'parcel.parcel.review_requested';
export const PARCEL_TRANSFER_INITIATED_ROUTING_KEY = 'parcel.parcel.transfer_initiated';
export const PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY = 'parcel.parcel.transfer_confirmed';
export const PARCEL_TRANSFER_ESCALATED_ROUTING_KEY = 'parcel.parcel.transfer_escalated';
export const PARCEL_RETURN_INITIATED_ROUTING_KEY = 'parcel.parcel.return_initiated';
export const PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY = 'parcel.parcel.pending_operator_action';
export const TRIP_STOP_ARRIVED_ROUTING_KEY = 'trip.stop.arrived';
export const TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY = 'trip.vehicle_substituted';

export const SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY = 'subscription.limit.trip_skipped';
export const SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY = 'subscription.subscription.trial_expiring';
export const SUBSCRIPTION_EXPIRED_ROUTING_KEY = 'subscription.subscription.expired';
export const SUBSCRIPTION_APPROVED_ROUTING_KEY = 'subscription.subscription.approved';
export const SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY =
  'payment.subscription.payment_pending_warn';
export const SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY =
  'payment.subscription.payment_auto_reverted';
export const INVOICE_ISSUED_ROUTING_KEY = 'payment.invoice.issued';
export const TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY = 'payment.trip_settlement.completed';
export const PAYOUT_PROCESSED_ROUTING_KEY = 'payment.payout.processed';
export const PAYOUT_FAILED_ROUTING_KEY = 'payment.payout.failed';

export const PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS = [
  {
    queue: 'notification:booking-voucher-consent-accepted',
    routingKey: BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-voucher-consent-rejected',
    routingKey: BOOKING_VOUCHER_CONSENT_REJECTED_ROUTING_KEY,
  },
  { queue: 'notification:parcel-created', routingKey: PARCEL_CREATED_ROUTING_KEY },
  { queue: 'notification:parcel-loaded', routingKey: PARCEL_LOADED_ROUTING_KEY },
  { queue: 'notification:parcel-unloaded', routingKey: PARCEL_UNLOADED_ROUTING_KEY },
  {
    queue: 'notification:parcel-delivered-pending-confirm',
    routingKey: PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-delivery-confirmed',
    routingKey: PARCEL_DELIVERY_CONFIRMED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-delivery-rejected',
    routingKey: PARCEL_DELIVERY_REJECTED_ROUTING_KEY,
  },
  { queue: 'notification:parcel-cancelled', routingKey: PARCEL_CANCELLED_ROUTING_KEY },
  { queue: 'notification:parcel-rejected', routingKey: PARCEL_REJECTED_ROUTING_KEY },
  { queue: 'notification:parcel-returned', routingKey: PARCEL_RETURNED_ROUTING_KEY },
  { queue: 'notification:parcel-auto-rejected', routingKey: PARCEL_AUTO_REJECTED_ROUTING_KEY },
  {
    queue: 'notification:parcel-review-requested',
    routingKey: PARCEL_REVIEW_REQUESTED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-transfer-initiated',
    routingKey: PARCEL_TRANSFER_INITIATED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-transfer-confirmed',
    routingKey: PARCEL_TRANSFER_CONFIRMED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-transfer-escalated',
    routingKey: PARCEL_TRANSFER_ESCALATED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-return-initiated',
    routingKey: PARCEL_RETURN_INITIATED_ROUTING_KEY,
  },
  {
    queue: 'notification:parcel-pending-operator-action',
    routingKey: PARCEL_PENDING_OPERATOR_ACTION_ROUTING_KEY,
  },
  { queue: 'notification:trip-stop-arrived', routingKey: TRIP_STOP_ARRIVED_ROUTING_KEY },
  {
    queue: 'notification:trip-vehicle-substituted',
    routingKey: TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
  },
  {
    queue: 'notification:subscription-limit-trip-skipped',
    routingKey: SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
  },
  {
    queue: 'notification:subscription-trial-expiring',
    routingKey: SUBSCRIPTION_TRIAL_EXPIRING_ROUTING_KEY,
  },
  { queue: 'notification:subscription-expired', routingKey: SUBSCRIPTION_EXPIRED_ROUTING_KEY },
  { queue: 'notification:subscription-approved', routingKey: SUBSCRIPTION_APPROVED_ROUTING_KEY },
  {
    queue: 'notification:subscription-payment-pending-warn',
    routingKey: SUBSCRIPTION_PAYMENT_PENDING_WARN_ROUTING_KEY,
  },
  {
    queue: 'notification:subscription-payment-auto-reverted',
    routingKey: SUBSCRIPTION_PAYMENT_AUTO_REVERTED_ROUTING_KEY,
  },
  { queue: 'notification:invoice-issued', routingKey: INVOICE_ISSUED_ROUTING_KEY },
  {
    queue: 'notification:trip-settlement-completed',
    routingKey: TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
  },
  { queue: 'notification:payout-processed', routingKey: PAYOUT_PROCESSED_ROUTING_KEY },
  { queue: 'notification:payout-failed', routingKey: PAYOUT_FAILED_ROUTING_KEY },
] as const;
