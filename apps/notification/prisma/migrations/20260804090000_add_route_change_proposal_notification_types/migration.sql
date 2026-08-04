ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'ROUTE_CHANGE_PROPOSAL_CREATED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'ROUTE_CHANGE_PROPOSAL_APPROVED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'ROUTE_CHANGE_PROPOSAL_REJECTED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'ROUTE_CHANGE_PROPOSAL_SUPERSEDED';
ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'ROUTE_CHANGE_PROPOSAL_EXPIRED';
