ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_CANCELLED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_PICKED_UP';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_DELIVERED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_NO_SHOW';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_COMPLETED';
