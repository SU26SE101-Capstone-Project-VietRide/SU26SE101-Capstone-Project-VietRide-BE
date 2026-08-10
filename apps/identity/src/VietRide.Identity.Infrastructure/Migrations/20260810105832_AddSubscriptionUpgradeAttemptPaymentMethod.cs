using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Identity.Domain.Enums;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUpgradeAttemptPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SubscriptionPaymentMethod>(
                name: "payment_method",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "subscription_payment_method",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE vietride_identity.subscription_upgrade_attempts AS attempt
                SET payment_method = COALESCE(subscription.payment_method, 'VNPAY'::subscription_payment_method)
                FROM vietride_identity.operator_subscriptions AS subscription
                WHERE subscription.id = attempt.subscription_id;
                """);

            migrationBuilder.AlterColumn<SubscriptionPaymentMethod>(
                name: "payment_method",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "subscription_payment_method",
                nullable: false,
                oldClrType: typeof(SubscriptionPaymentMethod),
                oldType: "subscription_payment_method",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");
        }
    }
}
