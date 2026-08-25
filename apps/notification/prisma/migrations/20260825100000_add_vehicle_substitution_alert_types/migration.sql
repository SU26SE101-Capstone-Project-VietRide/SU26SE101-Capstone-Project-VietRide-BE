ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'VEHICLE_SUBSTITUTION_SEAT_SHORTAGE';

ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'BOOKING_TRANSFER_ESCALATED';
