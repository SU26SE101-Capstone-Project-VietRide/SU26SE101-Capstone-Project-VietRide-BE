using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBookingStatsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE vietride_booking.platform_booking_stats (
                    booking_id UUID PRIMARY KEY
                        REFERENCES vietride_booking.bookings(id) ON DELETE CASCADE,
                    operator_id UUID NOT NULL,
                    completed_at TIMESTAMPTZ NOT NULL,
                    booking_revenue_vnd BIGINT NOT NULL,
                    projected_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    CONSTRAINT ck_platform_booking_stats_revenue_non_negative
                        CHECK (booking_revenue_vnd >= 0)
                );

                CREATE INDEX idx_platform_booking_stats_completed_operator
                    ON vietride_booking.platform_booking_stats (completed_at, operator_id);

                CREATE OR REPLACE FUNCTION vietride_booking.sync_platform_booking_stats()
                RETURNS TRIGGER AS $$
                DECLARE
                    source_id UUID := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
                BEGIN
                    IF TG_OP <> 'DELETE'
                       AND NEW.status = 'COMPLETED'::public.booking_status
                       AND NEW.completed_at IS NOT NULL THEN
                        INSERT INTO vietride_booking.platform_booking_stats (
                            booking_id,
                            operator_id,
                            completed_at,
                            booking_revenue_vnd,
                            projected_at
                        )
                        VALUES (
                            NEW.id,
                            NEW.operator_id,
                            NEW.completed_at,
                            NEW.total_amount,
                            now()
                        )
                        ON CONFLICT (booking_id) DO UPDATE SET
                            operator_id = EXCLUDED.operator_id,
                            completed_at = EXCLUDED.completed_at,
                            booking_revenue_vnd = EXCLUDED.booking_revenue_vnd,
                            projected_at = now();
                    ELSE
                        DELETE FROM vietride_booking.platform_booking_stats
                        WHERE booking_id = source_id;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_sync_platform_booking_stats
                    AFTER INSERT OR UPDATE OR DELETE ON vietride_booking.bookings
                    FOR EACH ROW EXECUTE FUNCTION vietride_booking.sync_platform_booking_stats();

                CREATE OR REPLACE FUNCTION vietride_booking.rebuild_platform_booking_stats()
                RETURNS VOID AS $$
                BEGIN
                    INSERT INTO vietride_booking.platform_booking_stats (
                        booking_id,
                        operator_id,
                        completed_at,
                        booking_revenue_vnd,
                        projected_at
                    )
                    SELECT
                        id,
                        operator_id,
                        completed_at,
                        total_amount,
                        now()
                    FROM vietride_booking.bookings
                    WHERE status = 'COMPLETED'::public.booking_status
                      AND completed_at IS NOT NULL
                    ON CONFLICT (booking_id) DO UPDATE SET
                        operator_id = EXCLUDED.operator_id,
                        completed_at = EXCLUDED.completed_at,
                        booking_revenue_vnd = EXCLUDED.booking_revenue_vnd,
                        projected_at = now();

                    DELETE FROM vietride_booking.platform_booking_stats projection
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM vietride_booking.bookings source
                        WHERE source.id = projection.booking_id
                          AND source.status = 'COMPLETED'::public.booking_status
                          AND source.completed_at IS NOT NULL
                    );
                END;
                $$ LANGUAGE plpgsql;

                SELECT vietride_booking.rebuild_platform_booking_stats();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_platform_booking_stats
                    ON vietride_booking.bookings;
                DROP FUNCTION IF EXISTS vietride_booking.sync_platform_booking_stats();
                DROP FUNCTION IF EXISTS vietride_booking.rebuild_platform_booking_stats();
                DROP TABLE IF EXISTS vietride_booking.platform_booking_stats;
                """);
        }
    }
}
