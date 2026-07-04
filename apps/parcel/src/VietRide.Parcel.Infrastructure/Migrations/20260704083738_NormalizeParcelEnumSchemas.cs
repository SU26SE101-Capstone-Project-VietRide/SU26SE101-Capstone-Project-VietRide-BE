using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeParcelEnumSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS vietride_parcel.idx_parcels_additional_payment_deadline;
                DROP INDEX IF EXISTS vietride_parcel.idx_parcels_status_updated_at;
                DROP INDEX IF EXISTS vietride_parcel.idx_outbox_events_status_created;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN delivery_method DROP DEFAULT;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN status TYPE vietride_parcel.parcel_status
                        USING status::text::vietride_parcel.parcel_status,
                    ALTER COLUMN size_category TYPE vietride_parcel.parcel_size_category
                        USING size_category::text::vietride_parcel.parcel_size_category,
                    ALTER COLUMN review_decision TYPE vietride_parcel.parcel_review_decision
                        USING review_decision::text::vietride_parcel.parcel_review_decision,
                    ALTER COLUMN delivery_method TYPE vietride_parcel.parcel_delivery_method
                        USING delivery_method::text::vietride_parcel.parcel_delivery_method;

                ALTER TABLE vietride_parcel.parcel_route_fares
                    ALTER COLUMN size_category TYPE vietride_parcel.parcel_size_category
                        USING size_category::text::vietride_parcel.parcel_size_category;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status TYPE vietride_parcel.outbox_event_status
                        USING status::text::vietride_parcel.outbox_event_status;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN delivery_method SET DEFAULT 'TERMINAL_PICKUP'::vietride_parcel.parcel_delivery_method;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_parcel.outbox_event_status;

                CREATE INDEX idx_parcels_additional_payment_deadline
                    ON vietride_parcel.parcels (additional_payment_deadline)
                    WHERE status = 'PENDING_ADDITIONAL_PAYMENT'::vietride_parcel.parcel_status;

                CREATE INDEX idx_parcels_status_updated_at
                    ON vietride_parcel.parcels (status, updated_at)
                    WHERE status IN (
                        'PENDING'::vietride_parcel.parcel_status,
                        'PENDING_ADDITIONAL_PAYMENT'::vietride_parcel.parcel_status,
                        'PENDING_OPERATOR_REVIEW'::vietride_parcel.parcel_status,
                        'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status,
                        'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status,
                        'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status,
                        'DELIVERY_REJECTED'::vietride_parcel.parcel_status,
                        'TRANSFER_ESCALATED'::vietride_parcel.parcel_status);

                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_parcel.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_parcel.outbox_event_status,
                        'PUBLISHING'::vietride_parcel.outbox_event_status,
                        'FAILED'::vietride_parcel.outbox_event_status);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS vietride_parcel.idx_parcels_additional_payment_deadline;
                DROP INDEX IF EXISTS vietride_parcel.idx_parcels_status_updated_at;
                DROP INDEX IF EXISTS vietride_parcel.idx_outbox_events_status_created;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN delivery_method DROP DEFAULT;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN status TYPE public.parcel_status
                        USING status::text::public.parcel_status,
                    ALTER COLUMN size_category TYPE public.parcel_size_category
                        USING size_category::text::public.parcel_size_category,
                    ALTER COLUMN review_decision TYPE public.parcel_review_decision
                        USING review_decision::text::public.parcel_review_decision,
                    ALTER COLUMN delivery_method TYPE public.parcel_delivery_method
                        USING delivery_method::text::public.parcel_delivery_method;

                ALTER TABLE vietride_parcel.parcel_route_fares
                    ALTER COLUMN size_category TYPE public.parcel_size_category
                        USING size_category::text::public.parcel_size_category;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status TYPE public.outbox_event_status
                        USING status::text::public.outbox_event_status;

                ALTER TABLE vietride_parcel.parcels
                    ALTER COLUMN delivery_method SET DEFAULT 'TERMINAL_PICKUP'::public.parcel_delivery_method;

                ALTER TABLE vietride_parcel.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::public.outbox_event_status;

                CREATE INDEX idx_parcels_additional_payment_deadline
                    ON vietride_parcel.parcels (additional_payment_deadline)
                    WHERE status = 'PENDING_ADDITIONAL_PAYMENT'::public.parcel_status;

                CREATE INDEX idx_parcels_status_updated_at
                    ON vietride_parcel.parcels (status, updated_at)
                    WHERE status IN (
                        'PENDING'::public.parcel_status,
                        'PENDING_ADDITIONAL_PAYMENT'::public.parcel_status,
                        'PENDING_OPERATOR_REVIEW'::public.parcel_status,
                        'PENDING_OPERATOR_ACTION'::public.parcel_status,
                        'PENDING_TRANSFER_CONFIRM'::public.parcel_status,
                        'DELIVERED_PENDING_CONFIRM'::public.parcel_status,
                        'DELIVERY_REJECTED'::public.parcel_status,
                        'TRANSFER_ESCALATED'::public.parcel_status);

                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_parcel.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::public.outbox_event_status,
                        'PUBLISHING'::public.outbox_event_status,
                        'FAILED'::public.outbox_event_status);
                """);
        }
    }
}
