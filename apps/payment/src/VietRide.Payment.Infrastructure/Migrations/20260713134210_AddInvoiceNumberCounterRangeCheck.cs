using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumberCounterRangeCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "chk_invoice_number_counters_range",
                schema: "vietride_payment",
                table: "invoice_number_counters",
                sql: "last_value >= 0 AND last_value <= 999999");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_invoice_number_counters_range",
                schema: "vietride_payment",
                table: "invoice_number_counters");
        }
    }
}
