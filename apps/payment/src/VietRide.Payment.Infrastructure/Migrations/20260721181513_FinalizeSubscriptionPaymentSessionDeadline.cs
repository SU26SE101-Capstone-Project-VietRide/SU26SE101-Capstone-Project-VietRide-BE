using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeSubscriptionPaymentSessionDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX idx_payments_subscription_due_at
                ON vietride_payment.payments (due_at)
                WHERE reference_type = 'SUBSCRIPTION'::vietride_payment.payment_reference_type
                  AND due_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS vietride_payment.idx_payments_subscription_due_at;");
        }
    }
}
