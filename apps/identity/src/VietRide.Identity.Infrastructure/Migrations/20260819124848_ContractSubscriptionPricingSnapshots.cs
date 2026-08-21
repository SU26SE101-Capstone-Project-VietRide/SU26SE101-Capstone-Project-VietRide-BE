using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractSubscriptionPricingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_identity.operator_subscriptions
                        WHERE cycle_price_amount IS NULL)
                    OR EXISTS (
                        SELECT 1
                        FROM vietride_identity.subscription_upgrade_attempts
                        WHERE source_plan_id IS NULL
                           OR quoted_at IS NULL
                           OR period_from IS NULL
                           OR period_to IS NULL
                           OR current_cycle_price_amount IS NULL
                           OR target_cycle_price_amount IS NULL
                           OR unused_credit_amount IS NULL
                           OR prorated_target_amount IS NULL
                           OR is_prorated IS NULL)
                    THEN
                        RAISE EXCEPTION 'Subscription pricing snapshot contract requires zero NULL rows';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "unused_credit_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "target_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "quoted_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "prorated_target_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "period_to",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "period_from",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_prorated",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "current_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "cycle_price_amount",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "unused_credit_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "target_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "quoted_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "prorated_target_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "period_to",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "period_from",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<bool>(
                name: "is_prorated",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<long>(
                name: "current_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "cycle_price_amount",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
