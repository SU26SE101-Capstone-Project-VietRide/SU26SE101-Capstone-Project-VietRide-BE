using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementCancelReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "vietride_payment",
                table: "operator_trip_settlements");
        }
    }
}
