using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImproveOperatorWalletTransparency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurred_at",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "operator_funded_voucher_amount",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_code",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_operator_funded_voucher_amount",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "operator_funded_voucher_amount IS NULL OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND operator_funded_voucher_amount > 0)");

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_operator_occurred_at",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "operator_id", "occurred_at" },
                descending: new[] { false, true },
                filter: "occurred_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_operator_reference_code",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "operator_id", "reference_code" },
                filter: "reference_code IS NOT NULL");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_operator_funded_voucher_amount",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropIndex(
                name: "idx_operator_ledger_entries_operator_occurred_at",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropIndex(
                name: "idx_operator_ledger_entries_operator_reference_code",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "occurred_at",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "operator_funded_voucher_amount",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "reference_code",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

        }
    }
}
