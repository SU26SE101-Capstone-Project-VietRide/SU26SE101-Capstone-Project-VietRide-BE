using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentSessionDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_at",
                schema: "vietride_payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE vietride_payment.payments
                SET due_at = created_at + INTERVAL '15 minutes'
                WHERE reference_type = 'SUBSCRIPTION'::vietride_payment.payment_reference_type
                  AND due_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "due_at",
                schema: "vietride_payment",
                table: "payments");
        }
    }
}
