using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelAdditionalEnumValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,PARCEL_ADDITIONAL,TOP_UP,SUBSCRIPTION")
                .Annotation("Npgsql:Enum:payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,PARCEL_ADDITIONAL_PAYMENT,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:wallet_transaction_type", "CREDIT,DEBIT");

            migrationBuilder.Sql("ALTER TYPE vietride_payment.payment_reference_type ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL';");
            migrationBuilder.Sql("ALTER TYPE vietride_payment.wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT';");
            migrationBuilder.Sql("ALTER TYPE vietride_payment.platform_wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT_HOLD';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL does not support ALTER TYPE ... DROP VALUE.
            // This migration only ADDS enum members that do not break existing rows.
            // This is an intentional exception to the reversible-migration rule in AGENTS_DOTNET.md.
            // Revert by restoring from a pre-migration backup.
        }
    }
}
