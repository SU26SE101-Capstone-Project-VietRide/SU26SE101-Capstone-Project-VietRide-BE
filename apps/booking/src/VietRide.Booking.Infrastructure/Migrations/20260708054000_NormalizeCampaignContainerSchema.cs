using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCampaignContainerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS vietride_booking.campaigns (
                    id uuid NOT NULL DEFAULT gen_random_uuid(),
                    name character varying(120) NULL,
                    description text NULL,
                    owner_operator_id uuid NULL,
                    valid_from timestamp with time zone NOT NULL,
                    valid_until timestamp with time zone NOT NULL,
                    is_active boolean NOT NULL DEFAULT TRUE,
                    created_by_user_id uuid NOT NULL,
                    deleted_at timestamp with time zone NULL,
                    created_at timestamp with time zone NOT NULL DEFAULT now(),
                    updated_at timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT pk_campaigns PRIMARY KEY (id)
                );

                ALTER TABLE vietride_booking.campaigns
                    ADD COLUMN IF NOT EXISTS name character varying(120) NULL,
                    ADD COLUMN IF NOT EXISTS description text NULL,
                    ADD COLUMN IF NOT EXISTS owner_operator_id uuid NULL,
                    ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone NULL;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'vietride_booking'
                          AND table_name = 'campaigns'
                          AND column_name = 'title'
                    ) THEN
                        UPDATE vietride_booking.campaigns
                        SET name = COALESCE(NULLIF(name, ''), NULLIF(title, ''), 'Untitled Campaign')
                        WHERE name IS NULL OR name = '';
                    END IF;
                END $$;

                UPDATE vietride_booking.campaigns
                SET name = 'Untitled Campaign'
                WHERE name IS NULL OR name = '';

                ALTER TABLE vietride_booking.campaigns
                    ALTER COLUMN name SET NOT NULL,
                    ALTER COLUMN description DROP NOT NULL;

                ALTER TABLE vietride_booking.campaigns
                    DROP CONSTRAINT IF EXISTS chk_campaigns_priority_non_negative,
                    DROP CONSTRAINT IF EXISTS chk_campaigns_validity_window;

                ALTER TABLE vietride_booking.campaigns
                    ADD CONSTRAINT chk_campaigns_validity_window CHECK (valid_until > valid_from);

                DROP INDEX IF EXISTS vietride_booking.idx_campaigns_placement_priority_validity;
                DROP INDEX IF EXISTS vietride_booking.idx_campaigns_active_validity;
                DROP INDEX IF EXISTS vietride_booking.idx_campaigns_owner_operator;

                ALTER TABLE vietride_booking.campaigns
                    DROP COLUMN IF EXISTS title,
                    DROP COLUMN IF EXISTS banner_image_url,
                    DROP COLUMN IF EXISTS display_tag,
                    DROP COLUMN IF EXISTS placement,
                    DROP COLUMN IF EXISTS priority;

                CREATE INDEX IF NOT EXISTS idx_campaigns_active_validity
                    ON vietride_booking.campaigns (is_active, valid_until);

                CREATE INDEX IF NOT EXISTS idx_campaigns_owner_operator
                    ON vietride_booking.campaigns (owner_operator_id)
                    WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL;

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

                ALTER TABLE vietride_booking.campaign_vouchers
                    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
                    ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT now();

                UPDATE vietride_booking.campaign_vouchers
                SET id = gen_random_uuid()
                WHERE id IS NULL;

                ALTER TABLE vietride_booking.campaign_vouchers
                    DROP CONSTRAINT IF EXISTS pk_campaign_vouchers;

                ALTER TABLE vietride_booking.campaign_vouchers
                    ALTER COLUMN id SET NOT NULL,
                    ADD CONSTRAINT pk_campaign_vouchers PRIMARY KEY (id);

                DROP INDEX IF EXISTS vietride_booking.uq_campaign_vouchers_campaign_voucher;
                DROP INDEX IF EXISTS vietride_booking.idx_campaign_vouchers_voucher_id;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_campaign_vouchers_campaign_voucher
                    ON vietride_booking.campaign_vouchers (campaign_id, voucher_id);

                CREATE INDEX IF NOT EXISTS idx_campaign_vouchers_voucher_id
                    ON vietride_booking.campaign_vouchers (voucher_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS vietride_booking.idx_campaigns_owner_operator;
                DROP INDEX IF EXISTS vietride_booking.idx_campaigns_active_validity;

                ALTER TABLE vietride_booking.campaigns
                    ADD COLUMN IF NOT EXISTS title character varying(160) NULL,
                    ADD COLUMN IF NOT EXISTS banner_image_url character varying(500) NULL,
                    ADD COLUMN IF NOT EXISTS display_tag character varying(60) NULL,
                    ADD COLUMN IF NOT EXISTS placement vietride_booking.campaign_placement NULL,
                    ADD COLUMN IF NOT EXISTS priority integer NOT NULL DEFAULT 0;

                UPDATE vietride_booking.campaigns
                SET title = COALESCE(title, name),
                    placement = COALESCE(placement, 'HOME_HERO'::vietride_booking.campaign_placement);

                ALTER TABLE vietride_booking.campaigns
                    ALTER COLUMN title SET NOT NULL,
                    ALTER COLUMN description SET NOT NULL,
                    ALTER COLUMN placement SET NOT NULL;

                ALTER TABLE vietride_booking.campaigns
                    DROP COLUMN IF EXISTS name,
                    DROP COLUMN IF EXISTS owner_operator_id;

                ALTER TABLE vietride_booking.campaigns
                    ADD CONSTRAINT chk_campaigns_priority_non_negative CHECK (priority >= 0);

                ALTER TABLE vietride_booking.campaign_vouchers
                    DROP CONSTRAINT IF EXISTS pk_campaign_vouchers,
                    ADD CONSTRAINT pk_campaign_vouchers PRIMARY KEY (campaign_id, voucher_id);

                CREATE INDEX IF NOT EXISTS idx_campaigns_placement_priority_validity
                    ON vietride_booking.campaigns (placement, priority, valid_until)
                    WHERE is_active = true AND deleted_at IS NULL;
                """);
        }
    }
}
