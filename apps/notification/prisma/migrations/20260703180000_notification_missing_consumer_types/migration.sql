ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'STOP_DISABLED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'VOUCHER_CONSENT_ACCEPTED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'VOUCHER_CONSENT_REJECTED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SUBSCRIPTION_PAYMENT_PENDING_WARN';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SUBSCRIPTION_PAYMENT_AUTO_REVERTED';
