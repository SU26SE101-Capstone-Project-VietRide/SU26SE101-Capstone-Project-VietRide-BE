using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowNegativeParcelCompensationLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_amount_direction",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_amount_direction",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "(entry_type IN ('BOOKING_REFUND','PARCEL_REFUND','PARCEL_COMPENSATION') AND amount < 0) OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND amount = 0) OR (entry_type = 'ADJUSTMENT') OR (entry_type NOT IN ('BOOKING_REFUND','PARCEL_REFUND','PARCEL_COMPENSATION','VOUCHER_OPERATOR_FUNDED_AUDIT','ADJUSTMENT') AND amount > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_amount_direction",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_amount_direction",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "(entry_type IN ('BOOKING_REFUND','PARCEL_REFUND') AND amount < 0) OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND amount = 0) OR (entry_type = 'ADJUSTMENT') OR (entry_type NOT IN ('BOOKING_REFUND','PARCEL_REFUND','VOUCHER_OPERATOR_FUNDED_AUDIT','ADJUSTMENT') AND amount > 0)");
        }
    }
}
