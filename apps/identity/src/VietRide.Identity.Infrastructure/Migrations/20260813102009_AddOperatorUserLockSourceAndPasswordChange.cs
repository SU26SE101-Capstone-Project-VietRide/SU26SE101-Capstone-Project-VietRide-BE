using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorUserLockSourceAndPasswordChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TYPE public.user_lock_source AS ENUM ('AUTOMATIC_LOGIN_FAILURE', 'OPERATOR_ADMIN', 'SYSTEM_ADMIN', 'LEGACY_UNKNOWN');");

            migrationBuilder.Sql(
                "ALTER TYPE public.refresh_token_revoke_reason ADD VALUE IF NOT EXISTS 'PASSWORD_CHANGE';",
                suppressTransaction: true);

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "lock_source",
                schema: "vietride_identity",
                table: "users",
                type: "public.user_lock_source",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE vietride_identity.users SET lock_source = 'LEGACY_UNKNOWN' WHERE status = 'LOCKED' AND lock_source IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users",
                sql: "((status = 'LOCKED' AND locked_from_status IN ('ACTIVE', 'PENDING_EMAIL_VERIFICATION') AND lock_source IS NOT NULL) OR (status <> 'LOCKED' AND locked_from_status IS NULL AND lock_source IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lock_source",
                schema: "vietride_identity",
                table: "users");

            migrationBuilder.Sql("DROP TYPE public.user_lock_source;");

            migrationBuilder.Sql(
                "UPDATE vietride_identity.refresh_tokens SET revoked_reason = 'PASSWORD_RESET' WHERE revoked_reason = 'PASSWORD_CHANGE';");
            migrationBuilder.Sql(
                "ALTER TABLE vietride_identity.refresh_tokens ALTER COLUMN revoked_reason TYPE text USING revoked_reason::text;");
            migrationBuilder.Sql("DROP TYPE public.refresh_token_revoke_reason;");
            migrationBuilder.Sql(
                "CREATE TYPE public.refresh_token_revoke_reason AS ENUM ('NORMAL_ROTATION', 'REUSE_DETECTED', 'USER_LOGOUT', 'ADMIN_REVOKE', 'PASSWORD_RESET');");
            migrationBuilder.Sql(
                "ALTER TABLE vietride_identity.refresh_tokens ALTER COLUMN revoked_reason TYPE public.refresh_token_revoke_reason USING revoked_reason::public.refresh_token_revoke_reason;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users",
                sql: "((status = 'LOCKED' AND locked_from_status IN ('ACTIVE', 'PENDING_EMAIL_VERIFICATION')) OR (status <> 'LOCKED' AND locked_from_status IS NULL))");
        }
    }
}
