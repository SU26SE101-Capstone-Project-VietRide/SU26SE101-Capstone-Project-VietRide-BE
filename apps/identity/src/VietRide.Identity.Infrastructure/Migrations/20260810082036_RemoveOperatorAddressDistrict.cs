using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOperatorAddressDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_district",
                schema: "vietride_identity",
                table: "operators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_district",
                schema: "vietride_identity",
                table: "operators",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
