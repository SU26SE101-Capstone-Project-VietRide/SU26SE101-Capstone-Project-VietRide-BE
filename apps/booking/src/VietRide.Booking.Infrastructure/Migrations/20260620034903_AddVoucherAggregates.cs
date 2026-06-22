using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enum types — must exist before any table that references them. Created via explicit
            // SQL to mirror InitBookingSchema (booking_status et al. are created the same way and
            // live as a SINGLE copy in the search_path schema). We deliberately do NOT also emit
            // `AlterDatabase().Annotation("Npgsql:Enum:voucher_*")` here: that path would create a
            // SECOND copy of each enum in the model's default schema (vietride_booking), leaving the
            // type name ambiguous across two schemas and breaking every enum write at runtime
            // ("More than one PostgreSQL type was found with the name voucher_funding_type"). One
            // creator only — the raw SQL below — exactly as booking_status is created.
            migrationBuilder.Sql("CREATE TYPE voucher_type AS ENUM ('PERCENT_OFF', 'FIXED_AMOUNT');");
            migrationBuilder.Sql("CREATE TYPE voucher_funding_type AS ENUM ('VIETRIDE_FUNDED', 'OPERATOR_FUNDED');");
            migrationBuilder.Sql("CREATE TYPE operator_voucher_consent_status AS ENUM ('PENDING', 'ACCEPTED', 'REJECTED');");

            migrationBuilder.CreateTable(
                name: "vouchers",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    type = table.Column<int>(type: "voucher_type", nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false),
                    min_order_amount = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    max_discount_amount = table.Column<long>(type: "bigint", nullable: true),
                    total_usage_limit = table.Column<int>(type: "integer", nullable: true),
                    per_user_limit = table.Column<int>(type: "integer", nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applicable_operator_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: true),
                    applicable_route_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: true),
                    funding_type = table.Column<int>(type: "voucher_funding_type", nullable: false),
                    owner_operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                    table.CheckConstraint("chk_vouchers_min_order_non_negative", "min_order_amount >= 0");
                    table.CheckConstraint("chk_vouchers_operator_owned_funding", "owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'::voucher_funding_type");
                    table.CheckConstraint("chk_vouchers_validity_window", "valid_until > valid_from");
                    table.CheckConstraint("chk_vouchers_value_positive", "value > 0");
                });

            migrationBuilder.CreateTable(
                name: "operator_voucher_consents",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "operator_voucher_consent_status", nullable: false, defaultValueSql: "'PENDING'"),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reject_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_voucher_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_operator_voucher_consents_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "vietride_booking",
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "voucher_usages",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount_amount = table.Column<long>(type: "bigint", nullable: false),
                    funded_by = table.Column<int>(type: "voucher_funding_type", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_voucher_usages_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_voucher_usages_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "vietride_booking",
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_operator_voucher_consents_operator_status",
                schema: "vietride_booking",
                table: "operator_voucher_consents",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_operator_voucher_consents_status",
                schema: "vietride_booking",
                table: "operator_voucher_consents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_operator_voucher_consents_voucher_id",
                schema: "vietride_booking",
                table: "operator_voucher_consents",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "uq_operator_voucher_consents_operator_voucher",
                schema: "vietride_booking",
                table: "operator_voucher_consents",
                columns: new[] { "operator_id", "voucher_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_voucher_usages_booking_id",
                schema: "vietride_booking",
                table: "voucher_usages",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "idx_voucher_usages_voucher_group",
                schema: "vietride_booking",
                table: "voucher_usages",
                columns: new[] { "voucher_id", "booking_group_id" },
                filter: "booking_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_voucher_usages_voucher_user",
                schema: "vietride_booking",
                table: "voucher_usages",
                columns: new[] { "voucher_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "idx_vouchers_active_validity",
                schema: "vietride_booking",
                table: "vouchers",
                column: "valid_until",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_vouchers_owner_operator",
                schema: "vietride_booking",
                table: "vouchers",
                column: "owner_operator_id",
                filter: "owner_operator_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_vouchers_code",
                schema: "vietride_booking",
                table: "vouchers",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_voucher_consents",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "voucher_usages",
                schema: "vietride_booking");

            migrationBuilder.DropTable(
                name: "vouchers",
                schema: "vietride_booking");

            migrationBuilder.Sql("DROP TYPE operator_voucher_consent_status;");
            migrationBuilder.Sql("DROP TYPE voucher_funding_type;");
            migrationBuilder.Sql("DROP TYPE voucher_type;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .Annotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .Annotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .OldAnnotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .OldAnnotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");
        }
    }
}
