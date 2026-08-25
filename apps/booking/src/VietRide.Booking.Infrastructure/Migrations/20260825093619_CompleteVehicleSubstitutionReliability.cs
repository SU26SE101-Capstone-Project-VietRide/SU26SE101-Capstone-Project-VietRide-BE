using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteVehicleSubstitutionReliability : Migration
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
                .Annotation("Npgsql:Enum:vietride_booking.booking_transfer_confirmation_status", "PENDING_CONFIRM,ESCALATED,CONFIRMED,NOT_REQUIRED")
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

            migrationBuilder.AddColumn<bool>(
                name: "is_seat_downgrade",
                schema: "vietride_booking",
                table: "booking_transfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "new_seat_type",
                schema: "vietride_booking",
                table: "booking_transfers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_seat_type",
                schema: "vietride_booking",
                table: "booking_transfers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_booking_transfers_pending_confirm_transferred_at",
                schema: "vietride_booking",
                table: "booking_transfers",
                column: "transferred_at",
                filter: "confirmation_status = 'PENDING_CONFIRM'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_booking_transfers_pending_confirm_transferred_at",
                schema: "vietride_booking",
                table: "booking_transfers");

            migrationBuilder.DropColumn(
                name: "is_seat_downgrade",
                schema: "vietride_booking",
                table: "booking_transfers");

            migrationBuilder.DropColumn(
                name: "new_seat_type",
                schema: "vietride_booking",
                table: "booking_transfers");

            migrationBuilder.DropColumn(
                name: "original_seat_type",
                schema: "vietride_booking",
                table: "booking_transfers");

            migrationBuilder.Sql(
                """
                UPDATE vietride_booking.booking_transfers
                SET confirmation_status = 'PENDING_CONFIRM'
                WHERE confirmation_status = 'ESCALATED';

                ALTER TYPE vietride_booking.booking_transfer_confirmation_status
                    RENAME TO booking_transfer_confirmation_status_old;
                CREATE TYPE vietride_booking.booking_transfer_confirmation_status AS ENUM
                    ('PENDING_CONFIRM', 'CONFIRMED', 'NOT_REQUIRED');
                ALTER TABLE vietride_booking.booking_transfers
                    ALTER COLUMN confirmation_status TYPE
                        vietride_booking.booking_transfer_confirmation_status
                    USING confirmation_status::text::vietride_booking.booking_transfer_confirmation_status;
                DROP TYPE vietride_booking.booking_transfer_confirmation_status_old;
                """);

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
                .OldAnnotation("Npgsql:Enum:vietride_booking.booking_transfer_confirmation_status", "PENDING_CONFIRM,CONFIRMED,NOT_REQUIRED")
                .OldAnnotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");
        }
    }
}
