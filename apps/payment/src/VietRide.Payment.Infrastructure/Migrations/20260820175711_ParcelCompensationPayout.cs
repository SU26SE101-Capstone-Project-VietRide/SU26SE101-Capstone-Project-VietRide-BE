using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParcelCompensationPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL cannot remove one enum label in Down(). Keep additions idempotent so
            // migration verification can safely run Down -> Up against the same database.
            migrationBuilder.Sql("ALTER TYPE vietride_payment.operator_ledger_entry_type ADD VALUE IF NOT EXISTS 'PARCEL_COMPENSATION';", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TYPE vietride_payment.operator_wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_COMPENSATION';", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TYPE vietride_payment.platform_wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_COMPENSATION';", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TYPE vietride_payment.wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_COMPENSATION';", suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "parcel_compensation_payouts",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    funding_source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_compensation_payouts", x => x.id);
                    table.CheckConstraint("chk_parcel_compensation_payout_amount", "amount_vnd > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_compensation_payouts_claim_id",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                column: "claim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_compensation_payouts_operator_id_status_created_at",
                schema: "vietride_payment",
                table: "parcel_compensation_payouts",
                columns: new[] { "operator_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_compensation_payouts",
                schema: "vietride_payment");

            // PostgreSQL does not support removing a single value from an enum safely.
            // The PARCEL_COMPENSATION values intentionally remain after rollback.
        }
    }
}
