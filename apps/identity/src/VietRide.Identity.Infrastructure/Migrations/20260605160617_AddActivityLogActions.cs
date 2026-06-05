using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'SET_INITIAL_PASSWORD';",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'RESEND_INITIAL_PASSWORD';",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
