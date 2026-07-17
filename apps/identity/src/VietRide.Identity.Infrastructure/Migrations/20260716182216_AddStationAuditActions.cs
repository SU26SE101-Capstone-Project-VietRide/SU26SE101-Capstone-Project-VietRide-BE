using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStationAuditActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'STATION_MERGED';",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'STATION_NORMALIZED';",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM vietride_identity.activity_logs
                        WHERE action::text IN ('STATION_MERGED', 'STATION_NORMALIZED')
                    ) THEN
                        RAISE EXCEPTION 'Cannot remove Station audit actions while activity logs use them'
                            USING ERRCODE = '55000';
                    END IF;
                END $$;

                ALTER TYPE activity_log_action RENAME TO activity_log_action_day40;

                CREATE TYPE activity_log_action AS ENUM (
                    'LOGIN', 'LOGOUT', 'BOOK_TICKET', 'CANCEL_TICKET',
                    'UPDATE_PROFILE', 'CHANGE_PASSWORD', 'COMPLETE_PROFILE',
                    'CREATE_OPERATOR', 'APPROVE_OPERATOR', 'REJECT_OPERATOR',
                    'LOCK_USER', 'UNLOCK_USER',
                    'VEHICLE_SUBSTITUTION_TRIGGERED',
                    'DRIVER_SCHEDULE_EDIT', 'VEHICLE_SWAP',
                    'TRIP_COMPLETED_MANUAL',
                    'PARCEL_UNLOAD_OVERRIDE', 'PARCEL_DELIVERY_RESEND',
                    'PARCEL_MANUAL_CONFIRM',
                    'TRIP_SETTLEMENT_MANUAL',
                    'OPERATOR_WALLET_ADJUSTMENT',
                    'SET_INITIAL_PASSWORD', 'RESEND_INITIAL_PASSWORD'
                );

                ALTER TABLE vietride_identity.activity_logs
                    ALTER COLUMN action TYPE activity_log_action
                    USING action::text::activity_log_action;

                DROP TYPE activity_log_action_day40;
                """);
        }
    }
}
