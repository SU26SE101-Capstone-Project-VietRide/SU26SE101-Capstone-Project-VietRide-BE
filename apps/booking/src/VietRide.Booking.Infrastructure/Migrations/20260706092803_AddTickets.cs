using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .Annotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .Annotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .Annotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .Annotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .Annotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .Annotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .Annotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT")
                .OldAnnotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .OldAnnotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .OldAnnotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE t.typname = 'ticket_status'
                          AND n.nspname = 'public'
                    ) THEN
                        CREATE TYPE public.ticket_status AS ENUM (
                            'PENDING_PAYMENT',
                            'ISSUED',
                            'USED',
                            'CANCELLED',
                            'REFUNDED',
                            'EXPIRED'
                        );
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "vietride_booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "public.ticket_status", nullable: false, defaultValueSql: "'PENDING_PAYMENT'::public.ticket_status"),
                    fare_amount = table.Column<long>(type: "bigint", nullable: false),
                    discount_amount = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    paid_amount = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_tickets_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "vietride_booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tickets_passengers_passenger_id",
                        column: x => x.passenger_id,
                        principalSchema: "vietride_booking",
                        principalTable: "passengers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_booking_status",
                schema: "vietride_booking",
                table: "tickets",
                columns: new[] { "booking_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_seat_number",
                schema: "vietride_booking",
                table: "tickets",
                column: "seat_number");

            migrationBuilder.CreateIndex(
                name: "uq_tickets_passenger_id",
                schema: "vietride_booking",
                table: "tickets",
                column: "passenger_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tickets_ticket_code",
                schema: "vietride_booking",
                table: "tickets",
                column: "ticket_code",
                unique: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'vietride_booking'
                          AND table_name = 'booking_transfers'
                    ) THEN
                        ALTER TABLE vietride_booking.booking_transfers
                            ADD COLUMN IF NOT EXISTS ticket_id uuid;

                        CREATE INDEX IF NOT EXISTS idx_booking_transfers_ticket_id
                            ON vietride_booking.booking_transfers (ticket_id);

                        IF NOT EXISTS (
                            SELECT 1
                            FROM pg_constraint
                            WHERE conname = 'fk_booking_transfers_tickets_ticket_id'
                        ) THEN
                            ALTER TABLE vietride_booking.booking_transfers
                                ADD CONSTRAINT fk_booking_transfers_tickets_ticket_id
                                FOREIGN KEY (ticket_id)
                                REFERENCES vietride_booking.tickets(id)
                                ON DELETE RESTRICT;
                        END IF;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'vietride_booking'
                          AND table_name = 'booking_transfers'
                    ) THEN
                        IF EXISTS (
                            SELECT 1
                            FROM pg_constraint
                            WHERE conname = 'fk_booking_transfers_tickets_ticket_id'
                        ) THEN
                            ALTER TABLE vietride_booking.booking_transfers
                                DROP CONSTRAINT fk_booking_transfers_tickets_ticket_id;
                        END IF;

                        DROP INDEX IF EXISTS vietride_booking.idx_booking_transfers_ticket_id;

                        ALTER TABLE vietride_booking.booking_transfers
                            DROP COLUMN IF EXISTS ticket_id;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "vietride_booking");

            migrationBuilder.Sql("DROP TYPE IF EXISTS public.ticket_status;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .Annotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .Annotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .Annotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .Annotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .Annotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .Annotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT")
                .OldAnnotation("Npgsql:Enum:booking_cancellation_reason", "USER_INITIATED,OPERATOR_CANCELLED_TRIP,OPERATOR_DISRUPTED_IN_PROGRESS,SCHEDULE_CHANGED,ROUTE_CHANGED_REFUSED,VEHICLE_SUBSTITUTION_DOWNGRADE,VEHICLE_SUBSTITUTION_NO_SEAT,STOP_DISABLED_REFUSED")
                .OldAnnotation("Npgsql:Enum:booking_status", "PENDING_PAYMENT,CONFIRMED,COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:operator_voucher_consent_status", "PENDING,ACCEPTED,REJECTED")
                .OldAnnotation("Npgsql:Enum:passenger_boarding_status", "PENDING,BOARDED,NO_SHOW")
                .OldAnnotation("Npgsql:Enum:public.ticket_status", "PENDING_PAYMENT,ISSUED,USED,CANCELLED,REFUNDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:trip_direction", "OUTBOUND,RETURN")
                .OldAnnotation("Npgsql:Enum:vietride_booking.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:voucher_funding_type", "VIETRIDE_FUNDED,OPERATOR_FUNDED")
                .OldAnnotation("Npgsql:Enum:voucher_type", "PERCENT_OFF,FIXED_AMOUNT");
        }
    }
}
