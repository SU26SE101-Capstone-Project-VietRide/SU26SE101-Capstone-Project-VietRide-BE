using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacySubscriptionWarningFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "warn_sent_at",
                schema: "vietride_identity",
                table: "operator_subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "warn_sent_at",
                schema: "vietride_identity",
                table: "operator_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
