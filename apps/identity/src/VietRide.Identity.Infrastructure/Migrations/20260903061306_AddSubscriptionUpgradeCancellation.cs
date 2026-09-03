using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUpgradeCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "__enum_annotation_metadata_only__")
            {
                migrationBuilder.Sql(
                    "ALTER TYPE public.subscription_upgrade_attempt_status ADD VALUE IF NOT EXISTS 'CANCELLED' AFTER 'FAILED';",
                    suppressTransaction: true);
                return;
            }

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,SUSPEND_OPERATOR,REACTIVATE_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD,STATION_MERGED,STATION_NORMALIZED,CREATE_SUBSCRIPTION_CUSTOM_REQUEST,APPROVE_SUBSCRIPTION_CUSTOM_REQUEST,REJECT_SUBSCRIPTION_CUSTOM_REQUEST,DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN")
                .Annotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                .Annotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                .Annotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                .Annotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                .Annotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET,PASSWORD_CHANGE")
                .Annotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                .Annotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                .Annotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                .Annotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED,CANCELLED")
                .Annotation("Npgsql:Enum:user_lock_source", "AUTOMATIC_LOGIN_FAILURE,OPERATOR_ADMIN,SYSTEM_ADMIN,LEGACY_UNKNOWN")
                .Annotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                .Annotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                .Annotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,SUSPEND_OPERATOR,REACTIVATE_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD,STATION_MERGED,STATION_NORMALIZED,CREATE_SUBSCRIPTION_CUSTOM_REQUEST,APPROVE_SUBSCRIPTION_CUSTOM_REQUEST,REJECT_SUBSCRIPTION_CUSTOM_REQUEST,DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN")
                .OldAnnotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                .OldAnnotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                .OldAnnotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                .OldAnnotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET,PASSWORD_CHANGE")
                .OldAnnotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                .OldAnnotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                .OldAnnotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                .OldAnnotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                .OldAnnotation("Npgsql:Enum:user_lock_source", "AUTOMATIC_LOGIN_FAILURE,OPERATOR_ADMIN,SYSTEM_ADMIN,LEGACY_UNKNOWN")
                .OldAnnotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                .OldAnnotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                .OldAnnotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "__enum_annotation_metadata_only__")
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF EXISTS (
                            SELECT 1
                            FROM vietride_identity.subscription_upgrade_attempts
                            WHERE status::text = 'CANCELLED') THEN
                            RAISE EXCEPTION 'Cannot remove CANCELLED from subscription_upgrade_attempt_status while CANCELLED rows exist.';
                        END IF;
                    END $$;

                    DROP INDEX IF EXISTS vietride_identity.uq_subscription_upgrade_attempts_active_subscription;
                    DROP INDEX IF EXISTS vietride_identity.idx_subscription_upgrade_attempts_status_due_at;
                    ALTER TABLE vietride_identity.subscription_upgrade_attempts
                        ALTER COLUMN status TYPE text USING status::text;
                    DROP TYPE public.subscription_upgrade_attempt_status;
                    CREATE TYPE public.subscription_upgrade_attempt_status AS ENUM (
                        'INITIATED', 'PAYMENT_PENDING', 'SUCCEEDED', 'EXPIRED', 'FAILED');
                    ALTER TABLE vietride_identity.subscription_upgrade_attempts
                        ALTER COLUMN status TYPE public.subscription_upgrade_attempt_status
                        USING status::public.subscription_upgrade_attempt_status;
                    CREATE UNIQUE INDEX uq_subscription_upgrade_attempts_active_subscription
                        ON vietride_identity.subscription_upgrade_attempts (subscription_id)
                        WHERE status IN ('INITIATED', 'PAYMENT_PENDING');
                    CREATE INDEX idx_subscription_upgrade_attempts_status_due_at
                        ON vietride_identity.subscription_upgrade_attempts (status, due_at);
                    """);
                return;
            }

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,SUSPEND_OPERATOR,REACTIVATE_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD,STATION_MERGED,STATION_NORMALIZED,CREATE_SUBSCRIPTION_CUSTOM_REQUEST,APPROVE_SUBSCRIPTION_CUSTOM_REQUEST,REJECT_SUBSCRIPTION_CUSTOM_REQUEST,DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN")
                .Annotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                .Annotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                .Annotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                .Annotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                .Annotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET,PASSWORD_CHANGE")
                .Annotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                .Annotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                .Annotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                .Annotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                .Annotation("Npgsql:Enum:user_lock_source", "AUTOMATIC_LOGIN_FAILURE,OPERATOR_ADMIN,SYSTEM_ADMIN,LEGACY_UNKNOWN")
                .Annotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                .Annotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                .Annotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,SUSPEND_OPERATOR,REACTIVATE_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD,STATION_MERGED,STATION_NORMALIZED,CREATE_SUBSCRIPTION_CUSTOM_REQUEST,APPROVE_SUBSCRIPTION_CUSTOM_REQUEST,REJECT_SUBSCRIPTION_CUSTOM_REQUEST,DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN")
                .OldAnnotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                .OldAnnotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                .OldAnnotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                .OldAnnotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET,PASSWORD_CHANGE")
                .OldAnnotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                .OldAnnotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                .OldAnnotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                .OldAnnotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:user_lock_source", "AUTOMATIC_LOGIN_FAILURE,OPERATOR_ADMIN,SYSTEM_ADMIN,LEGACY_UNKNOWN")
                .OldAnnotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                .OldAnnotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                .OldAnnotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");
        }
    }
}
