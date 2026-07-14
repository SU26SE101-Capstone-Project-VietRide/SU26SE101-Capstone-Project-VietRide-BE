ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_ASSIGNED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_UNFULFILLED';
ALTER TYPE "vietride_notification"."notification_type" ADD VALUE IF NOT EXISTS 'SHUTTLE_WARNING';
