using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Identity.Domain.Enums;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorSubscriptionBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE TYPE operator_registration_status AS ENUM ('PENDING', 'APPROVED', 'REJECTED', 'SUSPENDED');
                EXCEPTION
                    WHEN duplicate_object THEN NULL;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE TYPE subscription_status AS ENUM ('PENDING_APPROVAL', 'ACTIVE', 'EXPIRED', 'CANCELLED', 'PENDING_PAYMENT');
                EXCEPTION
                    WHEN duplicate_object THEN NULL;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE TYPE subscription_payment_method AS ENUM ('VNPAY');
                EXCEPTION
                    WHEN duplicate_object THEN NULL;
                END $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "address_district",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_province",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_street",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_ward",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                schema: "vietride_identity",
                table: "operators",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_name",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_number",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_registration_number",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_policy",
                schema: "vietride_identity",
                table: "operators",
                type: "jsonb",
                nullable: true,
                comment: "JSONB array of {hoursBeforeDeparture, feePercent}; sorted ascending. NULL = no policy configured.");

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "vietride_identity",
                table: "operators",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                schema: "vietride_identity",
                table: "operators",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "luggage_policy",
                schema: "vietride_identity",
                table: "operators",
                type: "jsonb",
                nullable: true,
                comment: "JSONB {defaultLuggageKgPerSeat}. NULL defaults to {10}.");

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parcel_no_show_policy",
                schema: "vietride_identity",
                table: "operators",
                type: "jsonb",
                nullable: true,
                comment: "JSONB {noShowFeePercent, additionalPaymentTimeoutMinutes}. NULL defaults to {0, 30}.");

            migrationBuilder.AddColumn<OperatorRegistrationStatus>(
                name: "registration_status",
                schema: "vietride_identity",
                table: "operators",
                type: "operator_registration_status",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reject_reason",
                schema: "vietride_identity",
                table: "operators",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejected_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rejected_by_user_id",
                schema: "vietride_identity",
                table: "operators",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "representative_name",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "representative_phone",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspend_reason",
                schema: "vietride_identity",
                table: "operators",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "vietride_identity",
                table: "operators",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("""
                UPDATE vietride_identity.operators
                SET
                    name = COALESCE(NULLIF(name, ''), 'Legacy Operator ' || LEFT(REPLACE(id::text, '-', ''), 12)),
                    business_registration_number = COALESCE(NULLIF(business_registration_number, ''), 'LEGACY-BRN-' || REPLACE(id::text, '-', '')),
                    tax_code = COALESCE(NULLIF(tax_code, ''), 'LEGACY-TAX-' || REPLACE(id::text, '-', '')),
                    contact_email = COALESCE(NULLIF(contact_email, ''), 'legacy+' || LEFT(REPLACE(id::text, '-', ''), 12) || '@vietride.local'),
                    contact_phone = COALESCE(NULLIF(contact_phone, ''), '+84000000000'),
                    registration_status = COALESCE(registration_status, 'APPROVED'::operator_registration_status)
                WHERE
                    name IS NULL OR name = '' OR
                    business_registration_number IS NULL OR business_registration_number = '' OR
                    tax_code IS NULL OR tax_code = '' OR
                    contact_email IS NULL OR contact_email = '' OR
                    contact_phone IS NULL OR contact_phone = '' OR
                    registration_status IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE vietride_identity.operators
                    ALTER COLUMN name SET NOT NULL,
                    ALTER COLUMN business_registration_number SET NOT NULL,
                    ALTER COLUMN tax_code SET NOT NULL,
                    ALTER COLUMN contact_email SET NOT NULL,
                    ALTER COLUMN contact_phone SET NOT NULL,
                    ALTER COLUMN registration_status SET NOT NULL,
                    ALTER COLUMN registration_status SET DEFAULT 'PENDING'::operator_registration_status;
                """);

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price_per_month = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    price_per_year = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    max_vehicles = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_drivers = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_assistants = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_operator_users = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_routes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_trips_per_month = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    enable_parcel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    enable_shuttle = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    enable_rag = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                    table.CheckConstraint("chk_subscription_plans_price_per_month_non_negative", "price_per_month >= 0");
                    table.CheckConstraint("chk_subscription_plans_price_per_year_non_negative", "price_per_year >= 0");
                });

            migrationBuilder.Sql("""
                INSERT INTO vietride_identity.subscription_plans (
                    id,
                    name,
                    description,
                    price_per_month,
                    price_per_year,
                    max_vehicles,
                    max_drivers,
                    max_assistants,
                    max_operator_users,
                    max_routes,
                    max_trips_per_month,
                    enable_parcel,
                    enable_shuttle,
                    enable_rag,
                    is_active)
                VALUES (
                    '00000000-0000-0000-0000-000000000001',
                    'Starter (Free Trial)',
                    'Default onboarding plan seeded by Identity migration.',
                    0,
                    0,
                    3,
                    5,
                    5,
                    3,
                    5,
                    100,
                    FALSE,
                    FALSE,
                    TRUE,
                    TRUE)
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.CreateTable(
                name: "operator_subscriptions",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_active_plan_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Plan ACTIVE before PENDING_PAYMENT; used by revert flow if payment times out after 7 days."),
                    status = table.Column<SubscriptionStatus>(type: "subscription_status", nullable: false, defaultValue: SubscriptionStatus.PENDING_APPROVAL),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_method = table.Column<SubscriptionPaymentMethod>(type: "subscription_payment_method", nullable: true),
                    current_vehicles = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_drivers = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_assistants = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_operator_users = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_routes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_trips_this_month = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Reset to 0 monthly by Hangfire (day 1, 00:01). Skipped for Trip.source = VEHICLE_SUBSTITUTION."),
                    last_reset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    warn_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_expiring_warn_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_operator_subscriptions_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_subscriptions_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "vietride_identity",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_subscriptions_previous_active_plan_id",
                        column: x => x.previous_active_plan_id,
                        principalSchema: "vietride_identity",
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_operators_is_active",
                schema: "vietride_identity",
                table: "operators",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "idx_operators_registration_status",
                schema: "vietride_identity",
                table: "operators",
                column: "registration_status");

            migrationBuilder.CreateIndex(
                name: "uq_operators_business_reg_number",
                schema: "vietride_identity",
                table: "operators",
                column: "business_registration_number",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_operators_tax_code",
                schema: "vietride_identity",
                table: "operators",
                column: "tax_code",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_subscriptions_expires_at",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "expires_at",
                filter: "status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "idx_operator_subscriptions_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "idx_operator_subscriptions_previous_active_plan_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "previous_active_plan_id",
                filter: "previous_active_plan_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_subscriptions_status",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_operator_subscriptions_operator_id",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                column: "operator_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_subscription_plans_is_active",
                schema: "vietride_identity",
                table: "subscription_plans",
                column: "is_active");

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_operators_updated_at BEFORE UPDATE ON vietride_identity.operators
                    FOR EACH ROW EXECUTE FUNCTION vietride_identity.trg_set_updated_at();
                CREATE TRIGGER trg_subscription_plans_updated_at BEFORE UPDATE ON vietride_identity.subscription_plans
                    FOR EACH ROW EXECUTE FUNCTION vietride_identity.trg_set_updated_at();
                CREATE TRIGGER trg_operator_subscriptions_updated_at BEFORE UPDATE ON vietride_identity.operator_subscriptions
                    FOR EACH ROW EXECUTE FUNCTION vietride_identity.trg_set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_operator_subscriptions_updated_at ON vietride_identity.operator_subscriptions;
                DROP TRIGGER IF EXISTS trg_subscription_plans_updated_at ON vietride_identity.subscription_plans;
                DROP TRIGGER IF EXISTS trg_operators_updated_at ON vietride_identity.operators;
                """);

            migrationBuilder.DropTable(
                name: "operator_subscriptions",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "subscription_plans",
                schema: "vietride_identity");

            migrationBuilder.DropIndex(
                name: "idx_operators_is_active",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropIndex(
                name: "idx_operators_registration_status",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropIndex(
                name: "uq_operators_business_reg_number",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropIndex(
                name: "uq_operators_tax_code",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "address_district",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "address_province",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "address_street",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "address_ward",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "bank_account_name",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "bank_account_number",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "bank_name",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "business_registration_number",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "cancellation_policy",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "logo_url",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "luggage_policy",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "parcel_no_show_policy",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "registration_status",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "reject_reason",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "rejected_by_user_id",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "representative_name",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "representative_phone",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "suspend_reason",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "vietride_identity",
                table: "operators");

            migrationBuilder.Sql("DROP TYPE IF EXISTS subscription_payment_method;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS subscription_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS operator_registration_status;");

        }
    }
}
