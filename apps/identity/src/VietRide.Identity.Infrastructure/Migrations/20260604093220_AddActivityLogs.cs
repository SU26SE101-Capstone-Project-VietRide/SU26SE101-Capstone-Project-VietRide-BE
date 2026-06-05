using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Identity.Domain.Enums;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
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
                        'OPERATOR_WALLET_ADJUSTMENT'
                    );
                EXCEPTION WHEN duplicate_object THEN NULL;
                END
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "activity_logs",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<ActivityLogAction>(type: "activity_log_action", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_logs_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_action_created_at",
                schema: "vietride_identity",
                table: "activity_logs",
                columns: new[] { "action", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_user_id_created_at",
                schema: "vietride_identity",
                table: "activity_logs",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs",
                schema: "vietride_identity");

            migrationBuilder.Sql("DROP TYPE IF EXISTS activity_log_action;");
        }
    }
}
