ALTER TYPE vietride_notification.notification_type ADD VALUE IF NOT EXISTS 'TRIP_ASSIGNED';
ALTER TYPE vietride_notification.notification_type ADD VALUE IF NOT EXISTS 'TRIP_ASSIGNMENT_REMOVED';
ALTER TYPE vietride_notification.notification_type ADD VALUE IF NOT EXISTS 'OPERATOR_ANNOUNCEMENT';
ALTER TYPE vietride_notification.notification_delivery_status ADD VALUE IF NOT EXISTS 'VALIDATED';

ALTER TABLE vietride_notification.notification_deliveries
    ADD COLUMN IF NOT EXISTS provider_message_id VARCHAR(255) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS notification_deliveries_notification_id_fcm_token_key
    ON vietride_notification.notification_deliveries (notification_id, fcm_token);