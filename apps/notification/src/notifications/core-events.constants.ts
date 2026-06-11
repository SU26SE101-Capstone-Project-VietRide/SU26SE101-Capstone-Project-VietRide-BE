export const RABBITMQ_IDEMPOTENCY_TTL_SECONDS = 86_400;
export const RABBITMQ_PROCESSING_LOCK_TTL_SECONDS = 300;
export const RABBITMQ_PREFETCH_ONE = 1;

export const BOOKING_CONFIRMED_ROUTING_KEY = 'booking.booking.confirmed';
export const BOOKING_CANCELLED_ROUTING_KEY = 'booking.booking.cancelled';
export const BOOKING_REFUNDED_ROUTING_KEY = 'booking.booking.refunded';
export const WALLET_CREDITED_ROUTING_KEY = 'payment.wallet.credited';
export const WALLET_DEBITED_ROUTING_KEY = 'payment.wallet.debited';

export const CORE_EVENT_QUEUE_BINDINGS = [
  {
    queue: 'notification:booking-confirmed',
    routingKey: BOOKING_CONFIRMED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-cancelled',
    routingKey: BOOKING_CANCELLED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-refunded',
    routingKey: BOOKING_REFUNDED_ROUTING_KEY,
  },
  {
    queue: 'notification:wallet-credited',
    routingKey: WALLET_CREDITED_ROUTING_KEY,
  },
  {
    queue: 'notification:wallet-debited',
    routingKey: WALLET_DEBITED_ROUTING_KEY,
  },
] as const;
