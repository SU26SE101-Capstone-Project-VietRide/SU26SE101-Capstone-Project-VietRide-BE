using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClassifyOperatorLedgerAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_payment.operator_ledger_entries
                SET adjustment_reason = CASE
                    WHEN note = 'reverse-vietride-funded-voucher'
                         AND amount < 0
                         AND reference_type IN ('BOOKING', 'PARCEL')
                        THEN 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'::vietride_payment.operator_ledger_adjustment_reason
                    WHEN note = 'generic-booking-refund-entitlement'
                         AND amount = 0
                         AND reference_type = 'BOOKING'
                        THEN 'GENERIC_BOOKING_REFUND_ENTITLEMENT'::vietride_payment.operator_ledger_adjustment_reason
                    WHEN reference_type = 'MANUAL' AND amount <> 0
                        THEN 'MANUAL_WALLET_ADJUSTMENT'::vietride_payment.operator_ledger_adjustment_reason
                    ELSE 'LEGACY_UNCLASSIFIED'::vietride_payment.operator_ledger_adjustment_reason
                END
                WHERE entry_type = 'ADJUSTMENT' AND adjustment_reason IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_payment.operator_ledger_entries
                SET adjustment_reason = NULL
                WHERE entry_type = 'ADJUSTMENT';
                """);
        }
    }
}
