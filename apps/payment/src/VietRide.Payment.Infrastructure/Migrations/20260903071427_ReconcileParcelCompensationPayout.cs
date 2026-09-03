using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileParcelCompensationPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paid_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_platform_wallet_transactions_parcel_compensation",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                columns: new[] { "reference_type", "reference_id", "type" },
                unique: true,
                filter: "reference_type = 'PARCEL_COMPENSATION'");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_compensation_payouts_paid_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                column: "paid_event_id",
                unique: true,
                filter: "paid_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_compensation_payouts_source_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                column: "source_event_id",
                unique: true,
                filter: "source_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_operator_wallet_transactions_parcel_compensation",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                columns: new[] { "reference_type", "reference_id", "operator_id", "type" },
                unique: true,
                filter: "reference_type = 'PARCEL_COMPENSATION'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_platform_wallet_transactions_parcel_compensation",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropIndex(
                name: "ix_parcel_compensation_payouts_paid_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts");

            migrationBuilder.DropIndex(
                name: "ix_parcel_compensation_payouts_source_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts");

            migrationBuilder.DropIndex(
                name: "uq_operator_wallet_transactions_parcel_compensation",
                schema: "vietride_payment",
                table: "operator_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "paid_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts");

            migrationBuilder.DropColumn(
                name: "source_event_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts");
        }
    }
}
