using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundFailureLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refund_failure_logs",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refund_failure_logs", x => x.id);
                    table.CheckConstraint("chk_refund_failure_logs_target_exists", "booking_id IS NOT NULL OR parcel_id IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "idx_refund_failure_logs_booking_id",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                column: "booking_id",
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_refund_failure_logs_parcel_id",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                column: "parcel_id",
                filter: "parcel_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_refund_failure_logs_resolved_by_user_id",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                column: "resolved_by_user_id",
                filter: "resolved_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_refund_failure_logs_unresolved",
                schema: "vietride_payment",
                table: "refund_failure_logs",
                column: "last_attempt_at",
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refund_failure_logs",
                schema: "vietride_payment");
        }
    }
}
