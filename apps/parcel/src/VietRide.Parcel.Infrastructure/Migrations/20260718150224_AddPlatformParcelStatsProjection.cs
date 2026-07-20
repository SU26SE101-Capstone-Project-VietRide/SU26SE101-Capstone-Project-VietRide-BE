using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformParcelStatsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE vietride_parcel.platform_parcel_stats (
                    parcel_id UUID PRIMARY KEY
                        REFERENCES vietride_parcel.parcels(id) ON DELETE CASCADE,
                    operator_id UUID NOT NULL,
                    confirmed_at TIMESTAMPTZ NOT NULL,
                    parcel_revenue_vnd BIGINT NOT NULL,
                    projected_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );

                CREATE INDEX idx_platform_parcel_stats_confirmed_operator
                    ON vietride_parcel.platform_parcel_stats (confirmed_at, operator_id);

                CREATE OR REPLACE FUNCTION vietride_parcel.sync_platform_parcel_stats()
                RETURNS TRIGGER AS $$
                DECLARE
                    source_id UUID := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
                BEGIN
                    IF TG_OP <> 'DELETE'
                       AND NEW.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                       AND NEW.confirmed_at IS NOT NULL THEN
                        INSERT INTO vietride_parcel.platform_parcel_stats (
                            parcel_id,
                            operator_id,
                            confirmed_at,
                            parcel_revenue_vnd,
                            projected_at
                        )
                        VALUES (
                            NEW.id,
                            NEW.operator_id,
                            NEW.confirmed_at,
                            (NEW.deposit_amount::numeric
                                + NEW.additional_amount::numeric
                                - NEW.refund_amount::numeric)::bigint,
                            now()
                        )
                        ON CONFLICT (parcel_id) DO UPDATE SET
                            operator_id = EXCLUDED.operator_id,
                            confirmed_at = EXCLUDED.confirmed_at,
                            parcel_revenue_vnd = EXCLUDED.parcel_revenue_vnd,
                            projected_at = now();
                    ELSE
                        DELETE FROM vietride_parcel.platform_parcel_stats
                        WHERE parcel_id = source_id;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_sync_platform_parcel_stats
                    AFTER INSERT OR UPDATE OR DELETE ON vietride_parcel.parcels
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.sync_platform_parcel_stats();

                CREATE OR REPLACE FUNCTION vietride_parcel.rebuild_platform_parcel_stats()
                RETURNS VOID AS $$
                BEGIN
                    INSERT INTO vietride_parcel.platform_parcel_stats (
                        parcel_id,
                        operator_id,
                        confirmed_at,
                        parcel_revenue_vnd,
                        projected_at
                    )
                    SELECT
                        id,
                        operator_id,
                        confirmed_at,
                        (deposit_amount::numeric
                            + additional_amount::numeric
                            - refund_amount::numeric)::bigint,
                        now()
                    FROM vietride_parcel.parcels
                    WHERE status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                      AND confirmed_at IS NOT NULL
                    ON CONFLICT (parcel_id) DO UPDATE SET
                        operator_id = EXCLUDED.operator_id,
                        confirmed_at = EXCLUDED.confirmed_at,
                        parcel_revenue_vnd = EXCLUDED.parcel_revenue_vnd,
                        projected_at = now();

                    DELETE FROM vietride_parcel.platform_parcel_stats projection
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM vietride_parcel.parcels source
                        WHERE source.id = projection.parcel_id
                          AND source.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                          AND source.confirmed_at IS NOT NULL
                    );
                END;
                $$ LANGUAGE plpgsql;

                SELECT vietride_parcel.rebuild_platform_parcel_stats();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_platform_parcel_stats
                    ON vietride_parcel.parcels;
                DROP FUNCTION IF EXISTS vietride_parcel.sync_platform_parcel_stats();
                DROP FUNCTION IF EXISTS vietride_parcel.rebuild_platform_parcel_stats();
                DROP TABLE IF EXISTS vietride_parcel.platform_parcel_stats;
                """);
        }
    }
}
