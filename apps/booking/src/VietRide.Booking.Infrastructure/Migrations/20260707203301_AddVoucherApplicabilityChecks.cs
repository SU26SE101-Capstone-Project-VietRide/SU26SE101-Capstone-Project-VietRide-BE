using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherApplicabilityChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "chk_vouchers_applicable_payment_methods_valid",
                schema: "vietride_booking",
                table: "vouchers",
                sql: "applicable_payment_methods IS NULL OR applicable_payment_methods <@ ARRAY['WALLET', 'VNPAY']::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "chk_vouchers_applicable_services_valid",
                schema: "vietride_booking",
                table: "vouchers",
                sql: "applicable_services <@ ARRAY['BOOKING', 'PARCEL']::text[] AND cardinality(applicable_services) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_vouchers_applicable_payment_methods_valid",
                schema: "vietride_booking",
                table: "vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "chk_vouchers_applicable_services_valid",
                schema: "vietride_booking",
                table: "vouchers");
        }
    }
}
