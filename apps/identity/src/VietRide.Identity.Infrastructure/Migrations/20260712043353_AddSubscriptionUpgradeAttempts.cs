using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Persistence.Outbox;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUpgradeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The baseline already owns its enum annotations. Only create the two Day 37 enums.
            migrationBuilder.Sql("DO $$ BEGIN CREATE TYPE subscription_billing_period AS ENUM ('MONTHLY', 'YEARLY'); EXCEPTION WHEN duplicate_object THEN NULL; END $$;");
            migrationBuilder.Sql("DO $$ BEGIN CREATE TYPE subscription_upgrade_attempt_status AS ENUM ('INITIATED', 'PAYMENT_PENDING', 'SUCCEEDED', 'EXPIRED', 'FAILED'); EXCEPTION WHEN duplicate_object THEN NULL; END $$;");

            if (migrationBuilder.ActiveProvider == "__legacy_generated_enum_annotations__")
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
                    .OldAnnotation("Npgsql:Enum:subscription_payment_method", "VNPAY")
                    .OldAnnotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .OldAnnotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .OldAnnotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED");
            }

            migrationBuilder.AddColumn<SubscriptionBillingPeriod>(
                name: "billing_period",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "subscription_billing_period",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_upgrade_attempts",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_period = table.Column<SubscriptionBillingPeriod>(type: "subscription_billing_period", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<SubscriptionUpgradeAttemptStatus>(type: "subscription_upgrade_attempt_status", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    warn_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_upgrade_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_upgrade_attempts_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_upgrade_attempts_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operator_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_upgrade_attempts_target_plan_id",
                        column: x => x.target_plan_id,
                        principalSchema: "vietride_identity",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_subscription_upgrade_attempts_status_due_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_upgrade_attempts_operator_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_upgrade_attempts_subscription_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_upgrade_attempts_target_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "target_plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_upgrade_attempts_idempotency_key",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_subscription_upgrade_attempts_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "payment_id",
                unique: true,
                filter: "payment_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_upgrade_attempts",
                schema: "vietride_identity");

            migrationBuilder.DropColumn(
                name: "billing_period",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            if (migrationBuilder.ActiveProvider == "__legacy_generated_enum_annotations__")
            {
                migrationBuilder.AlterDatabase()
                    .Annotation("Npgsql:Enum:activity_log_action", "LOGIN,LOGOUT,BOOK_TICKET,CANCEL_TICKET,UPDATE_PROFILE,CHANGE_PASSWORD,COMPLETE_PROFILE,CREATE_OPERATOR,APPROVE_OPERATOR,REJECT_OPERATOR,LOCK_USER,UNLOCK_USER,VEHICLE_SUBSTITUTION_TRIGGERED,DRIVER_SCHEDULE_EDIT,VEHICLE_SWAP,TRIP_COMPLETED_MANUAL,PARCEL_UNLOAD_OVERRIDE,PARCEL_DELIVERY_RESEND,PARCEL_MANUAL_CONFIRM,TRIP_SETTLEMENT_MANUAL,OPERATOR_WALLET_ADJUSTMENT,SET_INITIAL_PASSWORD,RESEND_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:device_platform", "IOS,ANDROID,WEB")
                    .Annotation("Npgsql:Enum:email_verification_purpose", "REGISTRATION,PASSWORD_RESET,SET_INITIAL_PASSWORD")
                    .Annotation("Npgsql:Enum:oauth_provider", "GOOGLE")
                    .Annotation("Npgsql:Enum:operator_registration_status", "PENDING,APPROVED,REJECTED,SUSPENDED")
                    .Annotation("Npgsql:Enum:refresh_token_revoke_reason", "NORMAL_ROTATION,REUSE_DETECTED,USER_LOGOUT,ADMIN_REVOKE,PASSWORD_RESET")
                    .Annotation("Npgsql:Enum:subscription_payment_method", "VNPAY")
                    .Annotation("Npgsql:Enum:subscription_status", "PENDING_APPROVAL,ACTIVE,EXPIRED,CANCELLED,PENDING_PAYMENT")
                    .Annotation("Npgsql:Enum:user_role", "PASSENGER,DRIVER,ASSISTANT,OPERATOR_STAFF,OPERATOR_ADMIN,SYSTEM_ADMIN")
                    .Annotation("Npgsql:Enum:user_status", "PENDING_EMAIL_VERIFICATION,PENDING_INITIAL_PASSWORD,ACTIVE,LOCKED,DELETED")
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

            migrationBuilder.Sql("DROP TYPE IF EXISTS subscription_upgrade_attempt_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS subscription_billing_period;");
        }
    }
}
