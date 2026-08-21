using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelClaimAppealAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "appeal_reason",
                schema: "vietride_parcel",
                table: "parcel_claims",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appealed_at",
                schema: "vietride_parcel",
                table: "parcel_claims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "appealed_by_user_id",
                schema: "vietride_parcel",
                table: "parcel_claims",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "appeal_reason",
                schema: "vietride_parcel",
                table: "parcel_claims");

            migrationBuilder.DropColumn(
                name: "appealed_at",
                schema: "vietride_parcel",
                table: "parcel_claims");

            migrationBuilder.DropColumn(
                name: "appealed_by_user_id",
                schema: "vietride_parcel",
                table: "parcel_claims");
        }
    }
}
