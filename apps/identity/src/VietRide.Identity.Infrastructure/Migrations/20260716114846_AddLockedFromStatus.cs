using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLockedFromStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<VietRide.Identity.Domain.Enums.UserStatus>(
                name: "locked_from_status",
                schema: "vietride_identity",
                table: "users",
                type: "user_status",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE vietride_identity.users SET locked_from_status = 'ACTIVE' WHERE status = 'LOCKED' AND locked_from_status IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users",
                sql: "((status = 'LOCKED' AND locked_from_status IN ('ACTIVE', 'PENDING_EMAIL_VERIFICATION')) OR (status <> 'LOCKED' AND locked_from_status IS NULL))");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_locked_from_status",
                schema: "vietride_identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "locked_from_status",
                schema: "vietride_identity",
                table: "users");

        }
    }
}
