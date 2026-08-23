using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBusinessCodesReleaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transaction_code",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transaction_code",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trip_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_platform_wallet_transactions_code",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                column: "transaction_code",
                unique: true,
                filter: "transaction_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_operator_wallet_transactions_code",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                column: "transaction_code",
                unique: true,
                filter: "transaction_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_trip_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "trip_code");

            migrationBuilder.CreateIndex(
                name: "uq_operator_trip_settlements_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "settlement_code",
                unique: true,
                filter: "settlement_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_platform_wallet_transactions_code",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropIndex(
                name: "uq_operator_wallet_transactions_code",
                schema: "vietride_payment",
                table: "operator_wallet_transactions");

            migrationBuilder.DropIndex(
                name: "idx_operator_trip_settlements_trip_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropIndex(
                name: "uq_operator_trip_settlements_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "transaction_code",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "transaction_code",
                schema: "vietride_payment",
                table: "operator_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "settlement_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "trip_code",
                schema: "vietride_payment",
                table: "operator_trip_settlements");
        }
    }
}
