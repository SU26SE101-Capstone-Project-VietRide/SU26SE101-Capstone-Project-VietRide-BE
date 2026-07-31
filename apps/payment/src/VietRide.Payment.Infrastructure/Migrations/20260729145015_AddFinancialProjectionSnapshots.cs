using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialProjectionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deleted_financial_actor_markers",
                schema: "vietride_payment",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deleted_financial_actor_markers", x => x.user_id);
                });

            migrationBuilder.AddColumn<string>(
                name: "actor_display_name",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_email",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_role",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "actor_snapshot_resolved",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValueSql: "'SYSTEM'");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_contact_phone",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_logo_url",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "operator_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "settled_by_display_name",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settled_by_email",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settled_by_role",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "settled_by_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE vietride_payment.platform_wallet_transactions " +
                "SET actor_type = 'USER' " +
                "WHERE reference_type = 'MANUAL_ADJUSTMENT';");

            migrationBuilder.Sql(
                "UPDATE vietride_payment.platform_wallet_transactions AS transaction " +
                "SET actor_type = 'USER', " +
                "actor_user_id = settlement.settled_by_user_id, " +
                "actor_snapshot_resolved = (settlement.settled_by_user_id IS NULL) " +
                "FROM vietride_payment.operator_trip_settlements AS settlement " +
                "WHERE transaction.reference_type = 'TRIP_SETTLEMENT' " +
                "AND transaction.reference_id = settlement.id " +
                "AND settlement.settlement_method = 'ADMIN_MANUAL';");

            migrationBuilder.Sql(
                "UPDATE vietride_payment.operator_trip_settlements " +
                "SET settled_by_snapshot_resolved = TRUE " +
                "WHERE settled_by_user_id IS NULL;");

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transactions_actor_user_id",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                column: "actor_user_id",
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_platform_wallet_transactions_actor_type",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                sql: "actor_type IN ('USER','SYSTEM')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deleted_financial_actor_markers",
                schema: "vietride_payment");

            migrationBuilder.DropIndex(
                name: "idx_platform_wallet_transactions_actor_user_id",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_platform_wallet_transactions_actor_type",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_display_name",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_email",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_role",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_snapshot_resolved",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_type",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropColumn(
                name: "operator_contact_phone",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "operator_logo_url",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "operator_name",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "operator_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "settled_by_display_name",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "settled_by_email",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "settled_by_role",
                schema: "vietride_payment",
                table: "operator_trip_settlements");

            migrationBuilder.DropColumn(
                name: "settled_by_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_trip_settlements");
        }
    }
}
