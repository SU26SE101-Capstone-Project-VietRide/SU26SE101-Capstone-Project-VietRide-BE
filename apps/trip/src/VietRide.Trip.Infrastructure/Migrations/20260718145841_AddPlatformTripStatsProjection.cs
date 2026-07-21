using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformTripStatsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE vietride_trip.platform_trip_stats (
                    trip_id UUID PRIMARY KEY
                        REFERENCES vietride_trip.trips(id) ON DELETE CASCADE,
                    operator_id UUID NOT NULL,
                    completed_at TIMESTAMPTZ NOT NULL,
                    projected_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );

                CREATE INDEX idx_platform_trip_stats_completed_operator
                    ON vietride_trip.platform_trip_stats (completed_at, operator_id);

                CREATE OR REPLACE FUNCTION vietride_trip.sync_platform_trip_stats()
                RETURNS TRIGGER AS $$
                DECLARE
                    source_id UUID := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
                BEGIN
                    IF TG_OP <> 'DELETE'
                       AND NEW.status = 'COMPLETED'::vietride_trip.trip_status
                       AND NEW.completed_at IS NOT NULL THEN
                        INSERT INTO vietride_trip.platform_trip_stats (
                            trip_id,
                            operator_id,
                            completed_at,
                            projected_at
                        )
                        VALUES (NEW.id, NEW.operator_id, NEW.completed_at, now())
                        ON CONFLICT (trip_id) DO UPDATE SET
                            operator_id = EXCLUDED.operator_id,
                            completed_at = EXCLUDED.completed_at,
                            projected_at = now();
                    ELSE
                        DELETE FROM vietride_trip.platform_trip_stats
                        WHERE trip_id = source_id;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_sync_platform_trip_stats
                    AFTER INSERT OR UPDATE OR DELETE ON vietride_trip.trips
                    FOR EACH ROW EXECUTE FUNCTION vietride_trip.sync_platform_trip_stats();

                CREATE OR REPLACE FUNCTION vietride_trip.rebuild_platform_trip_stats()
                RETURNS VOID AS $$
                BEGIN
                    INSERT INTO vietride_trip.platform_trip_stats (
                        trip_id,
                        operator_id,
                        completed_at,
                        projected_at
                    )
                    SELECT id, operator_id, completed_at, now()
                    FROM vietride_trip.trips
                    WHERE status = 'COMPLETED'::vietride_trip.trip_status
                      AND completed_at IS NOT NULL
                    ON CONFLICT (trip_id) DO UPDATE SET
                        operator_id = EXCLUDED.operator_id,
                        completed_at = EXCLUDED.completed_at,
                        projected_at = now();

                    DELETE FROM vietride_trip.platform_trip_stats projection
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM vietride_trip.trips source
                        WHERE source.id = projection.trip_id
                          AND source.status = 'COMPLETED'::vietride_trip.trip_status
                          AND source.completed_at IS NOT NULL
                    );
                END;
                $$ LANGUAGE plpgsql;

                SELECT vietride_trip.rebuild_platform_trip_stats();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_platform_trip_stats
                    ON vietride_trip.trips;
                DROP FUNCTION IF EXISTS vietride_trip.sync_platform_trip_stats();
                DROP FUNCTION IF EXISTS vietride_trip.rebuild_platform_trip_stats();
                DROP TABLE IF EXISTS vietride_trip.platform_trip_stats;
                """);
        }
    }
}
