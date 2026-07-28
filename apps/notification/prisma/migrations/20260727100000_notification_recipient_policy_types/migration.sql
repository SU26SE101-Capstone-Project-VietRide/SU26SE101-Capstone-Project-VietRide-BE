ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'PARCEL_REVIEW_APPROVED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'PARCEL_FINAL_PAYMENT_REQUIRED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'PARCEL_SETTLEMENT_RECOVERED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'OPERATOR_REGISTRATION_SUBMITTED';
