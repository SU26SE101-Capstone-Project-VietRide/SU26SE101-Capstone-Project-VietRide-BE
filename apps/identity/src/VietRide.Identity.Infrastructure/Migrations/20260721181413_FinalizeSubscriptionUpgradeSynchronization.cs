using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeSubscriptionUpgradeSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_operator_subscriptions_previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            migrationBuilder.DropIndex(
                name: "uq_subscription_upgrade_attempts_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropIndex(
                name: "idx_operator_subscriptions_previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            migrationBuilder.Sql("""
                UPDATE vietride_identity.operator_subscriptions
                SET plan_id = COALESCE(previous_active_plan_id, '00000000-0000-0000-0000-000000000001'::uuid)
                WHERE status = 'PENDING_PAYMENT'::vietride_identity.subscription_status;
                """);

            migrationBuilder.DropColumn(
                name: "warn_sent_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            migrationBuilder.RenameColumn(
                name: "payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                newName: "latest_payment_id");

            migrationBuilder.RenameColumn(
                name: "plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                newName: "active_plan_id");

            migrationBuilder.RenameIndex(
                name: "idx_operator_subscriptions_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                newName: "idx_operator_subscriptions_active_plan_id");

            migrationBuilder.CreateIndex(
                name: "idx_subscription_upgrade_attempts_latest_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "latest_payment_id",
                filter: "latest_payment_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_subscription_upgrade_attempts_latest_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.RenameColumn(
                name: "latest_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                newName: "payment_id");

            migrationBuilder.RenameColumn(
                name: "active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                newName: "plan_id");

            migrationBuilder.RenameIndex(
                name: "idx_operator_subscriptions_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                newName: "idx_operator_subscriptions_plan_id");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "warn_sent_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "uuid",
                nullable: true,
                comment: "Plan ACTIVE before PENDING_PAYMENT; used by revert flow if payment times out after 7 days.");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_upgrade_attempts_payment_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "payment_id",
                unique: true,
                filter: "payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_subscriptions_previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "previous_active_plan_id",
                filter: "previous_active_plan_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_operator_subscriptions_previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "previous_active_plan_id",
                principalSchema: "vietride_identity",
                principalTable: "subscription_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
