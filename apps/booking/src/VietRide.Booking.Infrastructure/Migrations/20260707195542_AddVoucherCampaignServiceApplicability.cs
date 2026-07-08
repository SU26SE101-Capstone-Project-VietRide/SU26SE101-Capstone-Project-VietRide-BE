using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherCampaignServiceApplicability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS vietride_booking.idx_voucher_usages_booking_id;");

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_booking.vouchers
                    ADD COLUMN IF NOT EXISTS applicable_payment_methods text[];

                ALTER TABLE vietride_booking.vouchers
                    ADD COLUMN IF NOT EXISTS applicable_services text[] DEFAULT ARRAY['BOOKING']::text[];

                UPDATE vietride_booking.vouchers
                SET applicable_services = ARRAY['BOOKING']::text[]
                WHERE applicable_services IS NULL;

                ALTER TABLE vietride_booking.vouchers
                    ALTER COLUMN applicable_services SET DEFAULT ARRAY['BOOKING']::text[],
                    ALTER COLUMN applicable_services SET NOT NULL;

                ALTER TABLE vietride_booking.vouchers
                    ADD COLUMN IF NOT EXISTS new_user_only boolean DEFAULT FALSE;

                UPDATE vietride_booking.vouchers
                SET new_user_only = FALSE
                WHERE new_user_only IS NULL;

                ALTER TABLE vietride_booking.vouchers
                    ALTER COLUMN new_user_only SET DEFAULT FALSE,
                    ALTER COLUMN new_user_only SET NOT NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                schema: "vietride_booking",
                table: "voucher_usages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_booking.voucher_usages
                    ADD COLUMN IF NOT EXISTS reference_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid;

                ALTER TABLE vietride_booking.voucher_usages
                    ADD COLUMN IF NOT EXISTS reference_type character varying(20) DEFAULT 'BOOKING';

                UPDATE vietride_booking.voucher_usages
                SET reference_type = 'BOOKING',
                    reference_id = booking_id
                WHERE booking_id IS NOT NULL
                  AND (reference_type IS NULL OR reference_id IS NULL OR reference_id = '00000000-0000-0000-0000-000000000000'::uuid);

                UPDATE vietride_booking.voucher_usages
                SET reference_type = COALESCE(reference_type, 'BOOKING'),
                    reference_id = COALESCE(reference_id, '00000000-0000-0000-0000-000000000000'::uuid);

                ALTER TABLE vietride_booking.voucher_usages
                    ALTER COLUMN reference_id SET DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
                    ALTER COLUMN reference_id SET NOT NULL,
                    ALTER COLUMN reference_type SET DEFAULT 'BOOKING',
                    ALTER COLUMN reference_type SET NOT NULL;

                CREATE TABLE IF NOT EXISTS vietride_booking.campaigns (
                    id uuid NOT NULL DEFAULT gen_random_uuid(),
                    name character varying(120) NOT NULL,
                    description text NULL,
                    owner_operator_id uuid NULL,
                    valid_from timestamp with time zone NOT NULL,
                    valid_until timestamp with time zone NOT NULL,
                    is_active boolean NOT NULL DEFAULT TRUE,
                    created_by_user_id uuid NOT NULL,
                    deleted_at timestamp with time zone NULL,
                    created_at timestamp with time zone NOT NULL DEFAULT now(),
                    updated_at timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT pk_campaigns PRIMARY KEY (id),
                    CONSTRAINT chk_campaigns_validity_window CHECK (valid_until > valid_from)
                );

                CREATE TABLE IF NOT EXISTS vietride_booking.campaign_vouchers (
                    id uuid NOT NULL DEFAULT gen_random_uuid(),
                    campaign_id uuid NOT NULL,
                    voucher_id uuid NOT NULL,
                    created_at timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT pk_campaign_vouchers PRIMARY KEY (id),
                    CONSTRAINT fk_campaign_vouchers_campaigns_campaign_id FOREIGN KEY (campaign_id)
                        REFERENCES vietride_booking.campaigns (id) ON DELETE CASCADE,
                    CONSTRAINT fk_campaign_vouchers_vouchers_voucher_id FOREIGN KEY (voucher_id)
                        REFERENCES vietride_booking.vouchers (id) ON DELETE CASCADE
                );

                ALTER TABLE vietride_booking.campaigns
                    ADD COLUMN IF NOT EXISTS owner_operator_id uuid NULL,
                    ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone NULL;

                CREATE INDEX IF NOT EXISTS idx_vouchers_new_user_only
                    ON vietride_booking.vouchers (new_user_only);

                CREATE INDEX IF NOT EXISTS idx_voucher_usages_booking_id
                    ON vietride_booking.voucher_usages (booking_id)
                    WHERE booking_id IS NOT NULL;

                CREATE INDEX IF NOT EXISTS idx_voucher_usages_reference
                    ON vietride_booking.voucher_usages (reference_type, reference_id);

                CREATE INDEX IF NOT EXISTS idx_campaign_vouchers_voucher_id
                    ON vietride_booking.campaign_vouchers (voucher_id);

                CREATE UNIQUE INDEX IF NOT EXISTS uq_campaign_vouchers_campaign_voucher
                    ON vietride_booking.campaign_vouchers (campaign_id, voucher_id);

                CREATE INDEX IF NOT EXISTS idx_campaigns_active_validity
                    ON vietride_booking.campaigns (is_active, valid_until);

                CREATE INDEX IF NOT EXISTS idx_campaigns_owner_operator
                    ON vietride_booking.campaigns (owner_operator_id)
                    WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_vouchers",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "vietride_booking");

            migrationBuilder.DropIndex(
                name: "idx_vouchers_new_user_only",
                schema: "vietride_booking",
                table: "vouchers");

            migrationBuilder.DropIndex(
                name: "idx_voucher_usages_booking_id",
                schema: "vietride_booking",
                table: "voucher_usages");

            migrationBuilder.DropIndex(
                name: "idx_voucher_usages_reference",
                schema: "vietride_booking",
                table: "voucher_usages");

            migrationBuilder.DropColumn(
                name: "applicable_payment_methods",
                schema: "vietride_booking",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "applicable_services",
                schema: "vietride_booking",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "new_user_only",
                schema: "vietride_booking",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "reference_id",
                schema: "vietride_booking",
                table: "voucher_usages");

            migrationBuilder.DropColumn(
                name: "reference_type",
                schema: "vietride_booking",
                table: "voucher_usages");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                schema: "vietride_booking",
                table: "voucher_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_voucher_usages_booking_id",
                schema: "vietride_booking",
                table: "voucher_usages",
                column: "booking_id");
        }
    }
}
