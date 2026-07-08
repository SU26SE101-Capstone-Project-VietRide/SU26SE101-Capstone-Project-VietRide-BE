using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelVoucherDepositFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "discount_amount",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "original_deposit_amount",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("UPDATE vietride_parcel.parcels SET original_deposit_amount = deposit_amount WHERE original_deposit_amount = 0;");

            migrationBuilder.AddColumn<string>(
                name: "voucher_code",
                schema: "vietride_parcel",
                table: "parcels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voucher_usage_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_parcels_voucher_usage_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "voucher_usage_id",
                filter: "voucher_usage_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_parcels_voucher_usage_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "original_deposit_amount",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "voucher_code",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "voucher_usage_id",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
