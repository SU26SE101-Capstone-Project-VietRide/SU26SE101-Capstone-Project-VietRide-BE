using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorLedgerActorProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_display_name",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_email",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_role",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "actor_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValueSql: "'SYSTEM'");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE vietride_payment.operator_ledger_entries " +
                "SET actor_type = 'USER' " +
                "WHERE entry_type = 'ADJUSTMENT' AND reference_type = 'MANUAL';");

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_actor_user_id",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                column: "actor_user_id",
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operator_ledger_entries_actor_type",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                sql: "actor_type IN ('USER','SYSTEM')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_operator_ledger_entries_actor_user_id",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operator_ledger_entries_actor_type",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_display_name",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_email",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_role",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_snapshot_resolved",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_type",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                schema: "vietride_payment",
                table: "operator_ledger_entries");
        }
    }
}
