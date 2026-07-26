using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleSubstitutionTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .Annotation("Npgsql:Enum:booking_pending_action_reason", "ROUTE_CHANGE,SEAT_DOWNGRADE,SCHEDULE_CHANGE,PENDING_SEAT_ASSIGNMENT,STOP_DISABLED")
                .Annotation("Npgsql:Enum:booking_pending_action_resolved", "ACCEPTED,REJECTED,AUTO_FALLBACK_DESTINATION,AUTO_CANCELLED_NO_SEAT,OPERATOR_RESOLVED,SUPERSEDED")
                .Annotation("Npgsql:Enum:booking_pending_action_severity", "MEDIUM,MAJOR")
                .Annotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .Annotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .Annotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .Annotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .Annotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .Annotation("Npgsql:Enum:vietride_booking.booking_transfer_confirmation_status", "PENDING_CONFIRM,CONFIRMED,NOT_REQUIRED")
                .Annotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .Annotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT")
                .OldAnnotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_reason", "ROUTE_CHANGE,SEAT_DOWNGRADE,SCHEDULE_CHANGE,PENDING_SEAT_ASSIGNMENT,STOP_DISABLED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_resolved", "ACCEPTED,REJECTED,AUTO_FALLBACK_DESTINATION,AUTO_CANCELLED_NO_SEAT,OPERATOR_RESOLVED,SUPERSEDED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_severity", "MEDIUM,MAJOR")
                .OldAnnotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .OldAnnotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");

            migrationBuilder.AlterColumn<string>(
                name: "seat_number",
                schema: "vietride_booking",
                table: "passengers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "booking_transfers",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmation_status = table.Column<int>(type: "vietride_booking.booking_transfer_confirmation_status", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    transferred_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_transfers", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_transfers_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_transfers_passengers_passenger_id",
                        column: x => x.passenger_id,
                        principalSchema: "vietride_booking",
                        principalTable: "passengers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_transfers_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "vietride_booking",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_booking_id",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_new_trip_id",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "new_trip_id");

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_original_trip_id",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "original_trip_id");

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_passenger_id",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "passenger_id");

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_ticket_id",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "uq_booking_transfers_passenger_trip_pair",
                schema: "vietride_booking",
                table: "booking_transfers",
                columns: new[] { "passenger_id", "original_trip_id", "new_trip_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_booking.passengers AS passenger
                SET seat_number = COALESCE(
                    (
                        SELECT transfer.new_seat_number
                        FROM vietride_booking.booking_transfers AS transfer
                        WHERE transfer.passenger_id = passenger.id
                          AND transfer.new_seat_number IS NOT NULL
                        ORDER BY transfer.transferred_at DESC, transfer.id DESC
                        LIMIT 1
                    ),
                    (
                        SELECT transfer.original_seat_number
                        FROM vietride_booking.booking_transfers AS transfer
                        WHERE transfer.passenger_id = passenger.id
                          AND transfer.original_seat_number IS NOT NULL
                        ORDER BY transfer.transferred_at DESC, transfer.id DESC
                        LIMIT 1
                    )
                )
                WHERE passenger.seat_number IS NULL;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_booking.passengers
                        WHERE seat_number IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot restore passengers.seat_number NOT NULL: seat_number remains NULL';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "booking_transfers",
                schema: "vietride_booking");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .Annotation("Npgsql:Enum:booking_pending_action_reason", "ROUTE_CHANGE,SEAT_DOWNGRADE,SCHEDULE_CHANGE,PENDING_SEAT_ASSIGNMENT,STOP_DISABLED")
                .Annotation("Npgsql:Enum:booking_pending_action_resolved", "ACCEPTED,REJECTED,AUTO_FALLBACK_DESTINATION,AUTO_CANCELLED_NO_SEAT,OPERATOR_RESOLVED,SUPERSEDED")
                .Annotation("Npgsql:Enum:booking_pending_action_severity", "MEDIUM,MAJOR")
                .Annotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .Annotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .Annotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .Annotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .Annotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .Annotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .Annotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT")
                .OldAnnotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_reason", "ROUTE_CHANGE,SEAT_DOWNGRADE,SCHEDULE_CHANGE,PENDING_SEAT_ASSIGNMENT,STOP_DISABLED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_resolved", "ACCEPTED,REJECTED,AUTO_FALLBACK_DESTINATION,AUTO_CANCELLED_NO_SEAT,OPERATOR_RESOLVED,SUPERSEDED")
                .OldAnnotation("Npgsql:Enum:booking_pending_action_severity", "MEDIUM,MAJOR")
                .OldAnnotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .OldAnnotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:vietride_booking.booking_transfer_confirmation_status", "PENDING_CONFIRM,CONFIRMED,NOT_REQUIRED")
                .OldAnnotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");

            migrationBuilder.AlterColumn<string>(
                name: "seat_number",
                schema: "vietride_booking",
                table: "passengers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
