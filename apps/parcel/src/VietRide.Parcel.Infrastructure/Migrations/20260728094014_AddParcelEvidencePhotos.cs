using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelEvidencePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "check_in_photo_urls",
                schema: "vietride_parcel",
                table: "parcels",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_photo_urls",
                schema: "vietride_parcel",
                table: "parcels",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_check_in_photo_urls_max_three",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "check_in_photo_urls IS NULL OR (jsonb_typeof(check_in_photo_urls) = 'array' AND jsonb_array_length(check_in_photo_urls) <= 3)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_delivery_photo_urls_max_three",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "delivery_photo_urls IS NULL OR (jsonb_typeof(delivery_photo_urls) = 'array' AND jsonb_array_length(delivery_photo_urls) <= 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_check_in_photo_urls_max_three",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_delivery_photo_urls_max_three",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "check_in_photo_urls",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "delivery_photo_urls",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
