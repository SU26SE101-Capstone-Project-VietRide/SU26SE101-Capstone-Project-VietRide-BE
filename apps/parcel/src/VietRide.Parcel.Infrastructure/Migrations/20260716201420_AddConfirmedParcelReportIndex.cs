using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedParcelReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_parcels_confirmed_report",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "confirmed_at", "operator_id" },
                filter: "status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status AND confirmed_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_parcels_confirmed_report",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
