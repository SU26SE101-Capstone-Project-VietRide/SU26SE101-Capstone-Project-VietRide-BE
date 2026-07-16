using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveSubscriptionUpgradeAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscription_upgrade_attempts_subscription_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_upgrade_attempts_active_subscription",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "subscription_id",
                unique: true,
                filter: "status IN ('INITIATED', 'PAYMENT_PENDING')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_subscription_upgrade_attempts_active_subscription",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_upgrade_attempts_subscription_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "subscription_id");
        }
    }
}
