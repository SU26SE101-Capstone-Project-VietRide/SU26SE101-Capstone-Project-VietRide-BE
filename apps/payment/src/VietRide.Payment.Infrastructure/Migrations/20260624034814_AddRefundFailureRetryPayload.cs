using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundFailureRetryPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "amount",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reference_id",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_type",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_refund_failure_logs_reference",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                columns: new[] { "reference_type", "reference_id" },
                filter: "reference_type IS NOT NULL AND reference_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_refund_failure_logs_reference",
                schema: "vietride_payment",
                table: "refund_failure_logs");

            migrationBuilder.DropColumn(
                name: "amount",
                schema: "vietride_payment",
                table: "refund_failure_logs");

            migrationBuilder.DropColumn(
                name: "reference_id",
                schema: "vietride_payment",
                table: "refund_failure_logs");

            migrationBuilder.DropColumn(
                name: "reference_type",
                schema: "vietride_payment",
                table: "refund_failure_logs");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "vietride_payment",
                table: "refund_failure_logs");
        }
    }
}
