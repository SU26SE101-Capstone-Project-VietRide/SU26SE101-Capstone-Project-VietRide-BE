using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vietride_parcel");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:parcel_delivery_method", "TERMINAL_PICKUP")
                .Annotation("Npgsql:Enum:parcel_review_decision", "PENDING,APPROVED,REJECTED")
                .Annotation("Npgsql:Enum:parcel_size_category", "SMALL,MEDIUM,LARGE,EXTRA_LARGE")
                .Annotation("Npgsql:Enum:parcel_status", "PENDING_OPERATOR_REVIEW,PENDING_PAYMENT,PENDING,PENDING_ADDITIONAL_PAYMENT,LOADED,IN_TRANSIT,PENDING_TRANSFER_CONFIRM,TRANSFER_ESCALATED,UNLOADED,DELIVERED_PENDING_CONFIRM,DELIVERY_CONFIRMED,DELIVERY_REJECTED,RETURN_INITIATED,RETURNED,PENDING_OPERATOR_ACTION,CANCELLED,REJECTED,EXPIRED");

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "outbox_event_status", nullable: false, defaultValueSql: "'PENDING'"),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parcel_route_fares",
                schema: "vietride_parcel",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_category = table.Column<int>(type: "parcel_size_category", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_vnd = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    effective_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_route_fares", x => new { x.route_id, x.size_category });
                    table.CheckConstraint("chk_parcel_route_fares_effective_order", "effective_until IS NULL OR effective_until > effective_from");
                    table.CheckConstraint("chk_parcel_route_fares_price_non_negative", "price_vnd >= 0");
                });

            migrationBuilder.CreateTable(
                name: "parcel_stats",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stat_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_parcels = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_loaded = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_delivered = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_rejected = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_returned = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_revenue = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    total_refunded = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_stats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parcels",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dropoff_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    size_category = table.Column<int>(type: "parcel_size_category", nullable: false),
                    estimated_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    delivery_method = table.Column<string>(type: "parcel_delivery_method", nullable: false, defaultValueSql: "'TERMINAL_PICKUP'"),
                    deposit_amount = table.Column<long>(type: "bigint", nullable: false),
                    additional_amount = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    additional_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    additional_payment_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "parcel_status", nullable: false),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    review_decision = table.Column<int>(type: "parcel_review_decision", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_token = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_token_revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_pending_confirm_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    confirm_note = table.Column<string>(type: "text", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reminder_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transfer_target_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transfer_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transfer_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transfer_confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_reason = table.Column<string>(type: "text", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcels", x => x.id);
                    table.CheckConstraint("chk_parcels_actual_weight_positive", "actual_weight_kg IS NULL OR actual_weight_kg > 0");
                    table.CheckConstraint("chk_parcels_amounts_non_negative", "deposit_amount >= 0 AND additional_amount >= 0");
                    table.CheckConstraint("chk_parcels_weight_positive", "estimated_weight_kg > 0");
                });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_events_status_created",
                schema: "vietride_parcel",
                table: "outbox_events",
                columns: new[] { "status", "created_at" },
                filter: "status IN ('PENDING', 'PUBLISHING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "idx_parcel_route_fares_operator_id",
                schema: "vietride_parcel",
                table: "parcel_route_fares",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "idx_parcel_stats_stat_date",
                schema: "vietride_parcel",
                table: "parcel_stats",
                column: "stat_date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "uq_parcel_stats_operator_date",
                schema: "vietride_parcel",
                table: "parcel_stats",
                columns: new[] { "operator_id", "stat_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_parcels_additional_payment_deadline",
                schema: "vietride_parcel",
                table: "parcels",
                column: "additional_payment_deadline",
                filter: "status = 'PENDING_ADDITIONAL_PAYMENT'");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_additional_payment_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "additional_payment_id",
                filter: "additional_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_confirmed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "confirmed_by_user_id",
                filter: "confirmed_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_operator_id_status",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_parcels_recipient_user_id_created_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "recipient_user_id", "created_at" },
                descending: new[] { false, true },
                filter: "recipient_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_returned_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "returned_by_user_id",
                filter: "returned_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_reviewed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "reviewed_by_user_id",
                filter: "reviewed_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_sender_user_id_created_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "sender_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_parcels_status_updated_at",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "status", "updated_at" },
                filter: "status IN ('PENDING', 'PENDING_ADDITIONAL_PAYMENT', 'PENDING_OPERATOR_REVIEW', 'PENDING_OPERATOR_ACTION', 'PENDING_TRANSFER_CONFIRM', 'DELIVERED_PENDING_CONFIRM', 'DELIVERY_REJECTED', 'TRANSFER_ESCALATED')");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_transfer_confirmed_by_user_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "transfer_confirmed_by_user_id",
                filter: "transfer_confirmed_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_transfer_target_trip_id",
                schema: "vietride_parcel",
                table: "parcels",
                column: "transfer_target_trip_id",
                filter: "transfer_target_trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_parcels_trip_id_status",
                schema: "vietride_parcel",
                table: "parcels",
                columns: new[] { "trip_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_parcels_delivery_token",
                schema: "vietride_parcel",
                table: "parcels",
                column: "delivery_token",
                unique: true,
                filter: "delivery_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_parcels_parcel_code",
                schema: "vietride_parcel",
                table: "parcels",
                column: "parcel_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_route_fares",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcel_stats",
                schema: "vietride_parcel");

            migrationBuilder.DropTable(
                name: "parcels",
                schema: "vietride_parcel");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"pgcrypto\";");
        }
    }
}
