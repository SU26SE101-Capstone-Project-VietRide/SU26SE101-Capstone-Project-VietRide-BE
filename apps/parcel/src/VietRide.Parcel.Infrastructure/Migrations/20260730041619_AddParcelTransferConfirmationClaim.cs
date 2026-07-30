using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelTransferConfirmationClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transfer_confirmation_claim_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transfer_confirmation_claimed_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_confirmation_claimed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_parcels_transfer_confirmation_claimed_at",
                schema: "vietride_parcel",
                table: "parcels",
                column: "transfer_confirmation_claimed_at",
                filter: "status = 'PENDING_TRANSFER_CONFIRM' AND transfer_confirmation_claim_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_parcels_transfer_confirmation_claimed_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "transfer_confirmation_claim_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "transfer_confirmation_claimed_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "transfer_confirmation_claimed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels");
        }
    }
}
