using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParcelSettlementV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "actual_size_category",
                schema: "vietride_parcel",
                table: "parcels",
                type: "vietride_parcel.parcel_size_category",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "balance_paid_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<Guid>(
                name: "balance_payment_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "balance_required_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "checked_in_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deposit_paid_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<Guid>(
                name: "deposit_payment_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deposit_required_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<decimal>(
                name: "dim_weight_factor",
                schema: "vietride_parcel",
                table: "parcels",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 6000m);

            migrationBuilder.AddColumn<long>(
                name: "discount_amount_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "estimated_gross_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<int>(
                name: "estimated_size_category",
                schema: "vietride_parcel",
                table: "parcels",
                type: "vietride_parcel.parcel_size_category",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "estimated_total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "final_gross_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "final_total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "forfeited_deposit_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "latest_check_in_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "load_cutoff_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "minimum_price_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<int>(
                name: "pending_action_resume_status",
                schema: "vietride_parcel",
                table: "parcels",
                type: "vietride_parcel.parcel_status",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "price_per_kg_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "refund_due_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<long>(
                name: "refunded_amount_vnd",
                schema: "vietride_parcel",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reweighed_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reweighed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "settlement_policy_version",
                schema: "vietride_parcel",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcels
                SET estimated_size_category = size_category,
                    actual_size_category = CASE
                        WHEN actual_weight_kg IS NOT NULL THEN size_category
                        ELSE NULL
                    END,
                    estimated_gross_price_vnd = total_price_vnd,
                    discount_amount_vnd = discount_amount,
                    estimated_total_price_vnd = total_price_vnd,
                    final_gross_price_vnd = CASE
                        WHEN actual_weight_kg IS NOT NULL THEN total_price_vnd
                        ELSE 0
                    END,
                    final_total_price_vnd = CASE
                        WHEN actual_weight_kg IS NOT NULL THEN total_price_vnd
                        ELSE 0
                    END,
                    deposit_required_vnd = deposit_amount,
                    deposit_paid_vnd = CASE
                        WHEN status::text IN (
                            'PENDING', 'PENDING_ADDITIONAL_PAYMENT', 'LOADED', 'IN_TRANSIT',
                            'PENDING_TRANSFER_CONFIRM', 'TRANSFER_ESCALATED', 'UNLOADED',
                            'DELIVERED_PENDING_CONFIRM', 'DELIVERY_CONFIRMED', 'DELIVERY_REJECTED',
                            'RETURN_INITIATED', 'RETURNED', 'PENDING_OPERATOR_ACTION')
                        THEN deposit_amount
                        ELSE 0
                    END,
                    balance_required_vnd = CASE
                        WHEN status::text = 'PENDING_ADDITIONAL_PAYMENT' THEN additional_amount
                        ELSE 0
                    END,
                    balance_paid_vnd = CASE
                        WHEN additional_amount > 0
                         AND status::text IN (
                            'LOADED', 'IN_TRANSIT', 'PENDING_TRANSFER_CONFIRM', 'TRANSFER_ESCALATED',
                            'UNLOADED', 'DELIVERED_PENDING_CONFIRM', 'DELIVERY_CONFIRMED',
                            'DELIVERY_REJECTED', 'RETURN_INITIATED', 'RETURNED')
                        THEN additional_amount
                        ELSE 0
                    END,
                    refund_due_vnd = refund_amount,
                    refunded_amount_vnd = 0,
                    forfeited_deposit_vnd = 0,
                    balance_payment_id = additional_payment_id,
                    final_payment_deadline = additional_payment_deadline,
                    dim_weight_factor = 6000,
                    settlement_policy_version = 1;

                UPDATE vietride_parcel.parcels
                SET status = CASE status::text
                    WHEN 'PENDING' THEN 'RESERVED'::vietride_parcel.parcel_status
                    WHEN 'PENDING_ADDITIONAL_PAYMENT' THEN 'PENDING_FINAL_PAYMENT'::vietride_parcel.parcel_status
                    ELSE status
                END;
                """,
                suppressTransaction: true);

            migrationBuilder.AlterColumn<int>(
                name: "estimated_size_category",
                schema: "vietride_parcel",
                table: "parcels",
                type: "vietride_parcel.parcel_size_category",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "vietride_parcel.parcel_size_category",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "idx_parcels_status_updated_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_status_updated_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "status", "updated_at" },
                filter: "status IN ('PENDING_PAYMENT'::vietride_parcel.parcel_status, 'RESERVED'::vietride_parcel.parcel_status, 'CHECKED_IN'::vietride_parcel.parcel_status, 'PENDING_FINAL_PAYMENT'::vietride_parcel.parcel_status, 'READY_TO_LOAD'::vietride_parcel.parcel_status, 'PENDING_OPERATOR_REVIEW'::vietride_parcel.parcel_status, 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status, 'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status, 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status, 'DELIVERY_REJECTED'::vietride_parcel.parcel_status, 'TRANSFER_ESCALATED'::vietride_parcel.parcel_status)");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_balance_payment_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "balance_payment_id",
                filter: "balance_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_deposit_payment_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "deposit_payment_id",
                filter: "deposit_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels",
                column: "final_payment_deadline",
                filter: "status = 'PENDING_FINAL_PAYMENT'::vietride_parcel.parcel_status AND final_payment_deadline IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_latest_check_in_at",
                schema: "vietride_parcel",
                table: "parcels",
                column: "latest_check_in_at",
                filter: "status = 'RESERVED'::vietride_parcel.parcel_status AND latest_check_in_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_settlement_amounts_non_negative",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "estimated_gross_price_vnd >= 0 AND final_gross_price_vnd >= 0 AND discount_amount_vnd >= 0 AND estimated_total_price_vnd >= 0 AND final_total_price_vnd >= 0 AND deposit_required_vnd >= 0 AND deposit_paid_vnd >= 0 AND balance_required_vnd >= 0 AND balance_paid_vnd >= 0 AND refund_due_vnd >= 0 AND refunded_amount_vnd >= 0 AND forfeited_deposit_vnd >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_parcels_settlement_policy_version_positive",
                schema: "vietride_parcel",
                table: "parcels",
                sql: "settlement_policy_version > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcels
                SET status = CASE status::text
                    WHEN 'RESERVED' THEN 'PENDING'::vietride_parcel.parcel_status
                    WHEN 'CHECKED_IN' THEN 'PENDING'::vietride_parcel.parcel_status
                    WHEN 'READY_TO_LOAD' THEN 'PENDING'::vietride_parcel.parcel_status
                    WHEN 'PENDING_FINAL_PAYMENT' THEN 'PENDING_ADDITIONAL_PAYMENT'::vietride_parcel.parcel_status
                    ELSE status
                END,
                pending_action_resume_status = NULL;
                """,
                suppressTransaction: true);

            migrationBuilder.DropIndex(
                name: "idx_parcels_status_updated_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_status_updated_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "status", "updated_at" },
                filter: "status IN ('PENDING'::vietride_parcel.parcel_status, 'PENDING_ADDITIONAL_PAYMENT'::vietride_parcel.parcel_status, 'PENDING_OPERATOR_REVIEW'::vietride_parcel.parcel_status, 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status, 'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status, 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status, 'DELIVERY_REJECTED'::vietride_parcel.parcel_status, 'TRANSFER_ESCALATED'::vietride_parcel.parcel_status)");

            migrationBuilder.DropIndex(
                name: "idx_parcels_balance_payment_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropIndex(
                name: "idx_parcels_deposit_payment_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropIndex(
                name: "idx_parcels_final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropIndex(
                name: "idx_parcels_latest_check_in_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_settlement_amounts_non_negative",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropCheckConstraint(
                name: "chk_parcels_settlement_policy_version_positive",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "actual_size_category",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "balance_paid_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "balance_payment_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "balance_required_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "checked_in_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "checked_in_by_user_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "deposit_paid_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "deposit_payment_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "deposit_required_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "dim_weight_factor",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "discount_amount_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_gross_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_size_category",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "estimated_total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "final_gross_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "final_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "final_total_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "forfeited_deposit_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "latest_check_in_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "load_cutoff_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "minimum_price_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "pending_action_resume_status",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "price_per_kg_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "refund_due_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "refunded_amount_vnd",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "reweighed_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "reweighed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "settlement_policy_version",
                schema: "vietride_parcel",
                table: "parcels");

        }
    }
}
