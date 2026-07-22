using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RedesignSubscriptionUpgradeSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fallback_policy",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "RESTORE_CURRENT");

            migrationBuilder.AddColumn<string>(
                name: "latest_payment_status",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NONE");

            migrationBuilder.AddColumn<int>(
                name: "payment_session_version",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE vietride_identity.subscription_upgrade_attempts
                SET latest_payment_status = CASE status::text
                        WHEN 'SUCCEEDED' THEN 'SUCCEEDED'
                        WHEN 'FAILED' THEN 'FAILED'
                        WHEN 'EXPIRED' THEN 'EXPIRED'
                        WHEN 'PAYMENT_PENDING' THEN CASE WHEN payment_id IS NULL THEN 'NONE' ELSE 'PENDING' END
                        ELSE 'NONE'
                    END,
                    payment_session_version = CASE WHEN payment_id IS NULL THEN 0 ELSE 1 END,
                    due_at = LEAST(due_at, created_at + INTERVAL '15 minutes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fallback_policy",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "latest_payment_status",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "payment_session_version",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");
        }
    }
}
