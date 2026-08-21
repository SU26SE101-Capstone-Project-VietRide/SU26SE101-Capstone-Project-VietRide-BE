using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Identity.Domain.Enums;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandSubscriptionProrationAndCustomPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'CREATE_SUBSCRIPTION_CUSTOM_REQUEST';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'APPROVE_SUBSCRIPTION_CUSTOM_REQUEST';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'REJECT_SUBSCRIPTION_CUSTOM_REQUEST';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN';",
                suppressTransaction: true);

            migrationBuilder.AddColumn<long>(
                name: "current_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_prorated",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "period_from",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "period_to",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "prorated_target_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quoted_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "target_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "unused_credit_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_type",
                schema: "vietride_identity",
                table: "subscription_plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "STANDARD");

            migrationBuilder.AddColumn<Guid>(
                name: "source_custom_request_id",
                schema: "vietride_identity",
                table: "subscription_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cycle_price_amount",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    affected_rows integer;
                BEGIN
                    LOOP
                        WITH batch AS (
                            SELECT attempt.id
                            FROM vietride_identity.subscription_upgrade_attempts AS attempt
                            WHERE attempt.source_plan_id IS NULL
                               OR attempt.quoted_at IS NULL
                               OR attempt.period_from IS NULL
                               OR attempt.period_to IS NULL
                               OR attempt.current_cycle_price_amount IS NULL
                               OR attempt.target_cycle_price_amount IS NULL
                               OR attempt.unused_credit_amount IS NULL
                               OR attempt.prorated_target_amount IS NULL
                               OR attempt.is_prorated IS NULL
                            ORDER BY attempt.created_at, attempt.id
                            LIMIT 500
                            FOR UPDATE SKIP LOCKED
                        )
                        UPDATE vietride_identity.subscription_upgrade_attempts AS attempt
                        SET source_plan_id = subscription.active_plan_id,
                            quoted_at = attempt.created_at,
                            period_from = attempt.created_at,
                            period_to = CASE
                                WHEN attempt.billing_period = 'MONTHLY'
                                    THEN attempt.created_at + INTERVAL '1 month'
                                ELSE attempt.created_at + INTERVAL '1 year'
                            END,
                            current_cycle_price_amount = 0,
                            target_cycle_price_amount = attempt.amount,
                            unused_credit_amount = 0,
                            prorated_target_amount = attempt.amount,
                            is_prorated = FALSE
                        FROM vietride_identity.operator_subscriptions AS subscription, batch
                        WHERE attempt.id = batch.id
                          AND subscription.id = attempt.subscription_id;

                        GET DIAGNOSTICS affected_rows = ROW_COUNT;
                        EXIT WHEN affected_rows = 0;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    affected_rows integer;
                BEGIN
                    LOOP
                        WITH batch AS (
                            SELECT subscription.id
                            FROM vietride_identity.operator_subscriptions AS subscription
                            WHERE subscription.cycle_price_amount IS NULL
                            ORDER BY subscription.id
                            LIMIT 500
                            FOR UPDATE SKIP LOCKED
                        )
                        UPDATE vietride_identity.operator_subscriptions AS subscription
                        SET cycle_price_amount = CASE
                            WHEN subscription.billing_period IS NULL THEN 0
                            ELSE COALESCE(
                                (
                                    SELECT attempt.amount
                                    FROM vietride_identity.subscription_upgrade_attempts AS attempt
                                    WHERE attempt.subscription_id = subscription.id
                                      AND attempt.status = 'SUCCEEDED'
                                      AND attempt.target_plan_id = subscription.active_plan_id
                                      AND attempt.billing_period = subscription.billing_period
                                    ORDER BY attempt.updated_at DESC, attempt.created_at DESC
                                    LIMIT 1
                                ),
                                CASE
                                    WHEN subscription.billing_period = 'MONTHLY' THEN plan.price_per_month
                                    ELSE plan.price_per_year
                                END,
                                0)
                        END
                        FROM vietride_identity.subscription_plans AS plan, batch
                        WHERE subscription.id = batch.id
                          AND plan.id = subscription.active_plan_id;

                        GET DIAGNOSTICS affected_rows = ROW_COUNT;
                        EXIT WHEN affected_rows = 0;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "subscription_custom_requests",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_vehicles = table.Column<int>(type: "integer", nullable: false),
                    max_drivers = table.Column<int>(type: "integer", nullable: false),
                    max_assistants = table.Column<int>(type: "integer", nullable: false),
                    max_operator_users = table.Column<int>(type: "integer", nullable: false),
                    max_routes = table.Column<int>(type: "integer", nullable: false),
                    max_trips_per_month = table.Column<int>(type: "integer", nullable: false),
                    enable_parcel = table.Column<bool>(type: "boolean", nullable: false),
                    enable_shuttle = table.Column<bool>(type: "boolean", nullable: false),
                    enable_rag = table.Column<bool>(type: "boolean", nullable: false),
                    preferred_billing_period = table.Column<SubscriptionBillingPeriod>(type: "subscription_billing_period", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_custom_requests", x => x.id);
                    table.CheckConstraint("chk_subscription_custom_requests_limits_non_negative", "max_vehicles >= 0 AND max_drivers >= 0 AND max_assistants >= 0 AND max_operator_users >= 0 AND max_routes >= 0 AND max_trips_per_month >= 0");
                    table.CheckConstraint("chk_subscription_custom_requests_review_state", "(status = 'PENDING_REVIEW' AND reviewed_by IS NULL AND reviewed_at IS NULL AND rejection_reason IS NULL AND approved_plan_id IS NULL) OR (status = 'APPROVED' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND rejection_reason IS NULL AND approved_plan_id IS NOT NULL) OR (status = 'REJECTED' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND rejection_reason IS NOT NULL AND approved_plan_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_subscription_custom_requests_approved_plan_id",
                        column: x => x.approved_plan_id,
                        principalSchema: "vietride_identity",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_custom_requests_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_custom_requests_reviewed_by",
                        column: x => x.reviewed_by,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_upgrade_attempts_source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "source_plan_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_subscription_upgrade_attempts_quote_amounts",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                sql: "amount > 0 AND current_cycle_price_amount >= 0 AND target_cycle_price_amount >= 0 AND unused_credit_amount >= 0 AND prorated_target_amount >= 0 AND prorated_target_amount = unused_credit_amount + amount");

            migrationBuilder.AddCheckConstraint(
                name: "chk_subscription_upgrade_attempts_quote_period",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                sql: "quoted_at < due_at AND period_from < period_to");

            migrationBuilder.CreateIndex(
                name: "idx_subscription_plans_owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans",
                column: "owner_operator_id",
                filter: "owner_operator_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_plans_source_custom_request_id",
                schema: "vietride_identity",
                table: "subscription_plans",
                column: "source_custom_request_id",
                unique: true,
                filter: "source_custom_request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_subscription_plans_owner_by_type",
                schema: "vietride_identity",
                table: "subscription_plans",
                sql: "(plan_type = 'STANDARD' AND owner_operator_id IS NULL AND source_custom_request_id IS NULL) OR (plan_type = 'CUSTOM' AND owner_operator_id IS NOT NULL AND source_custom_request_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_subscriptions_cycle_price_non_negative",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                sql: "cycle_price_amount >= 0");

            migrationBuilder.CreateIndex(
                name: "idx_subscription_custom_requests_status_created_at",
                schema: "vietride_identity",
                table: "subscription_custom_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_custom_requests_reviewed_by",
                schema: "vietride_identity",
                table: "subscription_custom_requests",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_custom_requests_approved_plan_id",
                schema: "vietride_identity",
                table: "subscription_custom_requests",
                column: "approved_plan_id",
                unique: true,
                filter: "approved_plan_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_subscription_custom_requests_pending_operator",
                schema: "vietride_identity",
                table: "subscription_custom_requests",
                column: "operator_id",
                unique: true,
                filter: "status = 'PENDING_REVIEW'");

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_plans_owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans",
                column: "owner_operator_id",
                principalSchema: "vietride_identity",
                principalTable: "operators",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_upgrade_attempts_source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts",
                column: "source_plan_id",
                principalSchema: "vietride_identity",
                principalTable: "subscription_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscription_plans_owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_upgrade_attempts_source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropTable(
                name: "subscription_custom_requests",
                schema: "vietride_identity");

            migrationBuilder.DropIndex(
                name: "ix_subscription_upgrade_attempts_source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "chk_subscription_upgrade_attempts_quote_amounts",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "chk_subscription_upgrade_attempts_quote_period",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropIndex(
                name: "idx_subscription_plans_owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropIndex(
                name: "uq_subscription_plans_source_custom_request_id",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropCheckConstraint(
                name: "chk_subscription_plans_owner_by_type",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_subscriptions_cycle_price_non_negative",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "is_prorated",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "period_from",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "period_to",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "prorated_target_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "quoted_at",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "source_plan_id",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "target_cycle_price_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "unused_credit_amount",
                schema: "vietride_identity",
                table: "subscription_upgrade_attempts");

            migrationBuilder.DropColumn(
                name: "owner_operator_id",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "plan_type",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "source_custom_request_id",
                schema: "vietride_identity",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "cycle_price_amount",
                schema: "vietride_identity",
                table: "operator_subscriptions");

            // PostgreSQL cannot safely remove enum labels. The added activity-log labels remain
            // reserved after downgrade so the structural rollback and a later reapply stay valid.
        }
    }
}
