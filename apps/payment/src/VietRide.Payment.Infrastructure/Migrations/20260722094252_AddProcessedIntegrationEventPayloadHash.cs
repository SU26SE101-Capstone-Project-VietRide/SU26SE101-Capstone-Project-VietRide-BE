using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedIntegrationEventPayloadHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payload_hash",
                schema: "vietride_payment",
                table: "processed_integration_events",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payload_hash",
                schema: "vietride_payment",
                table: "processed_integration_events");
        }
    }
}
