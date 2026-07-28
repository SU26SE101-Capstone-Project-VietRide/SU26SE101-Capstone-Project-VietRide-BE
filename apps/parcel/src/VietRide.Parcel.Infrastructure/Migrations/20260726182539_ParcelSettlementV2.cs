using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParcelSettlementV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE vietride_parcel.parcel_status ADD VALUE IF NOT EXISTS 'RESERVED' AFTER 'PENDING_ADDITIONAL_PAYMENT';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE vietride_parcel.parcel_status ADD VALUE IF NOT EXISTS 'CHECKED_IN' AFTER 'RESERVED';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE vietride_parcel.parcel_status ADD VALUE IF NOT EXISTS 'PENDING_FINAL_PAYMENT' AFTER 'CHECKED_IN';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE vietride_parcel.parcel_status ADD VALUE IF NOT EXISTS 'READY_TO_LOAD' AFTER 'PENDING_FINAL_PAYMENT';",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL enum values cannot be removed safely in-place. The following schema
            // migration maps every v2 row back to a legacy status before dropping v2 columns;
            // retaining unused labels keeps the rollback data-safe.
        }
    }
}
