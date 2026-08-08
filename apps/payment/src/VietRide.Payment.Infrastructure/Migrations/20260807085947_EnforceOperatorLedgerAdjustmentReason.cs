using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOperatorLedgerAdjustmentReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_payments_subscription_succeeded_at",
                schema: "vietride_payment",
                table: "payments",
                column: "succeeded_at",
                filter: "reference_type = 'SUBSCRIPTION' AND status = 'SUCCEEDED'");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_settled_at",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "settled_at",
                filter: "status = 'SETTLED'");

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_canonical_revenue",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "created_at", "operator_id", "reference_type" },
                filter: "entry_type IN ('BOOKING_REVENUE','BOOKING_REFUND','PARCEL_REVENUE','PARCEL_REFUND','VOUCHER_VIETRIDE_FUNDED_CREDIT') OR (entry_type = 'ADJUSTMENT' AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_adjustment_reason_presence",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "(entry_type = 'ADJUSTMENT' AND adjustment_reason IS NOT NULL) OR (entry_type <> 'ADJUSTMENT' AND adjustment_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_adjustment_reason_semantics",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "(adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL' AND amount < 0 AND reference_type IN ('BOOKING','PARCEL')) OR (adjustment_reason = 'GENERIC_BOOKING_REFUND_ENTITLEMENT' AND amount = 0 AND reference_type = 'BOOKING') OR (adjustment_reason = 'MANUAL_WALLET_ADJUSTMENT' AND amount <> 0 AND reference_type = 'MANUAL') OR (adjustment_reason = 'LEGACY_UNCLASSIFIED') OR adjustment_reason IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_payments_subscription_succeeded_at",
                schema: "vietride_payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "idx_operator_trip_settlements_settled_at",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropIndex(
                name: "idx_operator_ledger_entries_canonical_revenue",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_adjustment_reason_presence",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_adjustment_reason_semantics",
                schema: "vietride_payment",
                table: "operator_ledger_entries");
        }
    }
}
