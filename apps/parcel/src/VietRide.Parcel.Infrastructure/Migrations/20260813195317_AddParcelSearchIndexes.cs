using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"unaccent\";");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_operator_created_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "operator_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "idx_parcels_operator_final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "operator_id", "final_payment_deadline", "id" },
                filter: "final_payment_deadline IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_operator_route_snapshot",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "operator_id", "trip_snapshot_route_id" },
                filter: "trip_snapshot_route_id IS NOT NULL");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_parcels_operator_created_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropIndex(
                name: "idx_parcels_operator_final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropIndex(
                name: "idx_parcels_operator_route_snapshot",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"unaccent\";");
        }
    }
}
