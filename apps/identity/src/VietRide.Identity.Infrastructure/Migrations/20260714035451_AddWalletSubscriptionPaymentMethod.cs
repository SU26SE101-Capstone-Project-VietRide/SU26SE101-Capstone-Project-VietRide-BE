using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletSubscriptionPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE public.subscription_payment_method ADD VALUE IF NOT EXISTS 'WALLET' AFTER 'VNPAY';",
                suppressTransaction: true);

            if (migrationBuilder.ActiveProvider == "__enum_annotation_metadata_only__")
            {
                migrationBuilder.AlterDatabase()
                    .Annotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                    .Annotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                    .Annotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                    .Annotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET")
                    .Annotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                    .Annotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                    .Annotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .Annotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                    .Annotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .Annotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                    .Annotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                    .OldAnnotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD")
                    .OldAnnotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                    .OldAnnotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                    .OldAnnotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                    .OldAnnotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                    .OldAnnotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET")
                    .OldAnnotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                    .OldAnnotation("Npgsql:Enum:subscription_payment_method", "VNPAY")
                    .OldAnnotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .OldAnnotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                    .OldAnnotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .OldAnnotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                    .OldAnnotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_identity.operator_subscriptions
                        WHERE payment_method::text = 'WALLET') THEN
                        RAISE EXCEPTION 'Cannot remove WALLET from subscription_payment_method while WALLET rows exist.';
                    END IF;
                END $$;

                ALTER TABLE vietride_identity.operator_subscriptions
                    ALTER COLUMN payment_method TYPE text USING payment_method::text;
                DROP TYPE public.subscription_payment_method;
                CREATE TYPE public.subscription_payment_method AS ENUM ('VNPAY');
                ALTER TABLE vietride_identity.operator_subscriptions
                    ALTER COLUMN payment_method TYPE public.subscription_payment_method
                    USING payment_method::public.subscription_payment_method;
                """);

            if (migrationBuilder.ActiveProvider == "__enum_annotation_metadata_only__")
            {
                migrationBuilder.AlterDatabase()
                    .Annotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                    .Annotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                    .Annotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                    .Annotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET")
                    .Annotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                    .Annotation("Npgsql:Enum:subscription_payment_method", "VNPAY")
                    .Annotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .Annotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                    .Annotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .Annotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                    .Annotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                    .OldAnnotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD")
                    .OldAnnotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                    .OldAnnotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                    .OldAnnotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                    .OldAnnotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                    .OldAnnotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET")
                    .OldAnnotation("Npgsql:Enum:subscription_billing_period", "MONTHLY,YEARLY")
                    .OldAnnotation("Npgsql:Enum:subscription_payment_method", "VNPAY,WALLET")
                    .OldAnnotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .OldAnnotation("Npgsql:Enum:subscription_upgrade_attempt_status", "INITIATED,PAYMENT_PENDING,SUCCEEDED,EXPIRED,FAILED")
                    .OldAnnotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .OldAnnotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
                    .OldAnnotation("Npgsql:Enum:vietride_identity.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");
            }
        }
    }
}
