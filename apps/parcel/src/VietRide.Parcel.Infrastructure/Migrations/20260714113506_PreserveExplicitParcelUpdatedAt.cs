using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveExplicitParcelUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION vietride_parcel.trg_set_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
                        NEW.updated_at = now();
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_parcels_updated_at ON vietride_parcel.parcels;
                CREATE TRIGGER trg_parcels_updated_at
                    BEFORE UPDATE ON vietride_parcel.parcels
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.trg_set_updated_at();

                DROP TRIGGER IF EXISTS trg_parcel_route_fares_updated_at ON vietride_parcel.parcel_route_fares;
                CREATE TRIGGER trg_parcel_route_fares_updated_at
                    BEFORE UPDATE ON vietride_parcel.parcel_route_fares
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.trg_set_updated_at();

                DROP TRIGGER IF EXISTS trg_parcel_stats_updated_at ON vietride_parcel.parcel_stats;
                CREATE TRIGGER trg_parcel_stats_updated_at
                    BEFORE UPDATE ON vietride_parcel.parcel_stats
                    FOR EACH ROW EXECUTE FUNCTION vietride_parcel.trg_set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION vietride_parcel.trg_set_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }
    }
}
